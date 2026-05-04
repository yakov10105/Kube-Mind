"""Unit tests for the compiled LangGraph."""
import pytest
from unittest.mock import AsyncMock, MagicMock, patch


def _make_incident(incident_id="inc-1", pod="my-pod", ns="default", reason="OOMKilled"):
    inc = MagicMock()
    inc.incident_id = incident_id
    inc.pod_name = pod
    inc.pod_namespace = ns
    inc.failure_reason = reason
    inc.logs = "OOM error in logs"
    inc.cluster_id = ""
    return inc


@pytest.fixture(autouse=True)
def _patch_services(monkeypatch):
    """Patch all external service singletons for graph unit tests."""
    with (
        patch("src.graph.nodes.get_deduplication_service"),
        patch("src.graph.nodes.get_enrichment_service"),
        patch("src.graph.nodes.get_memory_consolidation_service"),
        patch("src.graph.nodes.get_slack_service"),
        patch("src.graph.graph.ChatVertexAI"),
        patch("src.tools.kubernetes_tool.k8s_config.load_incluster_config", side_effect=Exception),
        patch("src.tools.kubernetes_tool.k8s_config.load_kube_config"),
        patch("src.tools.kubernetes_tool.client.CoreV1Api"),
        patch("src.tools.diagnostics_tool.ChatVertexAI"),
        patch("src.tools.polycheck_tool.ChatVertexAI"),
        patch("src.tools.gitops_tool.GitHubService"),
        patch("src.tools.gitops_tool.SlackNotificationService"),
    ):
        yield


@pytest.mark.asyncio
async def test_duplicate_incident_reaches_end_with_duplicate_outcome():
    from src.graph.nodes import get_deduplication_service
    mock_dedup = AsyncMock()
    mock_dedup.is_duplicate = AsyncMock(return_value=True)
    get_deduplication_service.return_value = mock_dedup

    from src.graph.graph import create_graph
    graph = create_graph()

    result = await graph.ainvoke({
        "incident": _make_incident(),
        "messages": [],
        "stream_events": [],
    })

    assert result["outcome"] == "duplicate"


@pytest.mark.asyncio
async def test_safety_blocked_incident_sets_safety_blocked_outcome():
    from src.graph.nodes import get_deduplication_service, get_enrichment_service, get_slack_service
    from src.graph.graph import create_graph, ChatVertexAI as MockLLM

    # Not a duplicate
    mock_dedup = AsyncMock()
    mock_dedup.is_duplicate = AsyncMock(return_value=False)
    get_deduplication_service.return_value = mock_dedup

    # Enrichment returns original goal
    mock_enrich = AsyncMock()
    mock_enrich.enrich = AsyncMock(return_value=("goal text", ""))
    get_enrichment_service.return_value = mock_enrich

    # Slack is silent
    mock_slack = AsyncMock()
    mock_slack.notify = AsyncMock()
    get_slack_service.return_value = mock_slack

    # LLM returns a final message with NO tool calls → routes to route node
    mock_llm_instance = MagicMock()
    final_msg = MagicMock()
    final_msg.tool_calls = []
    final_msg.content = "I cannot proceed safely."
    mock_llm_instance.invoke.return_value = final_msg
    mock_llm_instance.bind_tools.return_value = mock_llm_instance
    MockLLM.return_value = mock_llm_instance

    graph = create_graph()
    result = await graph.ainvoke({
        "incident": _make_incident(),
        "messages": [],
        "stream_events": [],
        "safety_result": "NO",
    })

    assert result.get("outcome") in ("safety_blocked", None)


def test_build_sop_goal_renders_incident_id_and_sop_headers():
    from src.graph.nodes import build_sop_goal
    inc = _make_incident(incident_id="test-inc-99")
    goal = build_sop_goal(inc)
    assert "test-inc-99" in goal
    assert "STANDARD OPERATING PROCEDURE" in goal


def test_build_sop_goal_includes_historical_context():
    from src.graph.nodes import build_sop_goal
    inc = _make_incident()
    goal = build_sop_goal(inc, historical_context="Past fix: increased memory to 512Mi")
    assert "Past fix" in goal
    assert "HISTORICAL CONTEXT" in goal
