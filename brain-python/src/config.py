from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8", extra="ignore")

    # GCP — GOOGLE_APPLICATION_CREDENTIALS is intentionally NOT a Settings field.
    # It is read directly by the google-auth library from the OS environment.
    gcp_project_id: str = "default-project"
    gcp_location: str = "us-central1"

    # LLM
    gemini_model_id: str = "gemini-2.5-pro"

    # Redis
    redis_url: str = "redis://localhost:6379"
    deduplication_ttl_seconds: int = 300

    # Qdrant
    qdrant_host: str = "localhost"
    qdrant_port: int = 6334
    qdrant_collection: str = "k8s_incidents"
    qdrant_similarity_threshold: float = 0.95

    # GitHub
    github_token: str = ""
    github_default_repo_owner: str = ""
    github_default_repo_name: str = ""
    github_default_base_branch: str = "main"

    # Slack
    slack_webhook_url: str = ""

    # Server
    grpc_port: int = 50051
    http_port: int = 5081

    # mTLS (leave empty to use insecure mode)
    grpc_tls_ca_cert: str = ""
    grpc_tls_server_cert: str = ""
    grpc_tls_server_key: str = ""

    # Cluster identity
    default_cluster_id: str = "default-cluster"

    # Observability
    otlp_endpoint: str = ""
    log_level: str = "INFO"


settings = Settings()
