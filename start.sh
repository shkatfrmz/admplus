#!/bin/bash
set -e
ROOT="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-/usr/share/dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

cd "$ROOT/src/Admplus.Api"
dotnet run --urls http://0.0.0.0:3001 &
BACKEND_PID=$!

cleanup() {
  kill "$BACKEND_PID" 2>/dev/null || true
}
trap cleanup EXIT

cd "$ROOT/frontend"
if [ ! -d node_modules ]; then
  npm install
fi
npm run dev
