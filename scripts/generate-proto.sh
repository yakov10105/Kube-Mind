#!/bin/bash

# This script generates the Go and C# code from the .proto definitions.
#
# Prerequisites:
# 1. protoc (the protobuf compiler) must be installed.
# 2. Go protobuf plugins must be installed:
#    - go install google.golang.org/protobuf/cmd/protoc-gen-go
#    - go install google.golang.org/grpc/cmd/protoc-gen-go-grpc
# 3. .NET gRPC plugins are typically managed via NuGet packages in the .csproj file.
#    This script focuses on the Go generation.

# Set paths
PROTO_DIR="./proto"
OBSERVER_OUT_DIR="./observer/proto" # Assumes Go code will live in a 'proto' pkg
BRAIN_OUT_DIR="./brain/src/KubeMind.Brain.Api/Protos" # Example, adjust as needed

# Create output directories if they don't exist
mkdir -p "$OBSERVER_OUT_DIR"
mkdir -p "$BRAIN_OUT_DIR"

echo "Generating Go code..."
protoc --proto_path="$PROTO_DIR" \
       --go_out="$OBSERVER_OUT_DIR" --go_opt=paths=source_relative \
       --go-grpc_out="$OBSERVER_OUT_DIR" --go-grpc_opt=paths=source_relative \
       "$PROTO_DIR"/incident.proto

echo "Generating C# code..."
# For .NET, it's often better to let the project file handle generation.
# This part is for manual generation or reference.
protoc --proto_path="$PROTO_DIR" \
       --csharp_out="$BRAIN_OUT_DIR" \
       --grpc_out="$BRAIN_OUT_DIR" \
       "$PROTO_DIR"/incident.proto

echo "Protobuf generation complete."

