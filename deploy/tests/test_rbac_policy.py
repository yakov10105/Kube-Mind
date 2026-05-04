"""CI policy test: Observer ClusterRole must not grant write access to core workload resources.

Run with: pytest deploy/tests/test_rbac_policy.py

The Observer is a read-only watcher. All remediation goes through Brain → GitHub PR.
Exception: coordination.k8s.io/leases write verbs are permitted for controller-runtime
leader election (split-brain prevention), NOT for workload mutation.
"""
import yaml
import pathlib
import pytest

_CLUSTERROLE_PATH = (
    pathlib.Path(__file__).parent.parent
    / "helm" / "observer" / "templates" / "clusterrole.yaml"
)

# Write verbs that must never appear on core workload resources
FORBIDDEN_VERBS = {"create", "update", "patch", "delete", "deletecollection"}

# Resources whose write access is explicitly permitted (leader election only)
LEADER_ELECTION_RESOURCES = {"leases"}

# Resources that must be strictly read-only
MUST_BE_READONLY = {"pods", "pods/log", "events", "configmaps", "servicemonitors"}


def _load_clusterrole() -> dict:
    raw = _CLUSTERROLE_PATH.read_text()
    # Strip Helm template directives so PyYAML can parse the file
    lines = [
        ln for ln in raw.splitlines()
        if not ln.strip().startswith("{{") and "{{" not in ln
    ]
    return yaml.safe_load("\n".join(lines))


@pytest.fixture(scope="module")
def clusterrole():
    return _load_clusterrole()


def test_clusterrole_file_exists():
    assert _CLUSTERROLE_PATH.exists(), f"ClusterRole template not found: {_CLUSTERROLE_PATH}"


def test_core_workload_resources_are_readonly(clusterrole):
    """pods, events, configmaps, servicemonitors must have no write verbs."""
    rules = clusterrole.get("rules", [])
    for rule in rules:
        resources = set(rule.get("resources", []))
        verbs = set(rule.get("verbs", []))
        readonly_resources = resources & MUST_BE_READONLY
        if readonly_resources:
            forbidden_found = verbs & FORBIDDEN_VERBS
            assert not forbidden_found, (
                f"Observer ClusterRole grants write verbs {forbidden_found} "
                f"to read-only resources {readonly_resources}. "
                "The Observer must never mutate workloads — all fixes go via GitHub PRs."
            )


def test_leases_write_verbs_are_only_for_leader_election(clusterrole):
    """Write verbs on leases are permitted (leader election) but must be scoped to
    coordination.k8s.io only — never on core '' apiGroup."""
    rules = clusterrole.get("rules", [])
    for rule in rules:
        api_groups = set(rule.get("apiGroups", []))
        resources = set(rule.get("resources", []))
        verbs = set(rule.get("verbs", []))
        has_write = verbs & FORBIDDEN_VERBS
        has_lease = "leases" in resources
        if has_write and not has_lease:
            # Write verbs on non-lease resource — fail unless it's leader election
            forbidden_non_lease = resources - LEADER_ELECTION_RESOURCES
            assert not forbidden_non_lease, (
                f"Unexpected write verbs {verbs & FORBIDDEN_VERBS} "
                f"on non-lease resources: {forbidden_non_lease}"
            )
        if has_write and has_lease:
            # Leases must only appear under coordination.k8s.io, not core ''
            assert "coordination.k8s.io" in api_groups, (
                "Leases with write verbs must be under coordination.k8s.io apiGroup"
            )
            assert "" not in api_groups, (
                "Write verbs on core '' apiGroup with leases is not allowed"
            )


def test_no_wildcard_verbs(clusterrole):
    """No rule may use '*' as a verb."""
    rules = clusterrole.get("rules", [])
    for rule in rules:
        verbs = rule.get("verbs", [])
        assert "*" not in verbs, (
            "Wildcard verb '*' found in Observer ClusterRole — "
            "all verbs must be explicit."
        )


def test_no_wildcard_resources(clusterrole):
    """No rule may use '*' as a resource."""
    rules = clusterrole.get("rules", [])
    for rule in rules:
        resources = rule.get("resources", [])
        assert "*" not in resources, (
            "Wildcard resource '*' found in Observer ClusterRole — "
            "all resources must be explicit."
        )
