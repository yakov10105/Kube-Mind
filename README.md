# Kube-Mind Autonomous SRE Platform

This repository contains the source code for the Kube-Mind platform, an AI-driven system designed to function as an autonomous Level 1 Site Reliability Engineer for Kubernetes.

## Overview

The platform consists of two main components:

1.  **Observer (`/observer`):** A Go-based Kubernetes controller that detects workload failures, harvests diagnostic data, and streams it securely.
2.  **Brain (`/brain`):** A .NET-based AI orchestration service that receives data from the Observer, diagnoses the root cause, and proposes version-controlled fixes via GitOps (Pull Requests).

For a detailed overview of the project's vision, architecture, and roadmap, please see the [System Overview PRD](docs/system-overview-prd.md).

## Project Structure

-   `/proto`: Shared gRPC/Protobuf definitions.
-   `/observer`: The Go "Observer" service.
-   `/brain`: The .NET "Brain" service.
-   `/deploy`: Helm charts and deployment configurations.
-   `/scripts`: Utility and code-generation scripts.
-   `/docs`: Product Requirements Documents (PRDs).

## Getting Started

*Prerequisites: Go, .NET SDK, Docker, Helm, Kubectl (with a configured cluster like `kind` or Docker Desktop).*

1.  **Generate gRPC assets:**
    ```bash
    ./scripts/generate-proto.sh
    ```

2.  **Run the Observer (Go service):**
    ```bash
    cd observer
    # Further instructions to come...
    ```

3.  **Run the Brain (.NET service):**
    ```bash
    cd brain
    dotnet run --project src/KubeMind.Brain.Api
    ```

This `README.md` is a placeholder. Please update it with more detailed setup and usage instructions.
