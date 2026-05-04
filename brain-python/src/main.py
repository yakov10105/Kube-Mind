import asyncio
import signal

import structlog
import uvicorn

from src.config import settings
from src.observability.logging_config import configure_logging
from src.observability.tracing import configure_tracing
from src.utils import validate_gcp_credentials

log = structlog.get_logger()


async def main() -> None:
    configure_logging(settings.log_level)
    configure_tracing(settings.otlp_endpoint)

    validate_gcp_credentials()

    # Import after logging is configured so structlog is ready
    from src.grpc_server import serve_grpc
    from src.http_server import app
    from src.graph.graph import create_graph
    from src.services.memory_consolidation import get_memory_consolidation_service
    from src.services.enrichment import ensure_qdrant_collection
    from qdrant_client import AsyncQdrantClient

    # Ensure Qdrant collection exists
    qdrant = AsyncQdrantClient(
        host=settings.qdrant_host, port=settings.qdrant_port, prefer_grpc=True
    )
    await ensure_qdrant_collection(qdrant, settings.qdrant_collection)

    # Start memory consolidation background worker
    mem_svc = get_memory_consolidation_service()
    asyncio.create_task(mem_svc.run())

    graph = create_graph()

    async def incident_handler(incident) -> None:
        from src.services.event_bus import event_bus
        structlog.contextvars.bind_contextvars(incident_id=incident.incident_id)
        async for event in graph.astream(
            {"incident": incident, "messages": [], "stream_events": []},
            stream_mode="values",
        ):
            for ev in event.get("stream_events", []):
                await event_bus.publish(incident.incident_id, ev)

    uvicorn_config = uvicorn.Config(
        app, host="0.0.0.0", port=settings.http_port, loop="asyncio", log_config=None
    )
    server = uvicorn.Server(uvicorn_config)

    loop = asyncio.get_running_loop()
    for sig in (signal.SIGTERM, signal.SIGINT):
        loop.add_signal_handler(sig, server.handle_exit, sig, None)

    log.info("kubemind.starting", grpc_port=settings.grpc_port, http_port=settings.http_port)

    await asyncio.gather(
        serve_grpc(incident_handler, port=settings.grpc_port),
        server.serve(),
    )


if __name__ == "__main__":
    asyncio.run(main())
