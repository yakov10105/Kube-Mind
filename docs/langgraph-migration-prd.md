# PRD: Migrate Kube-Mind Brain from .NET Semantic Kernel to Python LangGraph

**Project Code Name:** `KM-MIGRATE-01`  
**Version:** 1.0.0  
**Author:** AI Thought Partner  
**Status:** DRAFT  
**Date:** 2026-05-03  
**Replaces:** `docs/orchestrator-prd.md` (KM-BRN-01) — Brain implementation only

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Current Architecture](#2-current-architecture)
3. [Target Architecture](#3-target-architecture)
4. [Migration Scope](#4-migration-scope)
5. [Technical Stack](#5-technical-stack)
6. [Project Layout](#6-project-layout)
7. [Implementation Phases](#7-implementation-phases)
   - [Phase 0: Environment & Prerequisites](#phase-0-environment--prerequisites)
   - [Phase 1: Python Project Foundation](#phase-1-python-project-foundation)
   - [Phase 2: gRPC Server — Replace .NET Ingestion Endpoint](#phase-2-grpc-server--replace-net-ingestion-endpoint)
   - [Phase 3: LangGraph State & Graph Definition](#phase-3-langgraph-state--graph-definition)
   - [Phase 4: Tool Implementations](#phase-4-tool-implementations)
   - [Phase 5: Cognitive Memory — Enrichment & Write-Behind](#phase-5-cognitive-memory--enrichment--write-behind)
   - [Phase 6: Real-time Event Streaming — Replace SignalR](#phase-6-real-time-event-streaming--replace-signalr)
   - [Phase 7: GitHub & Slack Integrations](#phase-7-github--slack-integrations)
   - [Phase 8: Observability, Structured Logging & Metrics](#phase-8-observability-structured-logging--metrics)
   - [Phase 9: Containerization](#phase-9-containerization)
   - [Phase 10: Kubernetes & Helm Deployment](#phase-10-kubernetes--helm-deployment)
   - [Phase 11: Testing Strategy](#phase-11-testing-strategy)
   - [Phase 12: Cutover & Deprecation of .NET Brain](#phase-12-cutover--deprecation-of-net-brain)
8. [Go Observer Controller Changes](#8-go-observer-controller-changes)
9. [Configuration Reference](#9-configuration-reference)
10. [Data Flow: Before vs. After](#10-data-flow-before-vs-after)
11. [Risk Register](#11-risk-register)
12. [Success Criteria & KPIs](#12-success-criteria--kpis)

---

## 1. Executive Summary

The current Kube-Mind **Brain** is implemented in .NET 8/10 using **Microsoft Semantic Kernel** (SK) as its AI orchestration layer. While SK is a capable framework, the Python AI/ML ecosystem — and specifically **LangGraph** — offers richer, more mature tooling for building production-grade, stateful, graph-based AI agent workflows. LangGraph's explicit graph model (nodes, edges, conditional routing) maps directly to the existing Kube-Mind cognitive loop and provides native streaming, built-in state management, and a rapidly growing integration ecosystem (including first-party LangChain Google integrations for Vertex AI and Gemini).

This PRD defines the complete, step-by-step migration of the Brain component from the .NET Semantic Kernel application to a **Python + LangGraph + FastAPI** application. 

**The Go Observer controller is not changed.** Its gRPC `StreamIncident` contract, Protobuf definitions, Helm charts, and runtime behaviour remain identical. The only operational change to the Observer is the `grpc.serverAddress` Helm value, which will be updated to point at the new Python Brain service.

All external services (Redis, Qdrant, GitHub, Slack, Google Cloud Vertex AI / Gemini) remain identical; only the client code consuming them changes language.

---

## 2. Current Architecture

### 2.1 Component Summary

| Component | Technology | Role |
|-----------|-----------|------|
| Observer | Go + controller-runtime | K8s watch → harvest → gRPC stream |
| Brain | .NET 8 + ASP.NET Core | gRPC server + AI orchestration |
| AI Orchestrator | Microsoft Semantic Kernel v1.70 | Plugin auto-invocation (tool calling) |
| LLM | Google Gemini 2.5-pro via GCP Vertex AI | Primary reasoning model |
| Embeddings | Vertex AI `text-embedding-004` (768-dim) | Memory enrichment & consolidation |
| Vector DB | Qdrant (gRPC port 6334) | Long-term incident memory |
| Deduplication | Redis (StackExchange.Redis) | Per-incident 5-min TTL lock |
| Real-time UI | SignalR (WebSocket) via ASP.NET Core Hub | Streaming agent thoughts to browser |
| GitOps | Octokit.NET + GitHub REST API | Branch creation, file commit, PR |
| Notifications | Slack Webhook (HttpClient) | PR-ready alerts |

### 2.2 Existing SK Plugin → LangGraph Tool Mapping

| .NET SK Plugin | Method | Purpose |
|----------------|--------|---------|
| `KubernetesPlugin` | `GetPodStatus(podName, namespace)` | Live pod state lookup |
| `K8sDiagnosticsPlugin` | `AnalyzeIncident(incidentContextJson)` | LLM-driven root cause analysis |
| `PolycheckPlugin` | `IsCodeChangeSafe(codeChange)` | Safety gate via secondary LLM call |
| `GitOpsPlugin` | `CreateFixPullRequest(...)` | Create GitHub branch + commit + PR |

### 2.3 Current Data Flow (Simplified)

```
Observer (Go) → gRPC StreamIncident → IncidentService.cs
  → Redis dedupe
  → EnrichmentService (Vertex AI embed → Qdrant search → inject context)
  → Kernel.InvokePromptAsync(enrichedGoal, FunctionChoiceBehavior.Auto())
      → KubernetesPlugin.GetPodStatus
      → K8sDiagnosticsPlugin.AnalyzeIncident
      → PolycheckPlugin.IsCodeChangeSafe
      → GitOpsPlugin.CreateFixPullRequest
  → SignalR stream to UI
  → MemoryBuffer → MemoryConsolidationService (async) → Qdrant upsert
```

---

## 3. Target Architecture

### 3.1 New Brain Stack

| Component | Technology | Replaces |
|-----------|-----------|---------|
| Brain runtime | Python 3.12 + asyncio | .NET 8 / ASP.NET Core |
| AI Orchestration | LangGraph 0.2.x | Microsoft Semantic Kernel |
| LLM | Google Gemini 2.5-pro via `langchain-google-vertexai` | SK Google connector |
| Embeddings | Vertex AI `text-embedding-004` via `langchain-google-vertexai` | SK Vertex embedding connector |
| gRPC Server | `grpcio` + `grpcio-tools` (generated from existing `.proto`) | Grpc.AspNetCore |
| HTTP Server | FastAPI + Uvicorn | ASP.NET Core HTTP endpoints |
| Real-time streaming | FastAPI SSE (`sse-starlette`) or WebSocket | ASP.NET Core SignalR Hub |
| Vector DB client | `qdrant-client` (async) | SK Qdrant connector |
| Redis client | `redis-py` (async, `redis.asyncio`) | StackExchange.Redis |
| GitHub | `PyGithub` or `httpx` + GitHub REST API | Octokit.NET |
| Slack | `httpx` webhook POST | `HttpClient` SlackNotificationService |
| Observability | `opentelemetry-sdk` + `opentelemetry-instrumentation-fastapi` | OpenTelemetry .NET SDK |
| Logging | `structlog` (JSON) | Serilog |
| Containerization | Docker (python:3.12-slim) | Distroless .NET |

### 3.2 LangGraph Graph Design

The LangGraph application models the existing Semantic Kernel SOP as an explicit directed graph with typed state:

```
                    ┌──────────────────────┐
                    │       START          │
                    └──────────┬───────────┘
                               │ IncidentContext (from gRPC)
                    ┌──────────▼───────────┐
                    │  deduplicate_node    │  Redis TTL check
                    └──────────┬───────────┘
                               │ [new] / skip [duplicate]
                    ┌──────────▼───────────┐
                    │  enrich_memory_node  │  Vertex AI embed → Qdrant search
                    └──────────┬───────────┘
                               │ enriched state
                    ┌──────────▼───────────┐
                    │  agent_node (LLM)    │  Gemini 2.5-pro with bound tools
                    └──────────┬───────────┘
                               │ tool_call decisions via react loop
              ┌────────────────┼────────────────────┐
              ▼                ▼                     ▼
   ┌──────────────┐  ┌─────────────────┐  ┌──────────────────┐
   │get_pod_status│  │analyze_incident │  │  polycheck_tool  │
   │    tool      │  │     tool        │  │  (safety gate)   │
   └──────┬───────┘  └────────┬────────┘  └────────┬─────────┘
          │                   │                    │
          └───────────────────┴────────────────────┘
                               │ tool results loop back to agent_node
                    ┌──────────▼───────────┐
                    │  route_after_check   │  conditional edge
                    └──────────┬───────────┘
                    [YES]      │      [NO]
          ┌────────────────────┼─────────────────┐
          ▼                                       ▼
┌──────────────────┐                   ┌─────────────────────┐
│  create_pr_tool  │                   │ safety_blocked_node │
│  (GitOps tool)   │                   │   (log + notify)    │
└────────┬─────────┘                   └──────────┬──────────┘
         │                                        │
         ▼                                        ▼
┌──────────────────┐                           END
│ write_memory_node│  async write-behind → Qdrant
└────────┬─────────┘
         │
        END
```

**Key LangGraph concepts used:**
- **TypedDict State** — carries incident, enriched context, diagnosis, proposed fix, safety result, PR URL, streaming events through the graph
- **ToolNode** — LangGraph's built-in `ToolNode` handles the tool execution loop for the 4 tools above
- **Conditional edges** — route after `polycheck_tool` based on YES/NO
- **`stream_mode="messages"`** — native LangGraph streaming yields partial LLM tokens and tool call events, replacing SignalR streaming
- **LangGraph Checkpointer** — optional `AsyncSqliteSaver` or in-memory `MemorySaver` for per-incident state persistence and replay

---

## 4. Migration Scope

### 4.1 What Changes

| Item | Change |
|------|--------|
| `brain/` directory | Replaced by new `brain-python/` directory |
| AI orchestration | Semantic Kernel → LangGraph |
| Plugins (4 SK plugins) | Re-implemented as Python `@tool` decorated functions |
| gRPC server | Grpc.AspNetCore → `grpcio` Python server |
| Real-time streaming | SignalR Hub → FastAPI SSE endpoint |
| GitHub client | Octokit.NET → `PyGithub` |
| Redis client | StackExchange.Redis → `redis.asyncio` |
| Qdrant client | SK Qdrant connector → `qdrant-client` async |
| Embeddings | SK Vertex AI connector → `langchain-google-vertexai` |
| Brain Docker image | .NET → Python 3.12-slim |
| Brain Helm chart | Updated image, same ports (50051 gRPC, 5081 HTTP) |

### 4.2 What Stays the Same (Zero Changes)

| Item | Notes |
|------|-------|
| Go Observer controller | All `.go` source files unchanged |
| Protobuf / `incident.proto` | Extended with optional `cluster_id = 9` field (backwards compatible; see Task 1.4) |
| Observer Helm chart | Only `grpc.serverAddress` value updated at deploy time |
| Observer Dockerfile | Unchanged |
| External services | Redis, Qdrant, GitHub, Slack, GCP — same endpoints |
| Secrets | Same GCP service account JSON, GitHub PAT, Slack webhook |
| RBAC (ClusterRole) | Observer permissions unchanged |
| End-to-end behaviour | Same 6-step SOP, same PR format, same Slack notifications |

---

## 5. Technical Stack

### 5.1 Python Dependencies (`pyproject.toml` / `requirements.txt`)

```toml
[tool.poetry.dependencies]
python = "^3.12"

# LangGraph & LangChain
langgraph = "^0.2"
langchain-core = "^0.3"
langchain-google-vertexai = "^2.0"   # Gemini chat + Vertex AI embeddings

# gRPC
grpcio = "^1.67"
grpcio-tools = "^1.67"               # proto compilation only (dev dep)
protobuf = "^5.28"

# HTTP server
fastapi = "^0.115"
uvicorn = {extras = ["standard"], version = "^0.32"}
sse-starlette = "^2.1"               # Server-Sent Events for real-time streaming

# External services
redis = {extras = ["asyncio"], version = "^5.2"}
qdrant-client = {extras = ["fastembed"], version = "^1.12"}
PyGithub = "^2.5"
httpx = "^0.27"                       # Async HTTP for Slack webhook

# Observability
opentelemetry-sdk = "^1.27"
opentelemetry-instrumentation-fastapi = "^0.48"
opentelemetry-exporter-otlp = "^1.27"
structlog = "^24.4"

# Config & validation
pydantic = "^2.9"
pydantic-settings = "^2.6"
python-dotenv = "^1.0"

# GCP auth
google-auth = "^2.35"
google-cloud-aiplatform = "^1.70"

[tool.poetry.dev-dependencies]
pytest = "^8.3"
pytest-asyncio = "^0.24"
pytest-mock = "^3.14"
grpcio-tools = "^1.67"
ruff = "^0.8"
mypy = "^1.13"
```

### 5.2 Python Version & Runtime

- **Python 3.12** (slim Docker image)
- **asyncio** throughout — all I/O is non-blocking
- **`grpcio` async server** via `grpc.aio` for the gRPC endpoint
- **Single process** running both gRPC and HTTP servers on different ports using `asyncio.gather`

---

## 6. Project Layout

New directory `brain-python/` at the repo root (alongside existing `brain/`):

```
brain-python/
├── pyproject.toml                  # Dependencies & build metadata
├── Dockerfile                      # Python 3.12-slim multi-stage image
├── .env.example                    # Template for local dev secrets
├── proto/
│   └── incident.proto              # Symlink to brain/src/KubeMind.Brain.Shared/Protos/incident.proto
├── generated/
│   └── incident_pb2.py             # Auto-generated by grpcio-tools
│   └── incident_pb2_grpc.py        # Auto-generated by grpcio-tools
├── scripts/
│   └── generate_proto.sh           # Runs grpcio-tools proto compilation
│   └── seed_qdrant.py              # Port of SeedRedis script for Qdrant
├── src/
│   ├── __init__.py
│   ├── main.py                     # Entry point: starts gRPC + HTTP servers
│   ├── config.py                   # Pydantic Settings — all env vars
│   ├── grpc_server.py              # grpcio async server; implements StreamIncident
│   ├── http_server.py              # FastAPI app (health, SSE stream endpoint)
│   ├── graph/
│   │   ├── __init__.py
│   │   ├── state.py                # TypedDict: IncidentGraphState
│   │   ├── graph.py                # LangGraph graph definition & compilation
│   │   └── nodes.py                # Non-tool graph nodes (deduplicate, enrich, write_memory)
│   ├── tools/
│   │   ├── __init__.py
│   │   ├── kubernetes_tool.py      # get_pod_status → replaces KubernetesPlugin
│   │   ├── diagnostics_tool.py     # analyze_incident → replaces K8sDiagnosticsPlugin
│   │   ├── polycheck_tool.py       # is_code_change_safe → replaces PolycheckPlugin
│   │   └── gitops_tool.py          # create_fix_pull_request → replaces GitOpsPlugin
│   ├── services/
│   │   ├── __init__.py
│   │   ├── deduplication.py        # RedisDeduplicationService (asyncio redis)
│   │   ├── enrichment.py           # EnrichmentService (Vertex AI embed + Qdrant search)
│   │   ├── memory_consolidation.py # MemoryConsolidationService (asyncio background task)
│   │   ├── github_service.py       # GitHubService (PyGithub wrapper)
│   │   └── slack_service.py        # SlackNotificationService (httpx webhook)
│   └── observability/
│       ├── __init__.py
│       ├── tracing.py              # OpenTelemetry setup
│       └── logging_config.py      # structlog JSON configuration
└── tests/
    ├── unit/
    │   ├── test_deduplication.py
    │   ├── test_enrichment.py
    │   ├── test_tools.py
    │   └── test_graph.py
    └── integration/
        ├── test_grpc_server.py
        └── test_end_to_end.py
```

---

## 7. Implementation Phases

---

### Phase 0: Environment & Prerequisites

**Goal:** Developer workstation and CI are ready to build and test the Python Brain.

#### Task 0.1 — Install Python 3.12 & Tooling

**DoD:** `python3.12 --version` works; `poetry` or `pip-tools` installed.

Sub-tasks:
- Install Python 3.12 (via `pyenv` or system installer)
- Install `poetry` for dependency management: `pip install poetry`
- Install `grpcio-tools` globally or in the project: `pip install grpcio-tools`
- Verify Docker is available for container builds

#### Task 0.2 — Verify External Services

**DoD:** All external services are reachable from dev workstation.

Sub-tasks:
- Confirm Redis is running: `redis-cli ping` returns `PONG`
- Confirm Qdrant is running: `curl http://localhost:6333/healthz` returns `ok`
- Confirm GCP credentials are valid:
  ```bash
  export GOOGLE_APPLICATION_CREDENTIALS="docs/kube-mind-c205678d57e9.json"
  python3 -c "import google.auth; creds, _ = google.auth.default(); print('GCP OK')"
  ```
- Confirm GitHub PAT has `repo` scope: `curl -H "Authorization: token <PAT>" https://api.github.com/user`

#### Task 0.3 — Create `brain-python/` Directory Structure

**DoD:** All directories and placeholder files listed in §6 exist; `git status` shows untracked files.

Sub-tasks:
- Create directory tree as per §6 Project Layout
- Add `pyproject.toml` with all dependencies from §5.1
- Add `.env.example` with all required env vars (see §9 Configuration Reference)
- Add `.gitignore` entries for `__pycache__/`, `*.pyc`, `.env`, `generated/`

#### Task 0.4 — Rotate & Remove Leaked GCP Credentials ⚠️ CRITICAL SECURITY

**Fixes:** `docs/kube-mind-c205678d57e9.json` committed to git; hardcoded path in `Program.cs:18`

**DoD:** The compromised service account key is revoked; the file is removed from git history; no credential file path is ever set inside application code.

Sub-tasks:
- In the Google Cloud Console, navigate to IAM → Service Accounts → find the `kube-mind-c205678d57e9` service account → Keys tab → **Delete the key** (`c205678d57e9`). This revokes it immediately.
- Create a new service account key and store it **outside the repo** (e.g., `~/.config/gcp/kube-mind-dev.json`)
- Remove the JSON file from git history using `git filter-repo`:
  ```bash
  pip install git-filter-repo
  git filter-repo --path docs/kube-mind-c205678d57e9.json --invert-paths --force
  git push --force-with-lease
  ```
- Add the following entries to the root `.gitignore` and to `brain-python/.gitignore`:
  ```gitignore
  # GCP service account credentials — never commit these
  *.json
  !package.json
  !package-lock.json
  !tsconfig.json
  docs/kube-mind-*.json
  ```
- For local development, set the environment variable in your shell profile, **not in any code file**:
  ```bash
  export GOOGLE_APPLICATION_CREDENTIALS="$HOME/.config/gcp/kube-mind-dev.json"
  ```
- For CI, inject the key as a GitHub Actions secret (`GCP_CREDENTIALS_JSON`) and write it to a temp file at build time, never committed
- For production on GKE, use Workload Identity Federation (see Task 10.3)

#### Task 0.5 — Audit & Fix Observer ClusterRole Permissions ⚠️ CRITICAL SECURITY

**Fixes:** `pod_controller.go:51` kubebuilder RBAC marker includes `create;update;patch;delete` verbs, contradicting the read-only design principle.

**DoD:** The deployed ClusterRole for the Observer only contains `get`, `list`, and `watch` verbs. Running `kubectl auth can-i create pods --as=system:serviceaccount:default:kube-mind-observer-sa` returns `no`.

Sub-tasks:
- Inspect the currently deployed ClusterRole:
  ```bash
  kubectl get clusterrole kube-mind-observer -o yaml
  ```
- If the Helm chart at `deploy/helm/observer/templates/clusterrole.yaml` already lists only read verbs, it overrides the kubebuilder marker — confirm this is the case and document it explicitly
- If write verbs appear in the deployed ClusterRole, patch it immediately:
  ```bash
  kubectl edit clusterrole kube-mind-observer
  # Remove: create, update, patch, delete from all resource rules
  ```
- In the new Python Brain Helm chart (`deploy/helm/brain-python/`), add a note in `templates/clusterrole.yaml` comments: "Brain has NO cluster permissions — all changes are via GitHub PRs only." The Brain has no ClusterRole at all.
- Add a CI gate to the new Helm chart using `conftest`/`kube-score` to assert no write verbs appear in any ClusterRole:
  ```bash
  kube-score score deploy/helm/observer/templates/clusterrole.yaml
  ```

---

### Phase 1: Python Project Foundation

**Goal:** A runnable Python app with proper config loading, logging, and proto bindings.

#### Task 1.1 — Proto Code Generation

**DoD:** `generated/incident_pb2.py` and `generated/incident_pb2_grpc.py` exist and import cleanly.

Sub-tasks:
- Create `brain-python/proto/incident.proto` as a symlink to (or copy of) `brain/src/KubeMind.Brain.Shared/Protos/incident.proto`
- Write `scripts/generate_proto.sh`:
  ```bash
  #!/usr/bin/env bash
  python3 -m grpc_tools.protoc \
    -I./proto \
    -I./proto/google/protobuf \
    --python_out=./generated \
    --grpc_python_out=./generated \
    --pyi_out=./generated \
    proto/incident.proto
  ```
- Run the script; verify generated files import without errors:
  ```python
  from generated import incident_pb2, incident_pb2_grpc
  ctx = incident_pb2.IncidentContext(incident_id="test-1", pod_name="my-pod")
  assert ctx.incident_id == "test-1"
  ```
- Add script to `Makefile` target `make proto`

#### Task 1.2 — Pydantic Settings (`config.py`)

**DoD:** All configuration loads from environment variables with type validation; missing required vars raise `ValidationError` on startup. No credential path is ever set or read inside application code.

**Fixes:** Hardcoded `Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", @"c:\Users\Me\Desktop\...")` in `Program.cs:18`. The new code never touches this variable — Application Default Credentials (ADC) reads `GOOGLE_APPLICATION_CREDENTIALS` from the environment automatically before the process starts.

```python
# src/config.py
from pydantic_settings import BaseSettings, SettingsConfigDict
import google.auth
import google.auth.exceptions

class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8")

    # GCP — GOOGLE_APPLICATION_CREDENTIALS is intentionally NOT a Settings field.
    # It is read directly by the google-auth library from the OS environment.
    # Never set it in code. Set it in your shell, CI secret, or K8s Secret volume mount.
    gcp_project_id: str
    gcp_location: str = "us-central1"

    # LLM
    gemini_model_id: str = "gemini-2.5-pro"

    # Redis
    redis_url: str = "redis://localhost:6379"
    deduplication_ttl_seconds: int = 300

    # Qdrant
    qdrant_host: str = "localhost"
    qdrant_port: int = 6334
    qdrant_collection: str = "k8s_incidents"
    qdrant_similarity_threshold: float = 0.95  # semantic dedup threshold

    # GitHub
    github_token: str
    github_default_repo_owner: str = ""
    github_default_repo_name: str = ""
    github_default_base_branch: str = "main"

    # Slack
    slack_webhook_url: str = ""

    # Server
    grpc_port: int = 50051
    http_port: int = 5081

    # Observability
    otlp_endpoint: str = ""
    log_level: str = "INFO"

settings = Settings()
```

Sub-tasks:
- Implement `config.py` as above
- Add an ADC startup validation in `main.py` that runs before any GCP client is created — fail fast with a clear error if credentials are not resolvable:
  ```python
  def validate_gcp_credentials():
      try:
          credentials, project = google.auth.default()
      except google.auth.exceptions.DefaultCredentialsError as e:
          raise RuntimeError(
              "GCP Application Default Credentials not found. "
              "Set GOOGLE_APPLICATION_CREDENTIALS to a service account key path, "
              "or run 'gcloud auth application-default login' for local dev."
          ) from e
  ```
- Write unit test: verify `Settings()` raises on missing required fields
- Write unit test: verify defaults are applied correctly

#### Task 1.3 — Structured Logging (`observability/logging_config.py`)

**DoD:** All log output is JSON (matching current Serilog compact JSON format) with `incident_id` as a bound context variable.

Sub-tasks:
- Configure `structlog` with JSON renderer:
  ```python
  import structlog, logging
  structlog.configure(
      processors=[
          structlog.contextvars.merge_contextvars,
          structlog.stdlib.add_log_level,
          structlog.stdlib.add_logger_name,
          structlog.processors.TimeStamper(fmt="iso"),
          structlog.processors.JSONRenderer(),
      ],
      wrapper_class=structlog.make_filtering_bound_logger(logging.INFO),
      context_class=dict,
      logger_factory=structlog.PrintLoggerFactory(),
  )
  ```
- Use `structlog.contextvars.bind_contextvars(incident_id=...)` in gRPC handler to propagate context across all log calls
- Verify `structlog.get_logger().info("test", foo="bar")` outputs valid JSON

#### Task 1.4 — Extend Proto Contract: Add `cluster_id` Field

**Fixes:** `cluster_id` missing from `IncidentContext` proto, causing `"default-cluster"` to be hardcoded everywhere in the Brain. This is a backwards-compatible addition — proto3 unset fields default to empty string, so the existing Go Observer sends `""` and the Brain substitutes a configured default. No Observer code change is needed.

**DoD:** `incident.proto` has an optional `cluster_id` field; the Python Brain falls back to `settings.default_cluster_id` when the field is empty; multi-cluster Qdrant queries use the real cluster ID.

Sub-tasks:
- Add field to `brain-python/proto/incident.proto`:
  ```protobuf
  message IncidentContext {
    string incident_id = 1;
    string pod_name = 2;
    string pod_namespace = 3;
    string failure_reason = 4;
    string logs = 5;
    string pod_manifest_json = 6;
    string deployment_manifest_json = 7;
    google.protobuf.Timestamp timestamp = 8;
    string cluster_id = 9;  // optional — empty string means "use server default"
  }
  ```
- Re-run `make proto` to regenerate `incident_pb2.py`
- Add `default_cluster_id: str = "default-cluster"` to `Settings`
- In every place that uses `cluster_id`, resolve it with:
  ```python
  cluster_id = incident.cluster_id or settings.default_cluster_id
  ```
- Update `§9 Configuration Reference` table with the new `DEFAULT_CLUSTER_ID` env var
- Note: The shared proto in `brain/src/KubeMind.Brain.Shared/Protos/incident.proto` is the authoritative source. Since the .NET Brain is being decommissioned, the Python copy in `brain-python/proto/` becomes the new authoritative source. Update the repo `README.md` to reflect this.

---

### Phase 2: gRPC Server — Replace .NET Ingestion Endpoint

**Goal:** A Python `grpc.aio` server that implements `IncidentService.StreamIncident` and accepts the same client-streaming RPC that the Go Observer currently sends to the .NET Brain.

#### Task 2.1 — Implement `grpc_server.py`

**DoD:** The Python gRPC server starts on port 50051, receives a test `IncidentContext` message from a Go Observer (or `grpcurl`), and logs it.

```python
# src/grpc_server.py
import grpc
import grpc.aio as aio
from generated import incident_pb2, incident_pb2_grpc
import structlog

log = structlog.get_logger()

class IncidentServicer(incident_pb2_grpc.IncidentServiceServicer):
    def __init__(self, incident_handler):
        # incident_handler is an async callable: (IncidentContext) -> None
        self._handler = incident_handler

    async def StreamIncident(self, request_iterator, context):
        log.info("grpc.stream_started")
        async for incident in request_iterator:
            log.info("grpc.incident_received",
                     incident_id=incident.incident_id,
                     pod_name=incident.pod_name,
                     namespace=incident.pod_namespace,
                     reason=incident.failure_reason)
            await self._handler(incident)
        log.info("grpc.stream_finished")
        return incident_pb2.StreamIncidentResponse(status="Incidents received and processed.")

async def serve_grpc(incident_handler, port: int):
    server = aio.server()
    incident_pb2_grpc.add_IncidentServiceServicer_to_server(
        IncidentServicer(incident_handler), server
    )
    server.add_insecure_port(f"0.0.0.0:{port}")
    await server.start()
    log.info("grpc.server_started", port=port)
    await server.wait_for_termination()
```

Sub-tasks:
- Implement `grpc_server.py` with the `IncidentServicer` class
- The `incident_handler` callable will be injected in `main.py` and will invoke the LangGraph graph
- **Implement stable dedup key — fixes timestamp-based `incident_id` from Observer (`pod_controller.go:123`)**. The Observer generates `incident_id` as `"{pod}-{container}-{reason}-{unix_timestamp}"`, meaning two events for the same failure 2 seconds apart produce different IDs and both pass through the Brain's Redis dedup. In the Brain, derive a stable dedup key from the deterministic fields instead of using the raw `incident_id` as the Redis key:
  ```python
  import hashlib

  def stable_dedup_key(incident) -> str:
      """Derives a stable identity key independent of the Observer's timestamp suffix."""
      raw = f"{incident.pod_namespace}/{incident.pod_name}/{incident.failure_reason}"
      return "kubemind:dedup:" + hashlib.sha256(raw.encode()).hexdigest()[:16]
  ```
  Pass this key to `RedisDeduplicationService.is_duplicate()` instead of `incident.incident_id`. The `incident_id` field is still forwarded to the graph for logging and tracing — it just isn't used as the Redis key.
- Add mTLS support (optional, matches Observer's `--grpc-insecure=false` mode):
  - Load `ca_cert`, `server_cert`, `server_key` from paths in `Settings`
  - Use `grpc.ssl_server_credentials(...)` if cert paths are configured
- Write integration test: spin up the Python server, send a test `IncidentContext` via a generated Python gRPC client stub, verify it was received
- Write dedup test: send two `IncidentContext` messages with the same pod/namespace/reason but different timestamps; verify only the first triggers graph execution

#### Task 2.2 — Implement `http_server.py` (FastAPI)

**DoD:** `GET /healthz` returns 200 and confirms LLM connectivity; `GET /` returns a plain-text status message.

```python
# src/http_server.py
from fastapi import FastAPI
from langchain_google_vertexai import ChatVertexAI
from src.config import settings

app = FastAPI(title="KubeMind Brain (Python)")

@app.get("/")
async def root():
    return {"status": "KubeMind Brain (Python) is online."}

@app.get("/healthz")
async def healthz():
    try:
        llm = ChatVertexAI(model=settings.gemini_model_id,
                           project=settings.gcp_project_id,
                           location=settings.gcp_location)
        result = await llm.ainvoke("Respond with a single word: OK")
        if result.content.strip() == "OK":
            return {"status": "healthy", "llm": "connected"}
        return {"status": "degraded", "llm": result.content}, 503
    except Exception as e:
        return {"status": "unhealthy", "error": str(e)}, 503
```

Sub-tasks:
- Implement `/healthz` endpoint
- Implement `/events` SSE endpoint (see Phase 6)
- Add OpenTelemetry FastAPI instrumentation middleware
- Add request ID middleware for trace correlation

#### Task 2.3 — Application Entry Point (`main.py`)

**DoD:** Running `python -m src.main` starts both the gRPC server (port 50051) and the HTTP server (port 5081) concurrently without blocking each other.

```python
# src/main.py
import asyncio
import uvicorn
from src.grpc_server import serve_grpc
from src.http_server import app
from src.graph.graph import create_graph
from src.config import settings
from src.observability.logging_config import configure_logging
from src.observability.tracing import configure_tracing

async def main():
    configure_logging()
    configure_tracing()
    
    graph = create_graph()  # Compiled LangGraph
    
    async def incident_handler(incident):
        await graph.ainvoke({"incident": incident})

    await asyncio.gather(
        serve_grpc(incident_handler, port=settings.grpc_port),
        uvicorn.Server(
            uvicorn.Config(app, host="0.0.0.0", port=settings.http_port, loop="asyncio")
        ).serve(),
    )

if __name__ == "__main__":
    asyncio.run(main())
```

Sub-tasks:
- Implement `main.py` as above
- Verify both servers start without port conflicts
- Verify graceful shutdown on `SIGTERM` (important for Kubernetes pod lifecycle)

---

### Phase 3: LangGraph State & Graph Definition

**Goal:** Define the typed state schema and wire up all nodes and edges into a compiled LangGraph that faithfully replicates the SK SOP.

#### Task 3.1 — Define `IncidentGraphState` (`graph/state.py`)

**DoD:** The state TypedDict compiles, and a test can instantiate it with an `IncidentContext` proto message.

```python
# src/graph/state.py
from typing import TypedDict, Optional, Annotated
from generated.incident_pb2 import IncidentContext
from langchain_core.messages import BaseMessage
import operator

class IncidentGraphState(TypedDict):
    # Input
    incident: IncidentContext             # Raw proto message from Observer

    # Enrichment
    enriched_goal: str                    # Goal string with historical context injected
    historical_context: str              # Retrieved from Qdrant

    # LangGraph message list for ReAct agent loop
    messages: Annotated[list[BaseMessage], operator.add]

    # Extracted results from tool calls
    pod_status: Optional[str]
    diagnosis: Optional[str]             # JSON from analyze_incident
    proposed_fix: Optional[str]         # Code/config change string

    # Safety gate
    safety_result: Optional[str]        # "YES" or "NO"

    # Outcome
    pr_url: Optional[str]
    outcome: Optional[str]              # "pr_created" | "safety_blocked" | "duplicate" | "error"
    error_message: Optional[str]

    # Streaming events (accumulated for SSE)
    stream_events: Annotated[list[str], operator.add]
```

Sub-tasks:
- Implement `state.py` as above
- Verify the TypedDict is compatible with LangGraph's `StateGraph(IncidentGraphState)`

#### Task 3.2 — Define Non-Tool Nodes (`graph/nodes.py`)

**DoD:** Each node function is a pure async function that takes `IncidentGraphState` and returns a partial state dict.

Nodes to implement:

**`deduplicate_node`:**
```python
async def deduplicate_node(state: IncidentGraphState) -> dict:
    svc = get_deduplication_service()
    is_dup = await svc.is_duplicate(state["incident"].incident_id)
    if is_dup:
        return {"outcome": "duplicate",
                "stream_events": ["Incident is a duplicate, skipping."]}
    return {"stream_events": [f"New Incident received: {state['incident'].incident_id}"]}
```

**`enrich_memory_node`:**
```python
async def enrich_memory_node(state: IncidentGraphState) -> dict:
    incident = state["incident"]
    svc = get_enrichment_service()
    original_goal = build_sop_goal(incident)
    enriched_goal, context = await svc.enrich(incident.logs, original_goal)
    return {
        "enriched_goal": enriched_goal,
        "historical_context": context,
        "stream_events": ["Memory enrichment complete."],
        "messages": [HumanMessage(content=enriched_goal)]
    }
```

**`write_memory_node`:**
```python
async def write_memory_node(state: IncidentGraphState) -> dict:
    # Fixes: hardcoded "default-cluster" in IncidentService.cs.
    # Resolve cluster_id from the proto field added in Task 1.4, with settings fallback.
    incident = state["incident"]
    cluster_id = incident.cluster_id or settings.default_cluster_id
    svc = get_memory_consolidation_service()
    await svc.enqueue(
        incident_id=incident.incident_id,
        cluster_id=cluster_id,
        namespace=incident.pod_namespace,
        raw_log=incident.logs,
        resolution=state.get("pr_url") or state.get("outcome") or "unknown"
    )
    return {"stream_events": ["Memory write-behind enqueued."]}
```

**`safety_blocked_node`:**
```python
async def safety_blocked_node(state: IncidentGraphState) -> dict:
    slack = get_slack_service()
    msg = f"Automated remediation BLOCKED for {state['incident'].incident_id}: safety check returned NO."
    await slack.notify(msg)
    return {
        "outcome": "safety_blocked",
        "stream_events": [f"Remediation blocked: safety check failed for {state['incident'].incident_id}"]
    }
```

Sub-tasks:
- Implement all 4 node functions in `graph/nodes.py`
- Wire service dependencies via module-level singletons initialized from `settings` (avoid DI framework complexity; Python singletons are sufficient)
- Write unit tests for each node with mocked services

#### Task 3.4 — Externalize the SOP Prompt to a Template File

**Fixes:** The 40-line SOP prompt embedded as a string literal inside `IncidentService.cs:58-92`. Prompts are configuration, not code — they need to be tuned, tested, and reviewed independently of the service logic.

**DoD:** The agent SOP lives in `brain-python/prompts/sop.j2`; `build_sop_goal()` renders it via Jinja2; changing the prompt requires no code change.

Sub-tasks:
- Add `jinja2 = "^3.1"` to `pyproject.toml`
- Create `brain-python/prompts/sop.j2`:
  ```jinja
  You are Kube-Mind, an autonomous Site Reliability Engineer (SRE).
  Your mission is to diagnose and fix the reported Kubernetes incident.

  Follow this STANDARD OPERATING PROCEDURE (SOP) strictly:

  1. **Gather Context**: Call `get_pod_status` to get the current state of the pod.
  2. **Diagnose Root Cause**: Call `analyze_incident` with the incident context JSON below.
  3. **Formulate Fix**: Based on the diagnosis, draft the specific code/configuration change required.
  4. **Safety Validation (CRITICAL)**: Call `is_code_change_safe` with your proposed changes.
     - If the result is "NO", STOP and report the safety violation. Do NOT proceed to step 5.
  5. **Apply Fix**: IF AND ONLY IF the safety check returned "YES", call `create_fix_pull_request`.
  6. **Report**: Summarize your actions and the outcome (PR link or safety failure reason).

  ---
  **INCIDENT CONTEXT:**
  {{ incident_json }}
  ---

  {% if historical_context %}
  **HISTORICAL CONTEXT FROM MEMORY:**
  {{ historical_context }}
  ---
  {% endif %}
  ```
- Implement `build_sop_goal(incident, historical_context="") -> str` in `graph/nodes.py`:
  ```python
  from jinja2 import Environment, FileSystemLoader
  from google.protobuf.json_format import MessageToDict
  import json

  _jinja_env = Environment(
      loader=FileSystemLoader("prompts"),
      autoescape=False,
  )

  def build_sop_goal(incident, historical_context: str = "") -> str:
      template = _jinja_env.get_template("sop.j2")
      # Use proto-native serialisation — fixes System.Text.Json + Protobuf mismatch
      incident_dict = MessageToDict(incident, preserving_proto_field_name=True)
      return template.render(
          incident_json=json.dumps(incident_dict, indent=2),
          historical_context=historical_context,
      )
  ```
- Note: `MessageToDict(preserving_proto_field_name=True)` produces `snake_case` keys matching the proto field names. This is the proto-native approach and avoids the `System.Text.Json` + Protobuf class mismatch that caused silent data loss in `K8sDiagnosticsPlugin.cs:29`.
- Write unit test: render the template with a test `IncidentContext` and assert the output contains the incident ID and SOP step headers

#### Task 3.3 — Build and Compile the LangGraph (`graph/graph.py`)

**DoD:** `create_graph()` returns a compiled `CompiledGraph` that can be invoked with a test state dict.

```python
# src/graph/graph.py
from langgraph.graph import StateGraph, END
from langgraph.prebuilt import ToolNode
from langchain_google_vertexai import ChatVertexAI
from src.graph.state import IncidentGraphState
from src.graph.nodes import (
    deduplicate_node, enrich_memory_node, write_memory_node, safety_blocked_node
)
from src.tools import (
    get_pod_status, analyze_incident, is_code_change_safe, create_fix_pull_request
)
from src.config import settings

TOOLS = [get_pod_status, analyze_incident, is_code_change_safe, create_fix_pull_request]

def route_after_deduplicate(state: IncidentGraphState) -> str:
    return "end" if state.get("outcome") == "duplicate" else "enrich"

def route_after_polycheck(state: IncidentGraphState) -> str:
    # Inspect the last AI message for tool result containing "NO"
    # LangGraph ToolNode surfaces this in state["messages"]
    # Extract from messages or state-level safety_result set by tools
    if state.get("safety_result") == "NO":
        return "blocked"
    return "write_memory"

def create_graph() -> CompiledGraph:
    llm = ChatVertexAI(
        model=settings.gemini_model_id,
        project=settings.gcp_project_id,
        location=settings.gcp_location,
    ).bind_tools(TOOLS)

    def agent_node(state: IncidentGraphState) -> dict:
        response = llm.invoke(state["messages"])
        return {"messages": [response],
                "stream_events": [f"Agent: {response.content[:200]}"]}

    tool_node = ToolNode(TOOLS)

    def should_continue(state: IncidentGraphState) -> str:
        last_message = state["messages"][-1]
        if hasattr(last_message, "tool_calls") and last_message.tool_calls:
            return "tools"
        return "route_after_polycheck"

    builder = StateGraph(IncidentGraphState)

    builder.add_node("deduplicate", deduplicate_node)
    builder.add_node("enrich", enrich_memory_node)
    builder.add_node("agent", agent_node)
    builder.add_node("tools", tool_node)
    builder.add_node("safety_blocked", safety_blocked_node)
    builder.add_node("write_memory", write_memory_node)

    builder.set_entry_point("deduplicate")

    builder.add_conditional_edges("deduplicate", route_after_deduplicate, {
        "end": END,
        "enrich": "enrich"
    })
    builder.add_edge("enrich", "agent")
    builder.add_conditional_edges("agent", should_continue, {
        "tools": "tools",
        "route_after_polycheck": "route_after_polycheck_node"
    })
    builder.add_edge("tools", "agent")
    builder.add_conditional_edges("route_after_polycheck_node", route_after_polycheck, {
        "blocked": "safety_blocked",
        "write_memory": "write_memory"
    })
    builder.add_edge("safety_blocked", END)
    builder.add_edge("write_memory", END)

    return builder.compile()
```

Sub-tasks:
- Implement `graph.py` as above; refine conditional routing logic after integration testing
- Add `MemorySaver` checkpointer for per-incident state replay (optional, enable via `settings.enable_checkpointer`)
- Write a graph smoke test: mock all tools and services, invoke graph with a test `IncidentContext`, verify it reaches `END` without errors

---

### Phase 4: Tool Implementations

**Goal:** Python `@tool` decorated functions that exactly replicate the behaviour of the 4 SK plugins.

#### Task 4.1 — `get_pod_status` Tool (`tools/kubernetes_tool.py`)

Replaces: `KubernetesPlugin.GetPodStatus`

**Fixes:** The .NET `KubernetesPlugin` always returns a hardcoded mock (`Status = "Running"`, `Restarts = 0`) regardless of the actual pod state. The LLM reasoned against fabricated data. This implementation must call the real Kubernetes API — a mock here is not acceptable at any stage of development.

**Hard DoD requirement:** Running `get_pod_status("my-crashing-pod", "default")` against a real cluster where that pod is in `CrashLoopBackOff` must return a response containing the actual restart count and container state. A unit test may mock the `kubernetes` client, but the integration test must hit a real cluster (use `kind` locally).

```python
from langchain_core.tools import tool
from kubernetes import client, config as k8s_config

@tool
def get_pod_status(pod_name: str, namespace: str) -> str:
    """Gets the current status and conditions of a Kubernetes pod by name and namespace."""
    try:
        k8s_config.load_incluster_config()
    except:
        k8s_config.load_kube_config()
    v1 = client.CoreV1Api()
    pod = v1.read_namespaced_pod(name=pod_name, namespace=namespace)
    status = {
        "phase": pod.status.phase,
        "conditions": [{"type": c.type, "status": c.status} for c in (pod.status.conditions or [])],
        "container_statuses": [
            {
                "name": cs.name,
                "ready": cs.ready,
                "restart_count": cs.restart_count,
                "state": str(cs.state)
            } for cs in (pod.status.container_statuses or [])
        ]
    }
    return json.dumps(status)
```

Sub-tasks:
- Implement the tool using `kubernetes` Python client (`pip install kubernetes`)
- Handle `load_incluster_config()` for in-cluster and `load_kube_config()` for local dev (same pattern as Go client-go)
- Add `kubernetes = "^31.0"` to `pyproject.toml`
- Write unit test mocking the K8s API call

#### Task 4.2 — `analyze_incident` Tool (`tools/diagnostics_tool.py`)

Replaces: `K8sDiagnosticsPlugin.AnalyzeIncident`

**Important:** Unlike the current .NET placeholder implementation (which returns a hardcoded stub), the Python version must invoke the LLM directly within the tool. This is a clean improvement over the current codebase.

```python
from langchain_core.tools import tool
from langchain_google_vertexai import ChatVertexAI
from src.config import settings
import json

@tool
def analyze_incident(incident_context_json: str) -> str:
    """Analyzes Kubernetes pod logs and manifests to diagnose the root cause of a failure.
    Returns a JSON string with rootCause, confidence, recommendedAction, and supportingEvidence."""
    incident = json.loads(incident_context_json)
    prompt = f"""You are an expert Kubernetes SRE.
Analyze the following incident and return ONLY a minified JSON object with keys:
rootCause, confidence (High/Medium/Low), recommendedAction, supportingEvidence.

Incident ID: {incident.get('incident_id')}
Pod: {incident.get('pod_namespace')}/{incident.get('pod_name')}
Failure Reason: {incident.get('failure_reason')}

Pod Manifest:
{incident.get('pod_manifest_json', '{}')}

Deployment Manifest:
{incident.get('deployment_manifest_json', '{}')}

Recent Logs:
{incident.get('logs', '')}

Rules:
- For OOMKilled: check memory limits in pod manifest
- For CrashLoopBackOff: look for stack traces and startup failures in logs
- For ImagePullBackOff: check image tag and pull secret configuration
"""
    llm = ChatVertexAI(model=settings.gemini_model_id,
                       project=settings.gcp_project_id,
                       location=settings.gcp_location)
    result = llm.invoke(prompt)
    return result.content
```

Sub-tasks:
- Implement the tool with actual LLM invocation (fixing the placeholder in the existing .NET code)
- Ensure JSON output is always valid; add a try/except with fallback JSON if the LLM returns malformed output
- Write unit test mocking `ChatVertexAI.invoke`

#### Task 4.3 — `is_code_change_safe` Tool (`tools/polycheck_tool.py`)

Replaces: `PolycheckPlugin.IsCodeChangeSafe`

```python
from langchain_core.tools import tool
from langchain_google_vertexai import ChatVertexAI
from src.config import settings
from src.graph.state import IncidentGraphState  # For state mutation side-effect
import structlog

log = structlog.get_logger()

# Module-level mutable ref for the current graph invocation's safety result
# Note: LangGraph tools cannot mutate state directly; we use the return value
# and the graph routing logic reads safety_result from state via message parsing.

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
    llm = ChatVertexAI(model=settings.gemini_model_id,
                       project=settings.gcp_project_id,
                       location=settings.gcp_location)
    result = llm.invoke(prompt)
    answer = result.content.strip().upper()
    verdict = "YES" if answer == "YES" else "NO"
    log.info("polycheck.result", verdict=verdict)
    return verdict
```

Sub-tasks:
- Implement the tool
- Write unit test: verify "YES" for a `resources.limits.memory: 128Mi` change
- Write unit test: verify "NO" for a `kubectl delete deployment` change

#### Task 4.4 — `create_fix_pull_request` Tool (`tools/gitops_tool.py`)

Replaces: `GitOpsPlugin.CreateFixPullRequest`

```python
from langchain_core.tools import tool
from src.services.github_service import GitHubService
from src.services.slack_service import SlackNotificationService
from src.config import settings
import structlog

log = structlog.get_logger()

@tool
async def create_fix_pull_request(
    repository_owner: str,
    repository_name: str,
    base_branch: str,
    new_branch_name: str,
    commit_message: str,
    file_path: str,
    file_content: str,
    pull_request_title: str,
    pull_request_body: str,
) -> str:
    """Creates a Pull Request in a GitHub repository with a proposed infrastructure fix.
    Returns the URL of the created Pull Request."""
    github = GitHubService(settings.github_token)
    slack = SlackNotificationService(settings.slack_webhook_url)

    await github.create_branch(repository_owner, repository_name, base_branch, new_branch_name)
    await github.create_or_update_file(
        repository_owner, repository_name, new_branch_name,
        file_path, file_content, commit_message
    )
    pr_url = await github.create_pull_request(
        repository_owner, repository_name,
        base_branch, new_branch_name,
        pull_request_title, pull_request_body
    )
    log.info("gitops.pr_created", pr_url=pr_url)
    if settings.slack_webhook_url:
        await slack.notify(f"Automated Fix Proposed! Review: {pr_url}")
    return pr_url
```

Sub-tasks:
- Implement the tool
- Implement `GitHubService` (see Phase 7)
- Write integration test against a real test GitHub repository (use a dedicated `kube-mind-test` repo)
- Verify PR is created with correct branch, commit, title, and body

---

### Phase 5: Cognitive Memory — Enrichment & Write-Behind

**Goal:** Port the `EnrichmentService` and `MemoryConsolidationService` to Python with identical semantics.

#### Task 5.1 — Redis Deduplication Service (`services/deduplication.py`)

**DoD:** `is_duplicate(incident_id)` returns `True` within 5 minutes of first call with the same ID; returns `False` on first call.

```python
# src/services/deduplication.py
import redis.asyncio as aioredis
from src.config import settings

class RedisDeduplicationService:
    def __init__(self):
        self._redis = aioredis.from_url(settings.redis_url)
        self._ttl = settings.deduplication_ttl_seconds

    async def is_duplicate(self, incident_id: str) -> bool:
        key = f"kubemind:incident:{incident_id}"
        # SET NX EX: set key only if not exists, with TTL
        result = await self._redis.set(key, "processed", nx=True, ex=self._ttl)
        # Returns True (value set = new) when NOT a duplicate
        return result is None  # None means key already existed
```

Sub-tasks:
- Implement `RedisDeduplicationService` using `redis.asyncio`
- Write unit test mocking Redis SET NX EX

#### Task 5.2 — Enrichment Service (`services/enrichment.py`)

**DoD:** `enrich(log_text, original_goal)` returns an enriched goal string with historical context appended, matching the format of `EnrichmentService.cs`.

```python
# src/services/enrichment.py
from langchain_google_vertexai import VertexAIEmbeddings
from qdrant_client import AsyncQdrantClient
from qdrant_client.models import Distance, VectorParams, SearchRequest
from src.config import settings
import structlog

log = structlog.get_logger()

class EnrichmentService:
    def __init__(self):
        self._embedder = VertexAIEmbeddings(
            model_name="text-embedding-004",
            project=settings.gcp_project_id,
            location=settings.gcp_location,
        )
        self._qdrant = AsyncQdrantClient(
            host=settings.qdrant_host, port=settings.qdrant_port, prefer_grpc=True
        )
        self._collection = settings.qdrant_collection

    async def enrich(self, log_text: str, original_goal: str) -> tuple[str, str]:
        try:
            vector = await self._embedder.aembed_query(log_text)
            results = await self._qdrant.search(
                collection_name=self._collection,
                query_vector=vector,
                limit=3,
                with_payload=True,
            )
            if not results:
                return original_goal, ""
            context_lines = []
            for r in results:
                payload = r.payload or {}
                context_lines.append(
                    f"- Past Incident: {payload.get('raw_log', '')}\n"
                    f"  Resolution: {payload.get('resolution_action', '')}"
                )
            context = "\n".join(context_lines)
            enriched = f"{original_goal}\n\nHISTORICAL CONTEXT FROM MEMORY:\n{context}"
            return enriched, context
        except Exception as e:
            log.warning("enrichment.failed", error=str(e))
            return original_goal, ""
```

Sub-tasks:
- Implement `EnrichmentService`
- Implement `VectorDbInitializer` — an async startup task called from `main.py` that ensures the `k8s_incidents` Qdrant collection exists:
  ```python
  async def ensure_qdrant_collection(qdrant: AsyncQdrantClient, collection: str):
      existing = [c.name for c in (await qdrant.get_collections()).collections]
      if collection not in existing:
          await qdrant.create_collection(
              collection_name=collection,
              vectors_config=VectorParams(size=768, distance=Distance.COSINE)
          )
          log.info("qdrant.collection_created", collection=collection)
  ```
- Write integration test against local Qdrant: seed one memory, verify it is returned by `enrich`

#### Task 5.3 — Memory Consolidation Service (`services/memory_consolidation.py`)

**DoD:** After processing an incident, its `raw_log + resolution` is asynchronously upserted to Qdrant; near-duplicate logs (cosine similarity >= 0.95) are skipped.

```python
# src/services/memory_consolidation.py
import asyncio
import uuid
from dataclasses import dataclass
from langchain_google_vertexai import VertexAIEmbeddings
from qdrant_client import AsyncQdrantClient
from qdrant_client.models import PointStruct
from src.config import settings
import structlog

log = structlog.get_logger()

@dataclass
class IncidentResolution:
    incident_id: str
    cluster_id: str
    namespace: str
    raw_log: str
    resolution: str

class MemoryConsolidationService:
    def __init__(self):
        self._queue: asyncio.Queue[IncidentResolution] = asyncio.Queue(maxsize=100)
        self._embedder = VertexAIEmbeddings(
            model_name="text-embedding-004",
            project=settings.gcp_project_id,
            location=settings.gcp_location,
        )
        self._qdrant = AsyncQdrantClient(
            host=settings.qdrant_host, port=settings.qdrant_port, prefer_grpc=True
        )

    async def enqueue(self, **kwargs):
        resolution = IncidentResolution(**kwargs)
        await self._queue.put(resolution)

    async def run(self):
        """Long-running background consumer. Call via asyncio.create_task()."""
        while True:
            resolution = await self._queue.get()
            try:
                await self._consolidate(resolution)
            except Exception as e:
                log.error("memory_consolidation.failed", error=str(e),
                           incident_id=resolution.incident_id)
            finally:
                self._queue.task_done()

    async def _consolidate(self, r: IncidentResolution):
        vector = await self._embedder.aembed_query(r.raw_log)
        # Semantic deduplication: skip if near-identical memory exists
        results = await self._qdrant.search(
            collection_name=settings.qdrant_collection,
            query_vector=vector, limit=1, with_payload=False
        )
        if results and results[0].score >= settings.qdrant_similarity_threshold:
            log.info("memory_consolidation.skipped_duplicate",
                      incident_id=r.incident_id, score=results[0].score)
            return
        point = PointStruct(
            id=str(uuid.uuid4()),
            vector=vector,
            payload={
                "cluster_id": r.cluster_id,
                "namespace": r.namespace,
                "raw_log": r.raw_log,
                "resolution_action": r.resolution,
                "incident_id": r.incident_id,
            }
        )
        await self._qdrant.upsert(collection_name=settings.qdrant_collection, points=[point])
        log.info("memory_consolidation.saved", incident_id=r.incident_id)
```

Sub-tasks:
- Implement `MemoryConsolidationService`
- Start `memory_consolidation_service.run()` as an `asyncio.create_task()` in `main.py`
- Write unit test: verify deduplication logic skips points above the threshold
- Write integration test: verify a new point is upserted to Qdrant after `enqueue()`

---

### Phase 6: Real-time Event Streaming — Replace SignalR

**Goal:** Provide an equivalent to ASP.NET Core SignalR that streams agent thoughts to a browser UI in real time. The Python replacement uses **Server-Sent Events (SSE)** which has simpler browser integration than WebSockets and no special library needed on the client side.

#### Task 6.1 — SSE Event Bus (`services/event_bus.py`)

**DoD:** Clients subscribe to a specific incident's event stream. No client ever receives events belonging to a different incident.

**Fixes:** `AgentStreamingFilter.cs` called `hubContext.Clients.All.SendAsync(...)`, broadcasting every incident's full processing details — including pod manifests, diagnosis, proposed code changes, and PR content — to every connected browser tab. This is both a data isolation failure and a security concern.

```python
# src/services/event_bus.py
import asyncio
from typing import AsyncGenerator

class EventBus:
    """Per-incident topic bus. Subscribers only receive events for the incident_id they request."""

    def __init__(self):
        # topic → list of subscriber queues
        self._topics: dict[str, list[asyncio.Queue]] = {}

    async def publish(self, incident_id: str, message: str):
        """Publish an event to all subscribers of a specific incident."""
        for q in self._topics.get(incident_id, []):
            await q.put(message)

    async def subscribe(self, incident_id: str) -> AsyncGenerator[str, None]:
        """Subscribe to events for a specific incident only."""
        q: asyncio.Queue[str] = asyncio.Queue()
        self._topics.setdefault(incident_id, []).append(q)
        try:
            while True:
                msg = await asyncio.wait_for(q.get(), timeout=120.0)
                yield msg
        except asyncio.TimeoutError:
            return  # Close the stream after 2 min of inactivity
        finally:
            self._topics[incident_id].remove(q)
            if not self._topics[incident_id]:
                del self._topics[incident_id]

event_bus = EventBus()  # Module-level singleton
```

#### Task 6.2 — SSE HTTP Endpoint

```python
# In src/http_server.py
from sse_starlette.sse import EventSourceResponse
from src.services.event_bus import event_bus

@app.get("/events/{incident_id}")
async def stream_events(incident_id: str):
    """Per-incident SSE stream. Clients subscribe to /events/{incident_id}
    and only receive events for that specific incident."""
    async def generator():
        async for message in event_bus.subscribe(incident_id):
            yield {"data": message}
    return EventSourceResponse(generator())

@app.get("/events")
async def stream_events_no_id():
    """Returns 400 — incident_id is required. Prevents accidental global subscription."""
    return {"error": "incident_id is required. Use /events/{incident_id}"}, 400
```

Sub-tasks:
- Implement `EventBus` class in `services/event_bus.py` with per-topic isolation as above
- Add `/events/{incident_id}` SSE endpoint to `http_server.py`; reject calls to bare `/events`
- Update all `event_bus.publish()` call sites throughout nodes and tools to pass `incident_id` as first argument:
  ```python
  await event_bus.publish(state["incident"].incident_id, f"New Incident received: {incident_id}")
  await event_bus.publish(incident_id, f"Tool: Polycheck → verdict: {verdict}")
  await event_bus.publish(incident_id, f"PR Created: {pr_url}")
  ```
- Update `brain-python/static/index.html` to subscribe to a specific incident ID from a URL param:
  ```javascript
  const incidentId = new URLSearchParams(window.location.search).get("incident_id");
  const evtSource = new EventSource(`/events/${incidentId}`);
  evtSource.onmessage = (e) => {
      document.getElementById('log').innerHTML += `<p>${e.data}</p>`;
  };
  ```
- Write unit test: publish to `incident-A` and `incident-B`; assert a subscriber to `incident-A` receives only `incident-A` events

---

### Phase 7: GitHub & Slack Integrations

**Goal:** Python implementations of `GitHubService` and `SlackNotificationService`.

#### Task 7.1 — GitHub Service (`services/github_service.py`)

**DoD:** Can create a branch, commit a file, and open a PR in a test repository.

```python
# src/services/github_service.py
from github import Github, GithubException
import structlog

log = structlog.get_logger()

class GitHubService:
    def __init__(self, token: str):
        self._client = Github(token)

    async def create_branch(self, owner: str, repo: str, base: str, new_branch: str):
        import asyncio
        loop = asyncio.get_event_loop()
        await loop.run_in_executor(None, self._create_branch_sync, owner, repo, base, new_branch)

    def _create_branch_sync(self, owner: str, repo: str, base: str, new_branch: str):
        repository = self._client.get_repo(f"{owner}/{repo}")
        base_sha = repository.get_branch(base).commit.sha
        repository.create_git_ref(ref=f"refs/heads/{new_branch}", sha=base_sha)
        log.info("github.branch_created", branch=new_branch)

    async def create_or_update_file(self, owner, repo, branch, path, content, message):
        loop = asyncio.get_event_loop()
        await loop.run_in_executor(
            None, self._create_or_update_file_sync,
            owner, repo, branch, path, content, message
        )

    def _create_or_update_file_sync(self, owner, repo, branch, path, content, message):
        repository = self._client.get_repo(f"{owner}/{repo}")
        try:
            existing = repository.get_contents(path, ref=branch)
            repository.update_file(path, message, content, existing.sha, branch=branch)
        except GithubException:
            repository.create_file(path, message, content, branch=branch)
        log.info("github.file_committed", path=path)

    async def create_pull_request(self, owner, repo, base, head, title, body) -> str:
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(
            None, self._create_pr_sync, owner, repo, base, head, title, body
        )

    def _create_pr_sync(self, owner, repo, base, head, title, body) -> str:
        repository = self._client.get_repo(f"{owner}/{repo}")
        pr = repository.create_pull(title=title, body=body, base=base, head=head)
        log.info("github.pr_created", url=pr.html_url)
        return pr.html_url
```

Sub-tasks:
- Implement `GitHubService` using `PyGithub`
- Note: `PyGithub` is synchronous; wrap all calls in `run_in_executor` for async compatibility (alternative: use `httpx` with GitHub REST API directly for native async)
- Write integration test against a real test repo (use `GITHUB_TEST_REPO_OWNER` and `GITHUB_TEST_REPO_NAME` env vars)

#### Task 7.2 — Slack Notification Service (`services/slack_service.py`)

**DoD:** Posts a message to the configured Slack webhook URL.

```python
# src/services/slack_service.py
import httpx
import structlog

log = structlog.get_logger()

class SlackNotificationService:
    def __init__(self, webhook_url: str):
        self._webhook_url = webhook_url

    async def notify(self, message: str):
        if not self._webhook_url:
            log.warning("slack.webhook_not_configured")
            return
        async with httpx.AsyncClient() as client:
            response = await client.post(
                self._webhook_url,
                json={"text": message},
                timeout=10.0
            )
            response.raise_for_status()
            log.info("slack.notification_sent", message=message[:100])
```

Sub-tasks:
- Implement `SlackNotificationService`
- Write unit test mocking `httpx.AsyncClient.post`

---

### Phase 8: Observability, Structured Logging & Metrics

**Goal:** The Python Brain emits the same quality of observability signals as the .NET Brain.

#### Task 8.1 — OpenTelemetry Tracing (`observability/tracing.py`)

**DoD:** An incoming gRPC call generates an OpenTelemetry span visible in Jaeger or the OTLP exporter.

```python
# src/observability/tracing.py
from opentelemetry import trace
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from src.config import settings

def configure_tracing():
    provider = TracerProvider()
    if settings.otlp_endpoint:
        exporter = OTLPSpanExporter(endpoint=settings.otlp_endpoint)
        provider.add_span_processor(BatchSpanProcessor(exporter))
    trace.set_tracer_provider(provider)
    FastAPIInstrumentor().instrument()

tracer = trace.get_tracer("kubemind.brain")
```

Sub-tasks:
- Instrument gRPC server with spans: `with tracer.start_as_current_span("grpc.StreamIncident") as span: span.set_attribute("incident.id", incident_id)`
- Instrument each LangGraph node with child spans
- Instrument LLM calls in tools with spans including token count attributes
- Add `incident_id` as a span attribute on every span within a single incident processing run

#### Task 8.2 — Prometheus Metrics

**DoD:** `GET /metrics` returns Prometheus-format counters for incidents processed, duplicates skipped, PRs created, safety blocks, and errors.

Sub-tasks:
- Add `prometheus-client` to dependencies
- Define counters:
  ```python
  incidents_received = Counter("kubemind_incidents_received_total", "Total incidents received")
  incidents_duplicated = Counter("kubemind_incidents_duplicate_total", "Duplicate incidents skipped")
  prs_created = Counter("kubemind_prs_created_total", "GitHub PRs created")
  safety_blocks = Counter("kubemind_safety_blocks_total", "Incidents blocked by Polycheck")
  incident_duration = Histogram("kubemind_incident_duration_seconds", "End-to-end incident processing duration")
  ```
- Expose `/metrics` endpoint via FastAPI
- Increment counters from the appropriate graph nodes

---

### Phase 9: Containerization

**Goal:** A production-ready Docker image for the Python Brain.

#### Task 9.1 — Write `Dockerfile`

**DoD:** Image builds successfully; runs on port 50051 (gRPC) and 5081 (HTTP); passes `trivy` scan with no critical CVEs; image size < 500MB.

```dockerfile
# brain-python/Dockerfile
FROM python:3.12-slim AS builder

WORKDIR /app

# Install build dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential \
    && rm -rf /var/lib/apt/lists/*

COPY pyproject.toml poetry.lock ./
RUN pip install --no-cache-dir poetry && \
    poetry config virtualenvs.create false && \
    poetry install --only=main --no-root

# ── Runtime stage ──────────────────────────────────────────────────────────────
FROM python:3.12-slim AS runtime

WORKDIR /app

# Non-root user
RUN groupadd -r kubemind && useradd -r -g kubemind -d /app kubemind

COPY --from=builder /usr/local/lib/python3.12 /usr/local/lib/python3.12
COPY --from=builder /usr/local/bin /usr/local/bin
COPY --chown=kubemind:kubemind . .

USER kubemind

EXPOSE 50051 5081

HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD python -c "import httpx; httpx.get('http://localhost:5081/healthz').raise_for_status()"

CMD ["python", "-m", "src.main"]
```

Sub-tasks:
- Write `Dockerfile` as above
- Write `.dockerignore`: exclude `__pycache__`, `*.pyc`, `.git`, `tests/`, `docs/`
- Build and verify: `docker build -t kubemind-brain-python:latest .`
- Run locally and verify both ports respond
- Run `trivy image kubemind-brain-python:latest` and remediate any critical CVEs

#### Task 9.2 — Docker Compose for Local Dev

**DoD:** `docker compose up` starts Redis, Qdrant, and the Python Brain together.

```yaml
# brain-python/docker-compose.yml
services:
  redis:
    image: redis/redis-stack:latest
    ports: ["6379:6379", "8001:8001"]

  qdrant:
    image: qdrant/qdrant:latest
    ports: ["6333:6333", "6334:6334"]
    volumes: ["qdrant_data:/qdrant/storage"]

  brain-python:
    build: .
    ports: ["50051:50051", "5081:5081"]
    environment:
      - REDIS_URL=redis://redis:6379
      - QDRANT_HOST=qdrant
      - QDRANT_PORT=6334
      - GCP_PROJECT_ID=${GCP_PROJECT_ID}
      - GCP_LOCATION=us-central1
      - GOOGLE_APPLICATION_CREDENTIALS=/creds/gcp-key.json
      - GITHUB_TOKEN=${GITHUB_TOKEN}
      - SLACK_WEBHOOK_URL=${SLACK_WEBHOOK_URL}
    volumes:
      - ./docs/kube-mind-c205678d57e9.json:/creds/gcp-key.json:ro
    depends_on: [redis, qdrant]

volumes:
  qdrant_data:
```

Sub-tasks:
- Write `docker-compose.yml` as above
- Verify end-to-end by running the compose stack and sending a test gRPC message with `grpcurl`

---

### Phase 10: Kubernetes & Helm Deployment

**Goal:** Deploy the Python Brain to Kubernetes using a new or updated Helm chart, and update the Observer's gRPC target.

#### Task 10.1 — New Brain Helm Chart (`deploy/helm/brain-python/`)

**DoD:** `helm install km-brain-python deploy/helm/brain-python/` deploys the Python Brain with correct ports, secrets, and resource limits.

Create `deploy/helm/brain-python/` with:

**`Chart.yaml`:**
```yaml
apiVersion: v2
name: kube-mind-brain-python
description: KubeMind Brain — Python LangGraph
version: 1.0.0
appVersion: "1.0.0"
```

**`values.yaml`:**
```yaml
replicaCount: 1

image:
  repository: kubemind-brain-python
  pullPolicy: IfNotPresent
  tag: "latest"

service:
  grpcPort: 50051
  httpPort: 5081
  type: ClusterIP

env:
  gcpProjectId: ""
  gcpLocation: "us-central1"
  redisUrl: "redis://redis-service:6379"
  qdrantHost: "qdrant-service"
  qdrantPort: 6334
  logLevel: "INFO"
  grpcInsecure: true

secrets:
  # These are references to K8s Secrets:
  gcpCredentialsSecretName: "kube-mind-gcp-creds"
  gcpCredentialsKey: "key.json"
  githubTokenSecretName: "kube-mind-github"
  githubTokenKey: "token"
  slackWebhookSecretName: "kube-mind-slack"
  slackWebhookKey: "url"

resources:
  limits:
    cpu: "1"
    memory: 512Mi
  requests:
    cpu: 250m
    memory: 256Mi

livenessProbe:
  httpGet:
    path: /healthz
    port: 5081
  initialDelaySeconds: 60
  periodSeconds: 30

readinessProbe:
  httpGet:
    path: /healthz
    port: 5081
  initialDelaySeconds: 30
  periodSeconds: 10
```

Sub-tasks:
- Create all Helm templates: `deployment.yaml`, `service.yaml`, `configmap.yaml`, `serviceaccount.yaml`
- Mount GCP credentials JSON as a Secret volume (same approach as .NET Brain)
- Mount GitHub token and Slack webhook as Secret env vars
- Create `templates/service.yaml` exposing ClusterIP on ports 50051 and 5081
- The service name should default to `kube-mind-brain-python` to allow the Observer's `grpc.serverAddress` to be updated

#### Task 10.2 — Update Observer Helm Values

**DoD:** The Observer's gRPC target is updated to `kube-mind-brain-python:50051`.

Sub-tasks:
- In `deploy/helm/observer/values.yaml`, update:
  ```yaml
  grpc:
    serverAddress: "kube-mind-brain-python:50051"  # was: "kube-mind-brain:50051"
  ```
- No code changes to the Observer itself — only this Helm value changes
- Document the override in the cutover runbook (Phase 12)

#### Task 10.3 — Kubernetes Secrets Setup

**DoD:** All secrets are created in the cluster as K8s Secrets; the Brain pod starts with all env vars populated.

Sub-tasks:
- Create secrets (once per environment):
  ```bash
  # GCP credentials
  kubectl create secret generic kube-mind-gcp-creds \
    --from-file=key.json=docs/kube-mind-c205678d57e9.json

  # GitHub PAT
  kubectl create secret generic kube-mind-github \
    --from-literal=token=<YOUR_GITHUB_PAT>

  # Slack webhook
  kubectl create secret generic kube-mind-slack \
    --from-literal=url=<YOUR_SLACK_WEBHOOK_URL>
  ```
- Document secret rotation procedure
- For production: replace manual `kubectl create secret` with sealed-secrets or External Secrets Operator

#### Task 10.5 — Verify Observer ClusterRole is Read-Only ⚠️ CRITICAL SECURITY

**Fixes:** Kubebuilder RBAC marker on `pod_controller.go:51` includes `create;update;patch;delete` verbs. The Helm chart templates are the authoritative deployed resource, but they must be explicitly verified to not inherit the overly-permissive marker output.

**DoD:** `kubectl auth can-i create pods --as=system:serviceaccount:<namespace>:kube-mind-observer-sa` returns `no` in all namespaces. The `clusterrole.yaml` Helm template contains only `get`, `list`, `watch`.

Sub-tasks:
- Open `deploy/helm/observer/templates/clusterrole.yaml` and confirm every `verbs` entry is strictly `["get", "list", "watch"]`
- If any write verb is present, remove it from the template directly — this is the fix, since the Helm chart overrides whatever `controller-gen` would generate
- Add a `conftest.py` policy check that runs in CI against the rendered Helm templates:
  ```python
  # deploy/tests/test_rbac_policy.py
  FORBIDDEN_VERBS = {"create", "update", "patch", "delete", "deletecollection"}

  def test_observer_clusterrole_is_readonly(rendered_clusterrole):
      for rule in rendered_clusterrole["rules"]:
          actual = set(rule["verbs"])
          assert not actual & FORBIDDEN_VERBS, (
              f"Observer ClusterRole contains write verbs: {actual & FORBIDDEN_VERBS}"
          )
  ```
- Run `helm template km-observer deploy/helm/observer/ | kubectl auth reconcile --dry-run=client -f -` and confirm no write permissions surface

#### Task 10.4 — Deploy Qdrant to Cluster

**DoD:** Qdrant is running as a Kubernetes Deployment accessible at `qdrant-service:6334` within the cluster.

Sub-tasks:
- Add Qdrant Helm chart to cluster (or add inline templates to `deploy/helm/brain-python/`):
  ```bash
  helm repo add qdrant https://qdrant.to/helm
  helm install qdrant qdrant/qdrant -n kube-mind \
    --set persistence.size=10Gi
  ```
- Verify: `kubectl port-forward svc/qdrant 6334:6334 -n kube-mind` and `curl http://localhost:6333/healthz`

---

### Phase 11: Testing Strategy

**Goal:** A comprehensive test suite that gives equivalent or better coverage vs. the .NET Brain.

#### Task 11.1 — Unit Tests

**DoD:** `pytest tests/unit/` passes with >80% coverage.

| Test File | What It Tests |
|-----------|--------------|
| `test_deduplication.py` | Redis SET NX behaviour; TTL expiry simulation |
| `test_enrichment.py` | Qdrant search result aggregation; fallback on failure |
| `test_memory_consolidation.py` | Semantic dedup threshold; queue backpressure |
| `test_polycheck_tool.py` | YES verdict for value-only changes; NO for structural changes |
| `test_diagnostics_tool.py` | JSON parsing of LLM output; fallback on malformed output |
| `test_graph.py` | Full graph smoke test with all services mocked |
| `test_config.py` | Settings validation; missing required fields |

#### Task 11.2 — Integration Tests

**DoD:** `pytest tests/integration/` passes against real Redis, Qdrant, and a mock LLM.

| Test File | What It Tests |
|-----------|--------------|
| `test_grpc_server.py` | Send real gRPC `IncidentContext` to Python server; verify processing starts |
| `test_enrichment_live.py` | Seed Qdrant, call `enrich()`, verify context appended |
| `test_memory_consolidation_live.py` | Enqueue resolution, wait, verify point in Qdrant |
| `test_github_service.py` | Create branch + commit + PR in test repo; verify PR URL |
| `test_end_to_end.py` | Full flow: gRPC message → graph → PR created (using test repo) |

#### Task 11.3 — Performance Baseline

**DoD:** End-to-end processing (gRPC receipt → PR created) completes in < 60 seconds (p90) under single-incident load.

Sub-tasks:
- Add `@pytest.mark.benchmark` test using `pytest-benchmark`
- Measure and record baseline timings for each phase:
  - Redis dedup check: target <10ms
  - Qdrant enrichment (embed + search): target <800ms
  - LLM call (Gemini Analyze): target <5s
  - Polycheck call: target <3s
  - GitHub PR creation: target <5s
  - **Total end-to-end: target <60s**
- Establish CI performance gate: fail if p90 > 90s

---

### Phase 12: Cutover & Deprecation of .NET Brain

**Goal:** Migrate production traffic from the .NET Brain to the Python Brain with zero downtime.

#### Task 12.1 — Parallel Run (Canary)

**DoD:** Both .NET Brain and Python Brain run simultaneously; the Observer can be switched between them.

Sub-tasks:
- Deploy the Python Brain (`helm install km-brain-python`) alongside the running .NET Brain (`km-brain`)
- Create a test Observer instance pointing at the Python Brain (`grpc.serverAddress: "kube-mind-brain-python:50051"`)
- Run the test Observer in a non-production namespace
- Verify the Python Brain processes incidents correctly end-to-end before switching production traffic

#### Task 12.2 — Observer cutover

**DoD:** The production Observer's gRPC target is updated to the Python Brain and verified to be working.

Sub-tasks:
- Update the Observer Helm release with the new `grpc.serverAddress`:
  ```bash
  helm upgrade km-observer deploy/helm/observer/ \
    --set grpc.serverAddress="kube-mind-brain-python:50051"
  ```
- Monitor for 30 minutes:
  - Check `kubectl logs -l app=kube-mind-brain-python -f`
  - Verify incidents are received and processed
  - Verify PRs are being created in GitHub
  - Verify Slack notifications are arriving
- **Rollback plan:** If issues are detected within 30 minutes, restore:
  ```bash
  helm upgrade km-observer deploy/helm/observer/ \
    --set grpc.serverAddress="kube-mind-brain:50051"
  ```

#### Task 12.3 — Decommission .NET Brain

**DoD:** The .NET Brain Helm release is uninstalled; the `brain/` directory is archived.

Sub-tasks:
- After 7 days of stable operation with the Python Brain, decommission the .NET Brain:
  ```bash
  helm uninstall km-brain
  ```
- Archive the `brain/` directory (do not delete — keep for reference):
  ```bash
  git mv brain brain-dotnet-archived
  git commit -m "chore: archive .NET brain after LangGraph migration"
  ```
- Remove .NET-specific CI/CD pipeline steps
- Update `README.md` to reflect the new Python Brain setup and running instructions

---

## 8. Go Observer Controller Changes

**The Observer source code requires zero changes.** The only configuration update needed is:

| File | Field | Old Value | New Value |
|------|-------|-----------|-----------|
| `deploy/helm/observer/values.yaml` | `grpc.serverAddress` | `kube-mind-brain:50051` | `kube-mind-brain-python:50051` |

This is a Helm values change only, applied via `helm upgrade` as described in Phase 12.2.

The gRPC proto contract (`incident.proto`), the Observer's `BrainGrpcClient` implementation, and all reconciliation logic remain identical. The Python gRPC server exposes the exact same service definition (`service IncidentService { rpc StreamIncident(stream IncidentContext) returns (StreamIncidentResponse); }`), so the Observer has no way to distinguish between the .NET and Python backends.

---

## 9. Configuration Reference

All configuration is loaded via `pydantic-settings` from environment variables or a `.env` file.

### 9.1 Complete Environment Variable Reference

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `GCP_PROJECT_ID` | Yes | — | Google Cloud Project ID (same as existing `GCP:ProjectId`) |
| `GCP_LOCATION` | No | `us-central1` | GCP region for Vertex AI |
| `GOOGLE_APPLICATION_CREDENTIALS` | Yes | — | Path to GCP service account JSON key |
| `GEMINI_MODEL_ID` | No | `gemini-2.5-pro` | Gemini model for reasoning |
| `REDIS_URL` | Yes | — | Redis connection URL, e.g. `redis://localhost:6379` |
| `DEDUPLICATION_TTL_SECONDS` | No | `300` | Per-incident dedup window (matches existing 5-min default) |
| `QDRANT_HOST` | No | `localhost` | Qdrant server hostname |
| `QDRANT_PORT` | No | `6334` | Qdrant gRPC port |
| `QDRANT_COLLECTION` | No | `k8s_incidents` | Vector collection name (matches existing Qdrant collection) |
| `QDRANT_SIMILARITY_THRESHOLD` | No | `0.95` | Score threshold for semantic dedup (matches existing 0.95 threshold) |
| `GITHUB_TOKEN` | Yes | — | GitHub Personal Access Token (same as existing `GitHub:Token`) |
| `SLACK_WEBHOOK_URL` | No | `` | Slack incoming webhook URL (empty = notifications disabled) |
| `GRPC_PORT` | No | `50051` | gRPC server listen port |
| `HTTP_PORT` | No | `5081` | HTTP/SSE server listen port |
| `GRPC_TLS_CA_CERT` | No | `` | Path to CA cert for mTLS (empty = insecure mode) |
| `GRPC_TLS_SERVER_CERT` | No | `` | Path to server cert for mTLS |
| `GRPC_TLS_SERVER_KEY` | No | `` | Path to server key for mTLS |
| `OTLP_ENDPOINT` | No | `` | OpenTelemetry OTLP exporter endpoint (empty = console only) |
| `LOG_LEVEL` | No | `INFO` | Log level: DEBUG, INFO, WARNING, ERROR |

### 9.2 `.env.example`

```bash
# Copy to .env for local development
GCP_PROJECT_ID=your-gcp-project-id
GCP_LOCATION=us-central1
GOOGLE_APPLICATION_CREDENTIALS=../docs/kube-mind-c205678d57e9.json

GEMINI_MODEL_ID=gemini-2.5-pro

REDIS_URL=redis://localhost:6379
DEDUPLICATION_TTL_SECONDS=300

QDRANT_HOST=localhost
QDRANT_PORT=6334
QDRANT_COLLECTION=k8s_incidents
QDRANT_SIMILARITY_THRESHOLD=0.95

GITHUB_TOKEN=ghp_your_personal_access_token

SLACK_WEBHOOK_URL=https://hooks.slack.com/services/xxx/yyy/zzz

GRPC_PORT=50051
HTTP_PORT=5081
LOG_LEVEL=INFO
```

---

## 10. Data Flow: Before vs. After

### Before (Semantic Kernel / .NET)

```
Observer (Go)
  └─ gRPC → IncidentService.StreamIncident (.NET)
       ├─ Redis.SET NX (dedup)
       ├─ Vertex AI embed → Qdrant search → enrich goal string
       ├─ Kernel.InvokePromptAsync(enrichedGoal, FunctionChoiceBehavior.Auto())
       │   ├─ [auto] KubernetesPlugin.GetPodStatus()
       │   ├─ [auto] K8sDiagnosticsPlugin.AnalyzeIncident()
       │   ├─ [auto] PolycheckPlugin.IsCodeChangeSafe()
       │   └─ [auto] GitOpsPlugin.CreateFixPullRequest()
       ├─ SignalR → browser UI
       └─ MemoryBuffer → MemoryConsolidationService → Qdrant upsert
```

### After (LangGraph / Python)

```
Observer (Go)                              [UNCHANGED]
  └─ gRPC → IncidentServicer.StreamIncident (Python grpcio)
       └─ graph.ainvoke({"incident": ctx})
            ├─ deduplicate_node: Redis.SET NX EX (same TTL)
            ├─ enrich_memory_node: Vertex AI embed → Qdrant search → enrich goal
            ├─ agent_node: ChatVertexAI(Gemini 2.5-pro).bind_tools(TOOLS).invoke()
            │   ├─ [tool_call] get_pod_status()
            │   ├─ [tool_call] analyze_incident()
            │   ├─ [tool_call] is_code_change_safe()
            │   └─ [tool_call] create_fix_pull_request()
            ├─ EventBus.publish() → SSE /events → browser UI
            └─ write_memory_node → MemoryConsolidationService.enqueue()
                                  └─ [background] Vertex AI embed → Qdrant upsert
```

**Behavioural equivalence:** Every step of the SK SOP maps 1:1 to a LangGraph node or tool. The output — a GitHub PR with a diagnosis-driven fix, a Slack notification, and a persisted memory record — is identical.

---

## 11. Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| **LangGraph tool-calling loop doesn't terminate** — LLM keeps calling tools indefinitely | Medium | High | Set `recursion_limit=25` on `graph.compile()`; add a `max_iterations` guard in `should_continue` |
| **Gemini structured output differs from GPT-4o** — SK was tested against OpenAI format; Gemini may format tool calls differently | Medium | Medium | Test all 4 tools with Gemini specifically before cutover; use `with_structured_output()` for `analyze_incident` to enforce JSON |
| **Python grpcio async server performance** — async gRPC in Python is slower than .NET | Low | Medium | Benchmark at 10 concurrent incidents; add worker concurrency if needed via `grpc.aio` `maximum_concurrent_rpcs` |
| **K8sDiagnosticsPlugin was a placeholder in .NET** — the Python port fully implements the LLM call, which may surface new prompt engineering issues | High | Medium | Run the tool in isolation against all 4 failure classes (OOMKilled, CrashLoopBackOff, ImagePullBackOff, Error) and validate output format |
| **Vertex AI rate limits** — high-volume incidents could exhaust Vertex AI embedding quota | Medium | Medium | The `asyncio.Queue(maxsize=100)` in `MemoryConsolidationService` acts as a buffer; add `asyncio.sleep(0.1)` between consolidation calls if rate-limited |
| **GCP credentials in container** — service account JSON key file mounted as a volume is a security risk | High | High | For production on GKE: use Workload Identity Federation instead of key files (see `enrichment.md` Task 1); Helm chart supports both methods |
| **Memory loss on crash** — in-memory `MemoryConsolidationService` queue loses items if pod crashes | Low | Low | Acceptable (same risk as .NET's `System.Threading.Channels.Channel`); critical data is in Kubernetes events; add Redis-backed queue if data loss is unacceptable |
| **SignalR clients break** — any existing UI hardcoded to `/agenthub` (SignalR) will stop receiving events | Medium | Low | The new `/events` SSE endpoint uses a standard browser API; update the static HTML page included in the new Brain image |
| **Proto symlink on Windows** — `brain-python/proto/incident.proto` symlink may not work on Windows dev machines | Low | Low | Use `robocopy` or a pre-build copy step on Windows; the Docker build handles it via `COPY` |

---

## 12. Success Criteria & KPIs

The migration is considered complete and successful when all of the following criteria are met:

### 12.1 Functional Criteria (Pass/Fail)

- [ ] The Go Observer connects to the Python Brain gRPC server without any code changes
- [ ] A simulated pod failure in a `kind` cluster results in a GitHub PR being created by the Python Brain within 60 seconds
- [ ] Duplicate incidents (same `incident_id` within 5 minutes) are correctly deduplicated
- [ ] The Polycheck safety gate blocks a test PR containing `kubectl delete deployment`
- [ ] Historical context from Qdrant is included in the LLM goal for a known-similar incident
- [ ] The `/events` SSE endpoint streams agent events to a browser in real time
- [ ] All 4 tools (`get_pod_status`, `analyze_incident`, `is_code_change_safe`, `create_fix_pull_request`) are successfully invoked by the LangGraph agent
- [ ] A Slack notification is sent when a PR is created

### 12.2 Performance Criteria

| Metric | Target | Measurement Method |
|--------|--------|-------------------|
| End-to-end (pod failure → PR created) | < 60s (p90) | `pytest-benchmark` integration test |
| Redis dedup check | < 10ms | Unit benchmark |
| Qdrant enrichment (embed + search) | < 800ms | Unit benchmark |
| gRPC ingestion overhead | < 50ms | gRPC span duration |
| Brain pod memory usage | < 512Mi | `kubectl top pod` under load |
| Brain pod CPU usage | < 1 core | `kubectl top pod` under sustained incident load |

### 12.3 Quality Criteria

- [ ] Test coverage > 80% (`pytest --cov`)
- [ ] `ruff check` and `mypy` pass with zero errors
- [ ] Docker image passes `trivy image` with no critical CVEs
- [ ] All existing `docs/` PRDs remain valid (the migration does not change the Observer, external services, or the overall SOP)

---

*End of PRD: KM-MIGRATE-01*

---

## .NET Perspective

For engineers familiar with the existing .NET Brain, the table below maps every key Python/LangGraph pattern introduced in this PRD to its direct .NET/Semantic Kernel equivalent. Use this when reading unfamiliar Python code during the migration.

| Python / LangGraph Pattern | .NET / Semantic Kernel Equivalent | Notes |
|---------------------------|----------------------------------|-------|
| `@tool` decorated function (`langchain_core.tools`) | `[KernelFunction]` + `[Description]` attribute on a C# method | Both register a callable that the LLM can invoke by name; LangGraph uses Python type hints for parameter descriptions, SK uses `[Description]` attributes |
| `StateGraph(IncidentGraphState)` + `builder.compile()` | `Kernel` + `FunctionChoiceBehavior.Auto()` | The compiled LangGraph is the equivalent of a configured `Kernel` with auto-invocation enabled; LangGraph makes the execution graph explicit, SK keeps it implicit inside the LLM loop |
| `TypedDict` state (`IncidentGraphState`) | No direct equivalent in SK — state was passed implicitly via the prompt string and plugin method parameters | LangGraph's typed state is a first-class concept; in SK the "state" was the concatenated goal string passed to `InvokePromptAsync` |
| `ToolNode` (LangGraph prebuilt) | `FunctionChoiceBehavior.Auto()` on `PromptExecutionSettings` | Both wrap the tool-call / tool-result loop; `ToolNode` is an explicit graph node, SK's auto-invocation is a setting on the kernel execution |
| Conditional edge (`add_conditional_edges`) | No direct equivalent — branching in SK was embedded in the LLM's natural language SOP prompt | LangGraph makes branching deterministic and code-controlled; the Polycheck YES/NO gate was previously enforced by prompt wording alone |
| `asyncio.Queue` + background `asyncio.create_task()` | `System.Threading.Channels.Channel<T>` + `BackgroundService` (`IHostedService`) | Both implement the write-behind pattern for memory consolidation; `asyncio.Queue` ↔ `Channel<IncidentResolution>`, `create_task(run())` ↔ `AddHostedService<MemoryConsolidationService>()` |
| `redis.asyncio` (`SET NX EX`) | `StackExchange.Redis` `StringSetAsync(key, value, expiry, When.NotExists)` | Identical Redis semantics; only the client library differs |
| `AsyncQdrantClient.search()` | `IVectorStoreRecordCollection<Guid, IncidentMemory>.SearchAsync()` | Both perform cosine-similarity vector search; the SK connector wraps Qdrant's gRPC API, the Python client calls it directly |
| `VertexAIEmbeddings.aembed_query()` | `IEmbeddingGenerator<string, Embedding<float>>` via `SkEmbeddingGeneratorAdapter` + `AddVertexAIEmbeddingGeneration()` | Both call Vertex AI `text-embedding-004`; Python uses `langchain-google-vertexai`, .NET used SK's Google connector with a manual `bearerTokenProvider` callback |
| `ChatVertexAI` (LangChain) | `IChatCompletionService` registered via `AddGoogleAIGeminiChatCompletion()` / `AddAiService()` | Both drive Gemini 2.5-pro; `ChatVertexAI.invoke()` ↔ `kernel.InvokePromptAsync()` |
| `FastAPI` + `@app.get("/healthz")` | `app.MapGet("/healthz", ...)` (ASP.NET Core minimal API) | Structural equivalents; both call the LLM to verify connectivity |
| `sse-starlette` SSE endpoint (`/events`) | ASP.NET Core SignalR Hub (`AgentHub`, `/agenthub`) | Both push real-time agent events to browser clients; SSE is unidirectional (server→client), SignalR is bidirectional; SSE requires no special browser library |
| `pydantic-settings` (`BaseSettings`) | `IConfiguration` + `appsettings.json` + `dotnet user-secrets` | Both load config from environment variables and files; `BaseSettings` auto-coerces types and raises `ValidationError` on startup for missing required values, equivalent to using `GetRequiredSection()` with `ValidateOnStart()` |
| `structlog` (JSON renderer) | `Serilog` + `CompactJsonFormatter` | Both produce structured JSON logs; `structlog.contextvars.bind_contextvars(incident_id=...)` ↔ Serilog's `LogContext.PushProperty("IncidentId", ...)` |
| `opentelemetry-sdk` + `FastAPIInstrumentor` | `OpenTelemetry.*` NuGet packages + `AddAspNetCoreInstrumentation()` | Direct equivalents; both export OTLP traces |
| `PyGithub` (`Github(token).get_repo(...).create_pull(...)`) | `Octokit.GitHubClient` + `IGitHubService` | Same GitHub REST API operations; `PyGithub` is synchronous so calls are wrapped in `run_in_executor` to avoid blocking the asyncio event loop — equivalent to `.NET`'s naturally async `Octokit` |
| `httpx.AsyncClient.post(webhook_url, json=...)` | `HttpClient.PostAsJsonAsync(webhookUrl, payload)` | Both POST JSON to the Slack incoming webhook; `httpx` is the Python async equivalent of .NET's `HttpClient` |
