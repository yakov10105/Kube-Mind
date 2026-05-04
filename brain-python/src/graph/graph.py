from langgraph.graph import END, StateGraph
from langgraph.prebuilt import ToolNode
from langchain_google_vertexai import ChatVertexAI

from src.config import settings
from src.graph.state import IncidentGraphState
from src.graph.nodes import (
    deduplicate_node,
    enrich_memory_node,
    safety_blocked_node,
    write_memory_node,
)
from src.tools import (
    analyze_incident,
    create_fix_pull_request,
    get_pod_status,
    is_code_change_safe,
)

TOOLS = [get_pod_status, analyze_incident, is_code_change_safe, create_fix_pull_request]


def _route_after_deduplicate(state: IncidentGraphState) -> str:
    return "end" if state.get("outcome") == "duplicate" else "enrich"


def _should_continue(state: IncidentGraphState) -> str:
    last = state["messages"][-1]
    if hasattr(last, "tool_calls") and last.tool_calls:
        return "tools"
    return "route"


def _route_after_polycheck(state: IncidentGraphState) -> str:
    # Check tool results in messages for polycheck verdict
    for msg in reversed(state.get("messages", [])):
        content = getattr(msg, "content", "")
        if isinstance(content, str) and content.strip().upper() == "NO":
            return "blocked"
    if state.get("safety_result") == "NO":
        return "blocked"
    return "write_memory"


def create_graph():
    llm = ChatVertexAI(
        model=settings.gemini_model_id,
        project=settings.gcp_project_id,
        location=settings.gcp_location,
    ).bind_tools(TOOLS)

    def agent_node(state: IncidentGraphState) -> dict:
        response = llm.invoke(state["messages"])
        preview = str(getattr(response, "content", ""))[:200]
        return {
            "messages": [response],
            "stream_events": [f"Agent: {preview}"],
        }

    tool_node = ToolNode(TOOLS)

    # Thin routing node so conditional edges can branch from it
    def route_node(state: IncidentGraphState) -> dict:
        return {}

    builder = StateGraph(IncidentGraphState)

    builder.add_node("deduplicate", deduplicate_node)
    builder.add_node("enrich", enrich_memory_node)
    builder.add_node("agent", agent_node)
    builder.add_node("tools", tool_node)
    builder.add_node("route", route_node)
    builder.add_node("safety_blocked", safety_blocked_node)
    builder.add_node("write_memory", write_memory_node)

    builder.set_entry_point("deduplicate")

    builder.add_conditional_edges(
        "deduplicate",
        _route_after_deduplicate,
        {"end": END, "enrich": "enrich"},
    )
    builder.add_edge("enrich", "agent")
    builder.add_conditional_edges(
        "agent",
        _should_continue,
        {"tools": "tools", "route": "route"},
    )
    builder.add_edge("tools", "agent")
    builder.add_conditional_edges(
        "route",
        _route_after_polycheck,
        {"blocked": "safety_blocked", "write_memory": "write_memory"},
    )
    builder.add_edge("safety_blocked", END)
    builder.add_edge("write_memory", END)

    return builder.compile()
