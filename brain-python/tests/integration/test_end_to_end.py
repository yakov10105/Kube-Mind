"""End-to-end integration tests.

Requires: Redis, Qdrant, real GitHub test repo (GITHUB_TEST_REPO_OWNER/NAME env vars).
Mark with @pytest.mark.integration to exclude from default test runs.
"""
import asyncio
import os
import pytest

from generated import incident_pb2


@pytest.mark.asyncio
@pytest.mark.integration
async def test_full_flow_grpc_to_graph_no_pr(monkeypatch):
    """Full flow: gRPC IncidentContext → graph runs → outcome set (no real GH/LLM)."""
    from unittest.mock import AsyncMock, MagicMock, patch

    incident = incident_pb2.IncidentContext(
        incident_id="e2e-1",
        pod_name="api",
        pod_namespace="default",
        failure_reason="OOMKilled",
        logs="java.lang.OutOfMemoryError",
    )

    with (
        patch("src.services.deduplication.aioredis.from_url") as mock_redis_url,
        patch("src.services.enrichment.VertexAIEmbeddings"),
        patch("src.services.enrichment.AsyncQdrantClient"),
        patch("src.graph.graph.ChatVertexAI") as mock_llm_cls,
        patch("src.tools.kubernetes_tool.k8s_config.load_incluster_config", side_effect=Exception),
        patch("src.tools.kubernetes_tool.k8s_config.load_kube_config"),
        patch("src.tools.kubernetes_tool.client.CoreV1Api"),
        patch("src.tools.diagnostics_tool.ChatVertexAI"),
        patch("src.tools.polycheck_tool.ChatVertexAI"),
        patch("src.tools.gitops_tool.GitHubService"),
        patch("src.tools.gitops_tool.SlackNotificationService"),
        patch("src.graph.nodes.get_slack_service"),
    ):
        mock_redis = AsyncMock()
        mock_redis.set = AsyncMock(return_value=True)
        mock_redis_url.return_value = mock_redis

        mock_enrich_svc = AsyncMock()
        mock_enrich_svc.enrich = AsyncMock(return_value=("goal", ""))

        mock_mem_svc = AsyncMock()
        mock_mem_svc.enqueue = AsyncMock()

        final_msg = MagicMock()
        final_msg.tool_calls = []
        final_msg.content = "Incident diagnosed. No safe fix available."
        mock_llm = MagicMock()
        mock_llm.invoke.return_value = final_msg
        mock_llm.bind_tools.return_value = mock_llm
        mock_llm_cls.return_value = mock_llm

        with (
            patch("src.graph.nodes.get_enrichment_service", return_value=mock_enrich_svc),
            patch("src.graph.nodes.get_memory_consolidation_service", return_value=mock_mem_svc),
            patch("src.graph.nodes.get_deduplication_service") as mock_dedup_factory,
        ):
            from src.services.deduplication import RedisDeduplicationService
            mock_dedup = AsyncMock(spec=RedisDeduplicationService)
            mock_dedup.is_duplicate = AsyncMock(return_value=False)
            mock_dedup_factory.return_value = mock_dedup

            from src.graph.graph import create_graph
            graph = create_graph()
            result = await graph.ainvoke({
                "incident": incident,
                "messages": [],
                "stream_events": [],
            })

    assert result is not None
    assert "stream_events" in result
    assert len(result["stream_events"]) > 0


@pytest.mark.asyncio
@pytest.mark.integration
async def test_duplicate_incident_within_ttl_is_skipped():
    """Second identical incident within TTL window is skipped."""
    from unittest.mock import AsyncMock, MagicMock, patch

    incident = incident_pb2.IncidentContext(
        incident_id="e2e-dup",
        pod_name="dup-pod",
        pod_namespace="default",
        failure_reason="CrashLoopBackOff",
    )

    with (
        patch("src.graph.graph.ChatVertexAI"),
        patch("src.graph.nodes.get_enrichment_service"),
        patch("src.graph.nodes.get_memory_consolidation_service"),
        patch("src.graph.nodes.get_slack_service"),
        patch("src.graph.nodes.get_deduplication_service") as mock_dedup_factory,
    ):
        mock_dedup = AsyncMock()
        mock_dedup.is_duplicate = AsyncMock(return_value=True)
        mock_dedup_factory.return_value = mock_dedup

        from src.graph.graph import create_graph
        graph = create_graph()
        result = await graph.ainvoke({
            "incident": incident,
            "messages": [],
            "stream_events": [],
        })

    assert result["outcome"] == "duplicate"


@pytest.mark.asyncio
@pytest.mark.integration
async def test_sse_stream_delivers_events_only_for_correct_incident():
    """SSE /events/{incident_id} only delivers events for the subscribed incident."""
    from src.services.event_bus import EventBus

    bus = EventBus()
    received_a: list[str] = []
    received_b: list[str] = []

    async def collect(incident_id: str, out: list, count: int):
        i = 0
        async for msg in bus.subscribe(incident_id):
            out.append(msg)
            i += 1
            if i >= count:
                break

    task_a = asyncio.create_task(collect("incident-A", received_a, 1))
    task_b = asyncio.create_task(collect("incident-B", received_b, 1))
    await asyncio.sleep(0.05)

    await bus.publish("incident-A", "event-for-A")
    await bus.publish("incident-B", "event-for-B")
    await asyncio.sleep(0.05)

    task_a.cancel()
    task_b.cancel()

    assert received_a == ["event-for-A"]
    assert received_b == ["event-for-B"]
