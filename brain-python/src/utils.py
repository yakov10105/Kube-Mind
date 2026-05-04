import google.auth
import google.auth.exceptions


def validate_gcp_credentials() -> None:
    try:
        google.auth.default()
    except google.auth.exceptions.DefaultCredentialsError as e:
        raise RuntimeError(
            "GCP Application Default Credentials not found. "
            "Set GOOGLE_APPLICATION_CREDENTIALS to a service account key path, "
            "or run 'gcloud auth application-default login' for local dev."
        ) from e
