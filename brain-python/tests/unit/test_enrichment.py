"""Unit tests for EnrichmentService."""
import pytest
from unittest.mock import AsyncMock, MagicMock, patch


@pytest.fixture
def svc():
    with (
        patch("src.services.enrichment.VertexAIEmbeddings"),
        patch("src.services.enrichment.AsyncQdrantClient"),
    ):
        from src.services.enrichment import EnrichmentService
        service = EnrichmentService()
        service._embedder = AsyncMock()
        service._qdrant = AsyncMock()
        service._collection = "k8s_incidents"
        yield service


@pytest.mark.asyncio
async def test_enrich_returns_enriched_goal_when_qdrant_has_results(svc):
    svc._embedder.aembed_query = AsyncMock(return_value=[0.1] * 768)
    hit = MagicMock()
    hit.payload = {"raw_log": "OOM error", "resolution_action": "increased memory limit"}
    hit.score = 0.97
    svc._qdrant.search = AsyncMock(return_value=[hit])

    enriched, context = await svc.enrich("some log text", "original goal")

    assert "HISTORICAL CONTEXT FROM MEMORY" in enriched
    assert "OOM error" in enriched
    assert "increased memory limit" in enriched
    assert context != ""


@pytest.mark.asyncio
async def test_enrich_returns_original_goal_when_no_qdrant_results(svc):
    svc._embedder.aembed_query = AsyncMock(return_value=[0.1] * 768)
    svc._qdrant.search = AsyncMock(return_value=[])

    enriched, context = await svc.enrich("log text", "original goal")

    assert enriched == "original goal"
    assert context == ""


@pytest.mark.asyncio
async def test_enrich_degrades_gracefully_on_exception(svc):
    svc._embedder.aembed_query = AsyncMock(side_effect=RuntimeError("GCP unavailable"))

    enriched, context = await svc.enrich("log text", "original goal")

    assert enriched == "original goal"
    assert context == ""


@pytest.mark.asyncio
async def test_historical_context_appears_verbatim(svc):
    svc._embedder.aembed_query = AsyncMock(return_value=[0.0] * 768)
    hit = MagicMock()
    hit.payload = {"raw_log": "crash log", "resolution_action": "rollback image"}
    hit.score = 0.99
    svc._qdrant.search = AsyncMock(return_value=[hit])

    _, context = await svc.enrich("crash", "goal")

    assert "crash log" in context
    assert "rollback image" in context
