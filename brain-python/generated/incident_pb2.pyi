import datetime

from google.protobuf import timestamp_pb2 as _timestamp_pb2
from google.protobuf import descriptor as _descriptor
from google.protobuf import message as _message
from collections.abc import Mapping as _Mapping
from typing import ClassVar as _ClassVar, Optional as _Optional, Union as _Union

DESCRIPTOR: _descriptor.FileDescriptor

class IncidentContext(_message.Message):
    __slots__ = ("incident_id", "pod_name", "pod_namespace", "failure_reason", "logs", "pod_manifest_json", "deployment_manifest_json", "timestamp", "cluster_id")
    INCIDENT_ID_FIELD_NUMBER: _ClassVar[int]
    POD_NAME_FIELD_NUMBER: _ClassVar[int]
    POD_NAMESPACE_FIELD_NUMBER: _ClassVar[int]
    FAILURE_REASON_FIELD_NUMBER: _ClassVar[int]
    LOGS_FIELD_NUMBER: _ClassVar[int]
    POD_MANIFEST_JSON_FIELD_NUMBER: _ClassVar[int]
    DEPLOYMENT_MANIFEST_JSON_FIELD_NUMBER: _ClassVar[int]
    TIMESTAMP_FIELD_NUMBER: _ClassVar[int]
    CLUSTER_ID_FIELD_NUMBER: _ClassVar[int]
    incident_id: str
    pod_name: str
    pod_namespace: str
    failure_reason: str
    logs: str
    pod_manifest_json: str
    deployment_manifest_json: str
    timestamp: _timestamp_pb2.Timestamp
    cluster_id: str
    def __init__(self, incident_id: _Optional[str] = ..., pod_name: _Optional[str] = ..., pod_namespace: _Optional[str] = ..., failure_reason: _Optional[str] = ..., logs: _Optional[str] = ..., pod_manifest_json: _Optional[str] = ..., deployment_manifest_json: _Optional[str] = ..., timestamp: _Optional[_Union[datetime.datetime, _timestamp_pb2.Timestamp, _Mapping]] = ..., cluster_id: _Optional[str] = ...) -> None: ...

class StreamIncidentResponse(_message.Message):
    __slots__ = ("status",)
    STATUS_FIELD_NUMBER: _ClassVar[int]
    status: str
    def __init__(self, status: _Optional[str] = ...) -> None: ...
