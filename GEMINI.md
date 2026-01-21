# GEMINI Project Guide: Kube-Mind Autonomous SRE Platform

This guide helps the Gemini assistant understand the project structure, key commands, and architectural patterns for the Kube-Mind platform.

## 1. Project Overview

Kube-Mind is an AI-driven, two-part system that acts as an autonomous Level 1 Site Reliability Engineer for Kubernetes. It consists of a Go-based "Observer" for real-time failure detection and a .NET-based "Brain" for AI-driven diagnosis and remediation.

## 2. Architecture Overview

The platform is a monorepo containing two main microservices:

*   **Observer (Go):** A Kubernetes controller built with `controller-runtime`. It has **read-only** access to the cluster to watch for pod failures (`CrashLoopBackOff`, `OOMKilled`). It harvests logs and manifests, redacts sensitive information, and streams a structured `IncidentContext` payload to the Brain via gRPC.

*   **Brain (.NET):** An ASP.NET Core service that serves as the cognitive core. It receives data from the Observer, uses **Microsoft Semantic Kernel** for AI orchestration and planning, and queries a Redis vector database for historical context. Its primary output is a GitOps-native fix, proposed as a Pull Request to a GitHub repository. It **does not** have direct write-access to the Kubernetes cluster.

## 3. Key Directory Map

The project is a monorepo with the following layout:

*   **Shared gRPC Definitions:** `/proto`
    *   Contains the `incident.proto` file defining the `IncidentContext` message shared between the Go and .NET services.

*   **Go Observer Code:** `/observer`
    *   **Controller Logic:** `/observer/internal/controller`
    *   **Main Executable:** `/observer/cmd/`

*   **.NET Brain Code:** `/brain`
    *   **Solution File:** `/brain/KubeMind.Brain.sln`
    *   **Source Code:** `/brain/src/`
    *   **Tests:** `/brain/tests/`

*   **Deployment (IaC):** `/deploy`
    *   **Helm Charts:** `/deploy/helm`

*   **Utility Scripts:** `/scripts`
    *   **Protobuf Compilation:** `/scripts/generate-proto.sh`

## 4. Common Commands

*   **Generate Protobuf Bindings:**
    *   `./scripts/generate-proto.sh`

*   **Go Observer (`/observer` directory):**
    *   **Run Tests:** `go test ./...`
    *   **Run Linter:** `golangci-lint run` (Assuming `golangci-lint` is used)
    *   **Build:** `go build -o ./bin/observer ./cmd/manager`

*   **.NET Brain (`/brain` directory):**
    *   **Run Tests:** `dotnet test`
    *   **Run Locally:** `dotnet run --project src/KubeMind.Brain.Api`

*   **Deployment:**
    *   **Deploy to Kubernetes:** `helm install km-observer ./deploy/helm`
