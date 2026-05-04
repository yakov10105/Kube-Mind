import os
import pytest

# Seed required env vars at module-load time — before pytest imports any test
# module that triggers `from src.config import settings` at module level.
# Per-test overrides use monkeypatch (which restores on teardown).
os.environ.setdefault("GCP_PROJECT_ID", "test-project")
os.environ.setdefault("GITHUB_TOKEN", "ghp_test_token")


def pytest_configure(config):
    config.addinivalue_line("markers", "integration: marks tests requiring external services")
