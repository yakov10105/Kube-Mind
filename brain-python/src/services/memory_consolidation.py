import asyncio
import uuid
from dataclasses import dataclass

import structlog
from langchain_google_vertexai import VertexAIEmbeddings
from qdrant_client import AsyncQdrantClient
from qdrant_client.models import PointStruct

from src.config import settings

log = structlog.get_logger()


@dataclass
class IncidentResolution:
    incident_id: str
    cluster_id: str
    namespace: str
    raw_log: str
    resolution: str


class MemoryConsolidationService:
    def __init__(self) -> None:
        self._queue: asyncio.Queue[IncidentResolution] = asyncio.Queue(maxsize=100)
        self._embedder = VertexAIEmbeddings(
            model_name="text-embedding-004",
            project=settings.gcp_project_id,
            location=settings.gcp_location,
        )
        self._qdrant = AsyncQdrantClient(
            host=settings.qdrant_host, port=settings.qdrant_port, prefer_grpc=True
        )

    async def enqueue(
        self,
        incident_id: str,
        cluster_id: str,
        namespace: str,
        raw_log: str,
        resolution: str,
    ) -> None:
        await self._queue.put(
            IncidentResolution(
                incident_id=incident_id,
                cluster_id=cluster_id,
                namespace=namespace,
                raw_log=raw_log,
                resolution=resolution,
            )
        )

    async def run(self) -> None:
        """Long-running background consumer. Start via asyncio.create_task()."""
        while True:
            resolution = await self._queue.get()
            try:
                await self._consolidate(resolution)
            except Exception as e:
                log.error(
                    "memory_consolidation.failed",
                    error=str(e),
                    incident_id=resolution.incident_id,
                )
            finally:
                self._queue.task_done()

    async def _consolidate(self, r: IncidentResolution) -> None:
        vector = await self._embedder.aembed_query(r.raw_log)
        results = await self._qdrant.search(
            collection_name=settings.qdrant_collection,
            query_vector=vector,
            limit=1,
            with_payload=False,
        )
        if results and results[0].score >= settings.qdrant_similarity_threshold:
            log.info(
                "memory_consolidation.skipped_duplicate",
                incident_id=r.incident_id,
                score=results[0].score,
            )
            return
        point = PointStruct(
            id=str(uuid.uuid4()),
            vector=vector,
            payload={
                "cluster_id": r.cluster_id,
                "namespace": r.namespace,
                "raw_log": r.raw_log,
                "resolution_action": r.resolution,
                "incident_id": r.incident_id,
            },
        )
        await self._qdrant.upsert(
            collection_name=settings.qdrant_collection, points=[point]
        )
        log.info("memory_consolidation.saved", incident_id=r.incident_id)


_service: MemoryConsolidationService | None = None


def get_memory_consolidation_service() -> MemoryConsolidationService:
    global _service
    if _service is None:
        _service = MemoryConsolidationService()
    return _service
