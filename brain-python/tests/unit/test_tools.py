"""Unit tests for all four LangGraph tools."""
import json
import pytest
from unittest.mock import AsyncMock, MagicMock, patch


# ── Task 4.1: get_pod_status ──────────────────────────────────────────────────

def test_get_pod_status_returns_real_phase_and_restart_count():
    mock_pod = MagicMock()
    mock_pod.status.phase = "Running"
    mock_pod.status.conditions = []
    cs = MagicMock()
    cs.name = "app"
    cs.ready = True
    cs.restart_count = 3
    cs.state = MagicMock()
    mock_pod.status.container_statuses = [cs]

    with (
        patch("src.tools.kubernetes_tool.k8s_config.load_incluster_config", side_effect=Exception),
        patch("src.tools.kubernetes_tool.k8s_config.load_kube_config"),
        patch("src.tools.kubernetes_tool.client.CoreV1Api") as mock_api,
    ):
        mock_api.return_value.read_namespaced_pod.return_value = mock_pod
        from src.tools.kubernetes_tool import get_pod_status
        result = json.loads(get_pod_status.invoke({"pod_name": "my-pod", "namespace": "default"}))

    assert result["phase"] == "Running"
    assert result["container_statuses"][0]["restart_count"] == 3


def test_get_pod_status_raises_on_404():
    from kubernetes.client.exceptions import ApiException

    with (
        patch("src.tools.kubernetes_tool.k8s_config.load_incluster_config", side_effect=Exception),
        patch("src.tools.kubernetes_tool.k8s_config.load_kube_config"),
        patch("src.tools.kubernetes_tool.client.CoreV1Api") as mock_api,
    ):
        err = ApiException(status=404)
        mock_api.return_value.read_namespaced_pod.side_effect = err
        from src.tools.kubernetes_tool import get_pod_status
        result = json.loads(get_pod_status.invoke({"pod_name": "missing", "namespace": "default"}))

    assert "not found" in result["error"]


# ── Task 4.2: analyze_incident ────────────────────────────────────────────────

def test_analyze_incident_calls_llm_with_correct_content():
    incident = {
        "incident_id": "inc-1",
        "pod_name": "api",
        "pod_namespace": "prod",
        "failure_reason": "OOMKilled",
        "logs": "java.lang.OutOfMemoryError",
        "pod_manifest_json": "{}",
        "deployment_manifest_json": "{}",
    }
    mock_response = MagicMock()
    mock_response.content = json.dumps({
        "rootCause": "OOM",
        "confidence": "High",
        "recommendedAction": "increase memory",
        "supportingEvidence": "OOM in logs",
    })

    with patch("src.tools.diagnostics_tool.ChatVertexAI") as mock_llm_cls:
        mock_llm_cls.return_value.invoke.return_value = mock_response
        from src.tools.diagnostics_tool import analyze_incident
        result = analyze_incident.invoke({"incident_context_json": json.dumps(incident)})

    parsed = json.loads(result)
    assert parsed["rootCause"] == "OOM"
    call_prompt = mock_llm_cls.return_value.invoke.call_args[0][0]
    assert "OOMKilled" in call_prompt
    assert "prod/api" in call_prompt


def test_analyze_incident_falls_back_on_malformed_llm_output():
    mock_response = MagicMock()
    mock_response.content = "not valid json {"

    with patch("src.tools.diagnostics_tool.ChatVertexAI") as mock_llm_cls:
        mock_llm_cls.return_value.invoke.return_value = mock_response
        from src.tools.diagnostics_tool import analyze_incident
        result = analyze_incident.invoke({"incident_context_json": "{}"})

    parsed = json.loads(result)
    assert parsed["confidence"] == "Low"


# ── Task 4.3: is_code_change_safe ─────────────────────────────────────────────

@pytest.mark.parametrize("llm_response,expected", [
    ("YES", "YES"),
    ("yes", "YES"),
    ("NO", "NO"),
    ("no", "NO"),
    ("BLOCKED", "NO"),  # any non-YES → NO
    ("  YES  ", "YES"),
])
def test_is_code_change_safe_verdict(llm_response, expected):
    mock_response = MagicMock()
    mock_response.content = llm_response

    with patch("src.tools.polycheck_tool.ChatVertexAI") as mock_llm_cls:
        mock_llm_cls.return_value.invoke.return_value = mock_response
        from src.tools.polycheck_tool import is_code_change_safe
        result = is_code_change_safe.invoke({"code_change": "resources.limits.memory: 256Mi"})

    assert result == expected


# ── Task 4.4: create_fix_pull_request ────────────────────────────────────────

@pytest.mark.asyncio
async def test_create_fix_pull_request_calls_github_in_order_and_notifies_slack():
    mock_github = AsyncMock()
    mock_github.create_branch = AsyncMock()
    mock_github.create_or_update_file = AsyncMock()
    mock_github.create_pull_request = AsyncMock(return_value="https://github.com/org/repo/pull/42")

    mock_slack = AsyncMock()
    mock_slack.notify = AsyncMock()

    with (
        patch("src.tools.gitops_tool.GitHubService", return_value=mock_github),
        patch("src.tools.gitops_tool.SlackNotificationService", return_value=mock_slack),
        patch("src.tools.gitops_tool.settings") as mock_settings,
    ):
        mock_settings.github_token = "tok"
        mock_settings.slack_webhook_url = "https://hooks.slack.com/test"
        from src.tools.gitops_tool import create_fix_pull_request
        result = await create_fix_pull_request.ainvoke({
            "repository_owner": "org",
            "repository_name": "repo",
            "base_branch": "main",
            "new_branch_name": "fix/oom",
            "commit_message": "fix: increase memory",
            "file_path": "values.yaml",
            "file_content": "memory: 512Mi",
            "pull_request_title": "Fix OOMKilled",
            "pull_request_body": "Increased memory limit",
        })

    assert result == "https://github.com/org/repo/pull/42"
    mock_github.create_branch.assert_awaited_once()
    mock_github.create_or_update_file.assert_awaited_once()
    mock_github.create_pull_request.assert_awaited_once()
    mock_slack.notify.assert_awaited_once()
