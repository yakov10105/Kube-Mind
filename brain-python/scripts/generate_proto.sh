#!/usr/bin/env bash
# Compiles proto/incident.proto into Python gRPC bindings.
# Run from the brain-python/ directory: bash scripts/generate_proto.sh
set -euo pipefail

PROTO_DIR="proto"
OUT_DIR="generated"

mkdir -p "$OUT_DIR"

python3 -m grpc_tools.protoc \
  -I"$PROTO_DIR" \
  --python_out="$OUT_DIR" \
  --grpc_python_out="$OUT_DIR" \
  --pyi_out="$OUT_DIR" \
  "$PROTO_DIR/incident.proto"

# Fix relative imports in generated files (grpcio-tools quirk)
sed -i 's/^import incident_pb2/from generated import incident_pb2/' \
  "$OUT_DIR/incident_pb2_grpc.py" 2>/dev/null || true

echo "Proto generation complete → $OUT_DIR/"
