#!/bin/bash
set -euo pipefail

BASE_URL="__BASE_URL__"
INSTALL_DIR="${HOME}/.local/bin"
BIN_NAME="transfer"

mkdir -p "$INSTALL_DIR"
curl -fsSL "$BASE_URL/transfer.sh" > "$INSTALL_DIR/$BIN_NAME"
chmod +x "$INSTALL_DIR/$BIN_NAME"

echo "Installed $BIN_NAME to $INSTALL_DIR/$BIN_NAME"

if ! echo "$PATH" | grep -q "$INSTALL_DIR"; then
  echo "Add $INSTALL_DIR to your PATH:"
  echo "  export PATH=\"$INSTALL_DIR:\$PATH\""
fi
