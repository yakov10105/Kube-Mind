import asyncio
from typing import AsyncGenerator


class EventBus:
    """Per-incident topic bus. Subscribers only receive events for the incident_id they request."""

    def __init__(self) -> None:
        self._topics: dict[str, list[asyncio.Queue[str]]] = {}

    async def publish(self, incident_id: str, message: str) -> None:
        for q in self._topics.get(incident_id, []):
            await q.put(message)

    async def subscribe(self, incident_id: str) -> AsyncGenerator[str, None]:
        q: asyncio.Queue[str] = asyncio.Queue()
        self._topics.setdefault(incident_id, []).append(q)
        try:
            while True:
                msg = await asyncio.wait_for(q.get(), timeout=120.0)
                yield msg
        except asyncio.TimeoutError:
            return
        finally:
            self._topics[incident_id].remove(q)
            if not self._topics[incident_id]:
                del self._topics[incident_id]


event_bus = EventBus()
