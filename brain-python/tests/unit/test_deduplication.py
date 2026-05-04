"""Unit tests for RedisDeduplicationService."""
import pytest
from unittest.mock import AsyncMock, patch

from src.services.deduplication import RedisDeduplicationService


@pytest.fixture
def svc(monkeypatch):
    with patch("src.services.deduplication.aioredis.from_url") as mock_from_url:
        mock_redis = AsyncMock()
        mock_from_url.return_value = mock_redis
        service = RedisDeduplicationService()
        service._redis = mock_redis
        yield service, mock_redis


@pytest.mark.asyncio
async def test_first_call_returns_false(svc):
    service, mock_redis = svc
    mock_redis.set.return_value = True  # SET NX succeeded → new key
    result = await service.is_duplicate("kubemind:dedup:abc123")
    assert result is False
    mock_redis.set.assert_awaited_once_with(
        "kubemind:incident:kubemind:dedup:abc123", "processed", nx=True, ex=service._ttl
    )


@pytest.mark.asyncio
async def test_second_call_within_ttl_returns_true(svc):
    service, mock_redis = svc
    mock_redis.set.return_value = None  # SET NX failed → key exists → duplicate
    result = await service.is_duplicate("kubemind:dedup:abc123")
    assert result is True


@pytest.mark.asyncio
async def test_call_after_ttl_expiry_returns_false(svc):
    service, mock_redis = svc
    # First call: not duplicate
    mock_redis.set.return_value = True
    assert await service.is_duplicate("key") is False
    # Simulate TTL expiry: second call succeeds again (new)
    mock_redis.set.return_value = True
    assert await service.is_duplicate("key") is False


def test_stable_hash_key_ignores_timestamp():
    from src.grpc_server import stable_dedup_key
    from unittest.mock import MagicMock

    inc1 = MagicMock()
    inc1.pod_namespace = "default"
    inc1.pod_name = "my-pod"
    inc1.failure_reason = "OOMKilled"

    inc2 = MagicMock()
    inc2.pod_namespace = "default"
    inc2.pod_name = "my-pod"
    inc2.failure_reason = "OOMKilled"

    assert stable_dedup_key(inc1) == stable_dedup_key(inc2)


def test_stable_hash_key_differs_for_different_incidents():
    from src.grpc_server import stable_dedup_key
    from unittest.mock import MagicMock

    inc1 = MagicMock()
    inc1.pod_namespace = "default"
    inc1.pod_name = "pod-a"
    inc1.failure_reason = "OOMKilled"

    inc2 = MagicMock()
    inc2.pod_namespace = "default"
    inc2.pod_name = "pod-b"
    inc2.failure_reason = "OOMKilled"

    assert stable_dedup_key(inc1) != stable_dedup_key(inc2)
