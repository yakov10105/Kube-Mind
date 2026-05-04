"""Integration tests for the gRPC server."""
import asyncio
import pytest
import grpc
import grpc.aio as aio

from generated import incident_pb2, incident_pb2_grpc
from src.grpc_server import serve_grpc, stable_dedup_key


@pytest.fixture
async def grpc_server(unused_tcp_port):
    received: list = []

    async def handler(incident):
        received.append(incident)

    server_task = asyncio.create_task(serve_grpc(handler, port=unused_tcp_port))
    await asyncio.sleep(0.2)  # allow server to start
    yield unused_tcp_port, received
    server_task.cancel()
    try:
        await server_task
    except (asyncio.CancelledError, Exception):
        pass


@pytest.fixture
def unused_tcp_port():
    import socket
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


@pytest.mark.asyncio
@pytest.mark.integration
async def test_server_starts_and_accepts_connection(grpc_server):
    port, received = grpc_server
    async with aio.insecure_channel(f"localhost:{port}") as channel:
        stub = incident_pb2_grpc.IncidentServiceStub(channel)

        async def gen():
            yield incident_pb2.IncidentContext(
                incident_id="test-1",
                pod_name="my-pod",
                pod_namespace="default",
                failure_reason="OOMKilled",
            )

        response = await stub.StreamIncident(gen())
        assert "processed" in response.status.lower()

    assert len(received) == 1
    assert received[0].incident_id == "test-1"


@pytest.mark.asyncio
@pytest.mark.integration
async def test_valid_incident_context_forwarded_to_handler(grpc_server):
    port, received = grpc_server
    async with aio.insecure_channel(f"localhost:{port}") as channel:
        stub = incident_pb2_grpc.IncidentServiceStub(channel)

        async def gen():
            yield incident_pb2.IncidentContext(
                incident_id="inc-42",
                pod_name="api-pod",
                pod_namespace="production",
                failure_reason="CrashLoopBackOff",
                logs="Error: segfault",
            )

        await stub.StreamIncident(gen())

    assert received[0].pod_name == "api-pod"
    assert received[0].failure_reason == "CrashLoopBackOff"


def test_same_pod_different_timestamps_produce_same_dedup_key():
    from unittest.mock import MagicMock

    inc1 = MagicMock()
    inc1.pod_namespace, inc1.pod_name, inc1.failure_reason = "default", "pod-x", "OOMKilled"

    inc2 = MagicMock()
    inc2.pod_namespace, inc2.pod_name, inc2.failure_reason = "default", "pod-x", "OOMKilled"

    assert stable_dedup_key(inc1) == stable_dedup_key(inc2)


@pytest.mark.asyncio
@pytest.mark.integration
async def test_server_returns_correct_status_message(grpc_server):
    port, _ = grpc_server
    async with aio.insecure_channel(f"localhost:{port}") as channel:
        stub = incident_pb2_grpc.IncidentServiceStub(channel)

        async def gen():
            yield incident_pb2.IncidentContext(incident_id="x")

        response = await stub.StreamIncident(gen())

    assert response.status == "Incidents received and processed."
