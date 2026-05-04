import json
from pathlib import Path

import structlog
from google.protobuf.json_format import MessageToDict
from jinja2 import Environment, FileSystemLoader
from langchain_core.messages import HumanMessage

from src.config import settings
from src.graph.state import IncidentGraphState
from src.services.deduplication import get_deduplication_service
from src.services.enrichment import get_enrichment_service
from src.services.memory_consolidation import get_memory_consolidation_service
from src.services.slack_service import get_slack_service

log = structlog.get_logger()

_PROMPTS_DIR = Path(__file__).parent.parent.parent / "prompts"

_jinja_env = Environment(
    loader=FileSystemLoader(str(_PROMPTS_DIR)),
    autoescape=False,
)


def build_sop_goal(incident: object, historical_context: str = "") -> str:
    template = _jinja_env.get_template("sop.j2")
    incident_dict = MessageToDict(incident, preserving_proto_field_name=True)  # type: ignore[arg-type]
    return template.render(
        incident_json=json.dumps(incident_dict, indent=2),
        historical_context=historical_context,
    )


async def deduplicate_node(state: IncidentGraphState) -> dict:
    incident = state["incident"]
    from src.grpc_server import stable_dedup_key
    key = stable_dedup_key(incident)
    svc = get_deduplication_service()
    is_dup = await svc.is_duplicate(key)
    if is_dup:
        log.info("graph.duplicate", incident_id=incident.incident_id)
        return {
            "outcome": "duplicate",
            "stream_events": [f"Incident {incident.incident_id} is a duplicate — skipping."],
        }
    return {"stream_events": [f"New incident received: {incident.incident_id}"]}


async def enrich_memory_node(state: IncidentGraphState) -> dict:
    incident = state["incident"]
    svc = get_enrichment_service()
    original_goal = build_sop_goal(incident)
    enriched_goal, context = await svc.enrich(incident.logs, original_goal)
    return {
        "enriched_goal": enriched_goal,
        "historical_context": context,
        "stream_events": ["Memory enrichment complete."],
        "messages": [HumanMessage(content=enriched_goal)],
    }


async def write_memory_node(state: IncidentGraphState) -> dict:
    incident = state["incident"]
    cluster_id = incident.cluster_id or settings.default_cluster_id
    svc = get_memory_consolidation_service()
    await svc.enqueue(
        incident_id=incident.incident_id,
        cluster_id=cluster_id,
        namespace=incident.pod_namespace,
        raw_log=incident.logs,
        resolution=state.get("pr_url") or state.get("outcome") or "unknown",
    )
    return {"stream_events": ["Memory write-behind enqueued."]}


async def safety_blocked_node(state: IncidentGraphState) -> dict:
    incident = state["incident"]
    slack = get_slack_service()
    msg = (
        f"Automated remediation BLOCKED for {incident.incident_id}: "
        "safety check returned NO."
    )
    await slack.notify(msg)
    log.warning("graph.safety_blocked", incident_id=incident.incident_id)
    return {
        "outcome": "safety_blocked",
        "stream_events": [
            f"Remediation blocked: safety check failed for {incident.incident_id}"
        ],
    }
