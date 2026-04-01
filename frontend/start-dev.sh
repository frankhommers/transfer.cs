#!/bin/zsh
set -euo pipefail

PORT=3002
DIR="$(cd "$(dirname "$0")" && pwd)"

# Check if vite is already running on this port for this project
already_running() {
  lsof -ti TCP:"$PORT" 2>/dev/null | while read -r pid; do
    cwd=$(lsof -p "$pid" -a -d cwd -Fn 2>/dev/null | grep ^n | cut -c2-)
    if [ "$cwd" = "$DIR" ]; then
      echo "$pid"
      return 0
    fi
  done
  return 1
}

# Test if the dev server responds with HMR support
hmr_alive() {
  curl -sf -o /dev/null --max-time 2 "http://localhost:$PORT/@vite/client" 2>/dev/null
}

pid=$(already_running || true)

if [ -n "$pid" ]; then
  if hmr_alive; then
    echo "Vite dev server already running (pid $pid) with hot-reload on :$PORT"
    echo "Nothing to do."
    exit 0
  fi
  echo "Vite on :$PORT not responding, restarting..."
  kill -9 "$pid" 2>/dev/null || true
  sleep 1
fi

echo "Starting vite dev server on :$PORT..."
cd "$DIR"
exec bun run dev
