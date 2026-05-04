"""Unit tests for Pydantic Settings — Task 1.2."""
import pytest
from pydantic import ValidationError
from pydantic_settings import SettingsConfigDict


# ---------------------------------------------------------------------------
# Helper: isolated Settings subclass that never reads a .env file.
# This keeps tests hermetic regardless of what the developer has in .env.
# ---------------------------------------------------------------------------

def _isolated_settings_cls():
    from src.config import Settings

    class _IsolatedSettings(Settings):
        model_config = SettingsConfigDict(env_file=None, extra="ignore")

    return _IsolatedSettings


# ---------------------------------------------------------------------------
# Required-field validation
# ---------------------------------------------------------------------------

def test_missing_gcp_project_id_raises_validation_error(monkeypatch):
    """Settings() must raise ValidationError when GCP_PROJECT_ID is not set."""
    monkeypatch.delenv("GCP_PROJECT_ID", raising=False)
    with pytest.raises(ValidationError) as exc_info:
        _isolated_settings_cls()()
    errors = exc_info.value.errors()
    assert any(e["loc"] == ("gcp_project_id",) for e in errors)


# ---------------------------------------------------------------------------
# Default values
# ---------------------------------------------------------------------------

def test_all_defaults_are_applied(monkeypatch):
    """Every optional field must resolve to its documented default."""
    monkeypatch.setenv("GCP_PROJECT_ID", "my-project")
    monkeypatch.delenv("GITHUB_TOKEN", raising=False)
    s = _isolated_settings_cls()()

    assert s.gcp_project_id == "my-project"
    assert s.gcp_location == "us-central1"
    assert s.gemini_model_id == "gemini-2.5-pro"
    assert s.redis_url == "redis://localhost:6379"
    assert s.deduplication_ttl_seconds == 300
    assert s.qdrant_host == "localhost"
    assert s.qdrant_port == 6334
    assert s.qdrant_collection == "k8s_incidents"
    assert s.qdrant_similarity_threshold == 0.95
    assert s.github_token == ""
    assert s.github_default_repo_owner == ""
    assert s.github_default_repo_name == ""
    assert s.github_default_base_branch == "main"
    assert s.slack_webhook_url == ""
    assert s.grpc_port == 50051
    assert s.http_port == 5081
    assert s.grpc_tls_ca_cert == ""
    assert s.grpc_tls_server_cert == ""
    assert s.grpc_tls_server_key == ""
    assert s.default_cluster_id == "default-cluster"
    assert s.otlp_endpoint == ""
    assert s.log_level == "INFO"


# ---------------------------------------------------------------------------
# Env-var overrides
# ---------------------------------------------------------------------------

def test_env_vars_override_defaults(monkeypatch):
    """Every env var must override the corresponding default."""
    monkeypatch.setenv("GCP_PROJECT_ID", "prod-project")
    monkeypatch.setenv("GCP_LOCATION", "europe-west1")
    monkeypatch.setenv("GEMINI_MODEL_ID", "gemini-2.0-flash")
    monkeypatch.setenv("GRPC_PORT", "50052")
    monkeypatch.setenv("HTTP_PORT", "8080")
    monkeypatch.setenv("DEDUPLICATION_TTL_SECONDS", "600")
    monkeypatch.setenv("QDRANT_SIMILARITY_THRESHOLD", "0.90")
    monkeypatch.setenv("DEFAULT_CLUSTER_ID", "prod-cluster")
    monkeypatch.setenv("LOG_LEVEL", "DEBUG")

    s = _isolated_settings_cls()()

    assert s.gcp_project_id == "prod-project"
    assert s.gcp_location == "europe-west1"
    assert s.gemini_model_id == "gemini-2.0-flash"
    assert s.grpc_port == 50052
    assert s.http_port == 8080
    assert s.deduplication_ttl_seconds == 600
    assert s.qdrant_similarity_threshold == pytest.approx(0.90)
    assert s.default_cluster_id == "prod-cluster"
    assert s.log_level == "DEBUG"


def test_integer_fields_reject_non_integer(monkeypatch):
    """Type coercion: pydantic should reject a non-numeric string for int fields."""
    monkeypatch.setenv("GCP_PROJECT_ID", "p")
    monkeypatch.setenv("GRPC_PORT", "not-a-number")
    with pytest.raises(ValidationError):
        _isolated_settings_cls()()


# ---------------------------------------------------------------------------
# ADC startup validation (validate_gcp_credentials in main.py)
# ---------------------------------------------------------------------------

def test_validate_gcp_credentials_raises_runtime_error_on_missing_adc():
    """validate_gcp_credentials() must raise RuntimeError when ADC is unavailable."""
    import google.auth.exceptions
    from unittest.mock import patch
    from src.utils import validate_gcp_credentials

    with patch(
        "src.utils.google.auth.default",
        side_effect=google.auth.exceptions.DefaultCredentialsError("no creds"),
    ):
        with pytest.raises(RuntimeError, match="GCP Application Default Credentials not found"):
            validate_gcp_credentials()


def test_validate_gcp_credentials_does_not_raise_when_adc_resolves():
    """validate_gcp_credentials() must be silent when ADC resolves successfully."""
    from unittest.mock import MagicMock, patch
    from src.utils import validate_gcp_credentials

    with patch("src.utils.google.auth.default", return_value=(MagicMock(), "test-project")):
        validate_gcp_credentials()  # must not raise


def test_google_application_credentials_is_not_a_settings_field():
    """GOOGLE_APPLICATION_CREDENTIALS must never appear as a Settings attribute."""
    from src.config import Settings
    assert not hasattr(Settings.model_fields, "google_application_credentials"), (
        "GOOGLE_APPLICATION_CREDENTIALS must not be a Settings field — "
        "google-auth reads it directly from the OS environment."
    )
