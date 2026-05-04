import structlog
from langchain_google_vertexai import VertexAIEmbeddings
from qdrant_client import AsyncQdrantClient
from qdrant_client.models import Distance, VectorParams
from src.config import settings

log = structlog.get_logger()


class EnrichmentService:
    def __init__(self) -> None:
        self._embedder = VertexAIEmbeddings(
            model_name="text-embedding-004",
            project=settings.gcp_project_id,
            location=settings.gcp_location,
        )
        self._qdrant = AsyncQdrantClient(
            host=settings.qdrant_host, port=settings.qdrant_port, prefer_grpc=True
        )
        self._collection = settings.qdrant_collection

    async def enrich(self, log_text: str, original_goal: str) -> tuple[str, str]:
        try:
            vector = await self._embedder.aembed_query(log_text)
            results = await self._qdrant.search(
                collection_name=self._collection,
                query_vector=vector,
                limit=3,
                with_payload=True,
            )
            if not results:
                return original_goal, ""
            context_lines = []
            for r in results:
                payload = r.payload or {}
                context_lines.append(
                    f"- Past Incident: {payload.get('raw_log', '')}\n"
                    f"  Resolution: {payload.get('resolution_action', '')}"
                )
            context = "\n".join(context_lines)
            enriched = f"{original_goal}\n\nHISTORICAL CONTEXT FROM MEMORY:\n{context}"
            return enriched, context
        except Exception as e:
            log.warning("enrichment.failed", error=str(e))
            return original_goal, ""


async def ensure_qdrant_collection(qdrant: AsyncQdrantClient, collection: str) -> None:
    existing = [c.name for c in (await qdrant.get_collections()).collections]
    if collection not in existing:
        await qdrant.create_collection(
            collection_name=collection,
            vectors_config=VectorParams(size=768, distance=Distance.COSINE),
        )
        log.info("qdrant.collection_created", collection=collection)


_service: EnrichmentService | None = None


def get_enrichment_service() -> EnrichmentService:
    global _service
    if _service is None:
        _service = EnrichmentService()
    return _service
