#!/bin/zsh
set -euo pipefail

PORT=5002
DIR="$(cd "$(dirname "$0")" && pwd)"

# Kill existing process on port if any
lsof -ti TCP:"$PORT" | xargs kill -9 2>/dev/null || true
sleep 1

echo "Starting backend with hot-reload (dotnet watch)..."
cd "$DIR/src/TransferCs.Api"
exec dotnet watch run
