# See here for image contents: https://github.com/microsoft/vscode-dev-containers/tree/v0.245.2/containers/dotnet/.devcontainer/base.Dockerfile

# [Choice] .NET version: 8.0, 7.0, 6.0
ARG VARIANT="8.0"
FROM mcr.microsoft.com/vscode/devcontainers/dotnet:0-${VARIANT}

# Install protobuf compiler
RUN apt-get update && apt-get install -y protobuf-compiler
