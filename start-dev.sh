#!/bin/zsh
set -euo pipefail

BACKEND_PORT=5002
FRONTEND_PORT=3002
DIR="$(cd "$(dirname "$0")" && pwd)"

# Kill existing processes on both ports
lsof -ti TCP:"$BACKEND_PORT" | xargs kill -9 2>/dev/null || true
lsof -ti TCP:"$FRONTEND_PORT" | xargs kill -9 2>/dev/null || true
sleep 1

# Start frontend in background
echo "Starting frontend on :$FRONTEND_PORT..."
cd "$DIR/frontend"
bun run dev &
FRONTEND_PID=$!

# Start backend in foreground (Ctrl+C stops everything)
echo "Starting backend on :$BACKEND_PORT..."
cd "$DIR/backend/src/TransferCs.Api"

trap "kill $FRONTEND_PID 2>/dev/null; exit" INT TERM
dotnet watch run
