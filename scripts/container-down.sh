#!/usr/bin/env bash
# Stops and removes the local container started by scripts/container-up.sh.
#
#   scripts/container-down.sh
#
# The data directory (~/containers/rest-o-matic-ui/data, or DATA_DIR) is never
# touched, so the next container-up.sh starts with the same users and settings.
# To start from an empty install, delete that directory yourself.
set -euo pipefail

NAME=rest-o-matic-ui
DATA_DIR="${DATA_DIR:-$HOME/containers/rest-o-matic-ui/data}"

if podman container exists "$NAME"; then
    # Where this container's data actually is, which DATA_DIR may not say.
    DATA_DIR=$(podman inspect -f '{{range .Mounts}}{{if eq .Destination "/data"}}{{.Source}}{{end}}{{end}}' "$NAME")
    podman rm -f "$NAME" >/dev/null
    echo "Removed the $NAME container."
else
    echo "No $NAME container is running."
fi
echo "The data in $DATA_DIR was kept."
