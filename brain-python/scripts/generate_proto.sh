#!/usr/bin/env bash
# Compiles proto/incident.proto into Python gRPC bindings.
# Run from the brain-python/ directory: bash scripts/generate_proto.sh
set -euo pipefail

PROTO_DIR="proto"
OUT_DIR="generated"

# Resolve Python interpreter cross-platform (Linux/macOS: python3; Windows: py)
PYTHON="${PYTHON:-}"
if [ -z "$PYTHON" ]; then
  if command -v python3 &>/dev/null; then
    PYTHON="python3"
  elif command -v python &>/dev/null; then
    PYTHON="python"
  elif command -v py &>/dev/null; then
    PYTHON="py"
  else
    echo "ERROR: no Python interpreter found. Set PYTHON env var or install Python 3." >&2
    exit 1
  fi
fi

mkdir -p "$OUT_DIR"

"$PYTHON" -m grpc_tools.protoc \
  -I"$PROTO_DIR" \
  --python_out="$OUT_DIR" \
  --grpc_python_out="$OUT_DIR" \
  --pyi_out="$OUT_DIR" \
  "$PROTO_DIR/incident.proto"

# grpcio-tools generates `import incident_pb2` (bare) but our package layout requires
# `from generated import incident_pb2`. Fix it with Python so sed portability
# (GNU sed vs BSD sed -i suffix) is not a concern.
"$PYTHON" - << 'PYEOF'
import pathlib, re
f = pathlib.Path("generated/incident_pb2_grpc.py")
fixed = re.sub(r"(?m)^import incident_pb2\b", "from generated import incident_pb2", f.read_text())
f.write_text(fixed)
PYEOF

echo "Proto generation complete → $OUT_DIR/"
