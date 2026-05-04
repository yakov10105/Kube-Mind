import operator
from typing import Annotated, Optional, TypedDict

from langchain_core.messages import BaseMessage

from generated.incident_pb2 import IncidentContext


class IncidentGraphState(TypedDict):
    incident: IncidentContext

    enriched_goal: str
    historical_context: str

    messages: Annotated[list[BaseMessage], operator.add]

    pod_status: Optional[str]
    diagnosis: Optional[str]
    proposed_fix: Optional[str]

    safety_result: Optional[str]

    pr_url: Optional[str]
    outcome: Optional[str]
    error_message: Optional[str]

    stream_events: Annotated[list[str], operator.add]
