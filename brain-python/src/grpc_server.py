import hashlib
from collections.abc import Callable, Awaitable

import grpc
import grpc.aio as aio
import structlog

from generated import incident_pb2, incident_pb2_grpc
from src.config import settings

log = structlog.get_logger()


def stable_dedup_key(incident: incident_pb2.IncidentContext) -> str:
    """Derives a stable Redis key independent of the Observer's timestamp suffix."""
    raw = f"{incident.pod_namespace}/{incident.pod_name}/{incident.failure_reason}"
    return "kubemind:dedup:" + hashlib.sha256(raw.encode()).hexdigest()[:16]


class IncidentServicer(incident_pb2_grpc.IncidentServiceServicer):
    def __init__(self, incident_handler: Callable[..., Awaitable[None]]) -> None:
        self._handler = incident_handler

    async def StreamIncident(self, request_iterator, context):
        structlog.contextvars.clear_contextvars()
        log.info("grpc.stream_started")
        async for incident in request_iterator:
            structlog.contextvars.bind_contextvars(incident_id=incident.incident_id)
            log.info(
                "grpc.incident_received",
                pod_name=incident.pod_name,
                namespace=incident.pod_namespace,
                reason=incident.failure_reason,
            )
            await self._handler(incident)
        log.info("grpc.stream_finished")
        return incident_pb2.StreamIncidentResponse(
            status="Incidents received and processed."
        )


async def serve_grpc(
    incident_handler: Callable[..., Awaitable[None]], port: int
) -> None:
    server = aio.server()
    incident_pb2_grpc.add_IncidentServiceServicer_to_server(
        IncidentServicer(incident_handler), server
    )

    if settings.grpc_tls_server_cert and settings.grpc_tls_server_key:
        with (
            open(settings.grpc_tls_server_cert, "rb") as cert_f,
            open(settings.grpc_tls_server_key, "rb") as key_f,
        ):
            cert = cert_f.read()
            key = key_f.read()
        ca = None
        if settings.grpc_tls_ca_cert:
            with open(settings.grpc_tls_ca_cert, "rb") as ca_f:
                ca = ca_f.read()
        credentials = grpc.ssl_server_credentials(
            [(key, cert)],
            root_certificates=ca,
            require_client_auth=ca is not None,
        )
        server.add_secure_port(f"0.0.0.0:{port}", credentials)
        log.info("grpc.server_started", port=port, tls=True)
    else:
        server.add_insecure_port(f"0.0.0.0:{port}")
        log.info("grpc.server_started", port=port, tls=False)

    await server.start()
    await server.wait_for_termination()
