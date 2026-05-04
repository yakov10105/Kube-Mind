import structlog
from fastapi import FastAPI
from fastapi.responses import HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from sse_starlette.sse import EventSourceResponse

from src.services.event_bus import event_bus

log = structlog.get_logger()

app = FastAPI(title="KubeMind Brain (Python)")


@app.get("/")
async def root() -> dict:
    return {"status": "KubeMind Brain (Python) is online."}


@app.get("/healthz")
async def healthz():
    try:
        from langchain_google_vertexai import ChatVertexAI
        from src.config import settings

        llm = ChatVertexAI(
            model=settings.gemini_model_id,
            project=settings.gcp_project_id,
            location=settings.gcp_location,
        )
        result = await llm.ainvoke("Respond with a single word: OK")
        if str(result.content).strip().upper() == "OK":
            return {"status": "healthy", "llm": "connected"}
        return JSONResponse(
            {"status": "degraded", "llm": str(result.content)}, status_code=503
        )
    except Exception as e:
        log.error("healthz.failed", error=str(e))
        return JSONResponse(
            {"status": "unhealthy", "error": str(e)}, status_code=503
        )


@app.get("/events/{incident_id}")
async def stream_events(incident_id: str):
    """Per-incident SSE stream. Only delivers events for the requested incident."""
    async def generator():
        async for message in event_bus.subscribe(incident_id):
            yield {"data": message}

    return EventSourceResponse(generator())


@app.get("/events")
async def stream_events_no_id():
    return JSONResponse(
        {"error": "incident_id is required. Use /events/{incident_id}"},
        status_code=400,
    )


# Serve the static UI under /ui
try:
    from pathlib import Path
    _static = Path(__file__).parent.parent / "static"
    if _static.exists():
        app.mount("/ui", StaticFiles(directory=str(_static), html=True), name="ui")
except Exception:
    pass
