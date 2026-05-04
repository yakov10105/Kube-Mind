import json

from langchain_core.tools import tool
from kubernetes import client, config as k8s_config
from kubernetes.client.exceptions import ApiException


@tool
def get_pod_status(pod_name: str, namespace: str) -> str:
    """Gets the current status and conditions of a Kubernetes pod by name and namespace."""
    try:
        k8s_config.load_incluster_config()
    except k8s_config.ConfigException:
        k8s_config.load_kube_config()

    v1 = client.CoreV1Api()
    try:
        pod = v1.read_namespaced_pod(name=pod_name, namespace=namespace)
    except ApiException as e:
        if e.status == 404:
            return json.dumps({"error": f"Pod {namespace}/{pod_name} not found"})
        raise

    status = {
        "phase": pod.status.phase,
        "conditions": [
            {"type": c.type, "status": c.status}
            for c in (pod.status.conditions or [])
        ],
        "container_statuses": [
            {
                "name": cs.name,
                "ready": cs.ready,
                "restart_count": cs.restart_count,
                "state": str(cs.state),
            }
            for cs in (pod.status.container_statuses or [])
        ],
    }
    return json.dumps(status)
