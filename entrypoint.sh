#!/bin/sh
set -e

# Fix ownership of /data volume (may be mounted from host with wrong owner)
chown transfercs:transfercs /data

# Drop to non-root and run the app
exec su-exec transfercs ./TransferCs.Api
