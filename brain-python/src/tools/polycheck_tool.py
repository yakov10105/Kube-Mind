import structlog
from langchain_core.tools import tool
from langchain_google_vertexai import ChatVertexAI

from src.config import settings

log = structlog.get_logger()


@tool
def is_code_change_safe(code_change: str) -> str:
    """Validates a proposed code or configuration change for safety.
    Returns 'YES' if the change is safe (value-only modifications),
    or 'NO' if it contains destructive actions like deletions or structural changes."""
    prompt = f"""You are a senior DevOps engineer responsible for infrastructure stability.
Your sole task: determine if this configuration change is safe.

A "safe" change ONLY modifies values (changing memory limits, updating image tags, modifying env var values).
An "unsafe" change alters structure, deletes resources, or changes fundamental behaviour
(deleting a deployment, changing a port, removing a volume).

Does this code look safe? Answer ONLY with "YES" or "NO".

---
{code_change}
---
"""
    llm = ChatVertexAI(
        model=settings.gemini_model_id,
        project=settings.gcp_project_id,
        location=settings.gcp_location,
    )
    result = llm.invoke(prompt)
    answer = result.content.strip().upper()
    verdict = "YES" if answer == "YES" else "NO"
    log.info("polycheck.result", verdict=verdict)
    return verdict
