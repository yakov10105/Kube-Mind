import redis.asyncio as aioredis
from src.config import settings


class RedisDeduplicationService:
    def __init__(self) -> None:
        self._redis = aioredis.from_url(settings.redis_url)
        self._ttl = settings.deduplication_ttl_seconds

    async def is_duplicate(self, key: str) -> bool:
        """Returns True if already seen within TTL window, False for new incidents."""
        result = await self._redis.set(key, "processed", nx=True, ex=self._ttl)
        return result is None  # None → key already existed → duplicate


_service: RedisDeduplicationService | None = None


def get_deduplication_service() -> RedisDeduplicationService:
    global _service
    if _service is None:
        _service = RedisDeduplicationService()
    return _service
