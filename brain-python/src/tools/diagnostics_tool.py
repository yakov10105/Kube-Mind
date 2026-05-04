import json

from langchain_core.tools import tool
from langchain_google_vertexai import ChatVertexAI

from src.config import settings


@tool
def analyze_incident(incident_context_json: str) -> str:
    """Analyzes Kubernetes pod logs and manifests to diagnose the root cause of a failure.
    Returns a JSON string with rootCause, confidence, recommendedAction, and supportingEvidence."""
    try:
        incident = json.loads(incident_context_json)
    except json.JSONDecodeError:
        incident = {"raw": incident_context_json}

    prompt = f"""You are an expert Kubernetes SRE.
Analyze the following incident and return ONLY a minified JSON object with keys:
rootCause, confidence (High/Medium/Low), recommendedAction, supportingEvidence.

Incident ID: {incident.get('incident_id', 'unknown')}
Pod: {incident.get('pod_namespace', 'unknown')}/{incident.get('pod_name', 'unknown')}
Failure Reason: {incident.get('failure_reason', 'unknown')}

Pod Manifest:
{incident.get('pod_manifest_json', '{}')}

Deployment Manifest:
{incident.get('deployment_manifest_json', '{}')}

Recent Logs:
{incident.get('logs', '')}

Rules:
- For OOMKilled: check memory limits in pod manifest
- For CrashLoopBackOff: look for stack traces and startup failures in logs
- For ImagePullBackOff: check image tag and pull secret configuration
"""
    llm = ChatVertexAI(
        model=settings.gemini_model_id,
        project=settings.gcp_project_id,
        location=settings.gcp_location,
    )
    try:
        result = llm.invoke(prompt)
        content = result.content
        # Validate it's parseable JSON; if not, wrap it
        json.loads(content)
        return content
    except (json.JSONDecodeError, Exception) as e:
        return json.dumps({
            "rootCause": "Analysis failed",
            "confidence": "Low",
            "recommendedAction": "Manual investigation required",
            "supportingEvidence": str(e),
        })
