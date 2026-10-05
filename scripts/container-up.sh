#!/usr/bin/env bash
# Builds the image and runs the app in a local container for trying it out.
#
#   scripts/container-up.sh
#
# Open http://localhost:8180 afterwards. Use the name "localhost": the sign-in
# cookie is Secure, and browsers only keep it over plain HTTP for localhost.
#
# The data (database and keys) lives in ~/containers/rest-o-matic-ui/data on
# the host and is kept between runs. Running this again rebuilds the image and
# replaces the container, keeping the data.
#
# Settings, through the environment:
#   PORT      host port to publish (default 8180)
#   DATA_DIR  host directory for the data (default ~/containers/rest-o-matic-ui/data)
set -euo pipefail

NAME=rest-o-matic-ui
IMAGE=rest-o-matic-ui:local
PORT="${PORT:-8180}"
DATA_DIR="${DATA_DIR:-$HOME/containers/rest-o-matic-ui/data}"
# The image's non-root user (see the Containerfile).
APP_UID=1654

cd "$(dirname "$0")/.."

# Removed first, so the port it holds is free for the check below.
if podman container exists "$NAME"; then
    echo "Replacing the existing $NAME container (the data is kept)..."
    podman rm -f "$NAME" >/dev/null
fi

# Another container or program on the port would answer instead of this app,
# which looks like this app misbehaving. Podman can even publish the same port
# twice, once on IPv4 and once on IPv6, so check before starting.
if ss -ltnH "sport = :$PORT" | grep -q .; then
    echo "Port $PORT is already in use:" >&2
    ss -ltnpH "sport = :$PORT" >&2 || true
    podman ps --format '{{.Names}} ({{.Image}}) {{.Ports}}' | grep -E ":$PORT->" | sed 's/^/  container /' >&2 || true
    echo "Pick another, for example: PORT=8181 $0" >&2
    exit 1
fi

echo "Building $IMAGE..."
podman build --build-arg VERSION=0.0.0-local -t "$IMAGE" .

mkdir -p "$DATA_DIR"

# keep-id runs the container's app user as you, so the files in $DATA_DIR stay
# owned by your own account and you can read, back up or delete them normally.
echo "Starting $NAME on port $PORT, with data in $DATA_DIR..."
podman run -d --name "$NAME" \
    -p "$PORT:8080" \
    --userns="keep-id:uid=$APP_UID,gid=$APP_UID" \
    -v "$DATA_DIR:/data:Z" \
    "$IMAGE" >/dev/null

echo -n "Waiting for the app to start"
for _ in $(seq 1 60); do
    if curl -sf "http://localhost:$PORT/healthz" >/dev/null; then
        echo " ready."
        break
    fi
    if ! podman container exists "$NAME" || [ "$(podman inspect -f '{{.State.Running}}' "$NAME")" != "true" ]; then
        echo
        echo "The container stopped. Its log:" >&2
        podman logs "$NAME" >&2 || true
        exit 1
    fi
    echo -n "."
    sleep 1
done
if ! curl -sf "http://localhost:$PORT/healthz" >/dev/null; then
    echo
    echo "The app did not become healthy within 60 seconds. See: podman logs $NAME" >&2
    exit 1
fi

# Ask the app whether setup is needed: while no user exists, every page
# leads to /setup. The log is not a reliable answer on its own, because the
# app writes it in the background and the token line can arrive late.
LANDING=$(curl -s -o /dev/null -w '%{redirect_url}' -H 'Accept: text/html' "http://localhost:$PORT/")

echo
if [[ "$LANDING" == */setup ]]; then
    TOKEN=""
    for _ in $(seq 1 15); do
        TOKEN=$(podman logs "$NAME" 2>&1 | grep -o 'first user: [A-Z2-7]\{26\}' | tail -n 1 | cut -d' ' -f3 || true)
        [ -n "$TOKEN" ] && break
        sleep 1
    done
    if [ -n "$TOKEN" ]; then
        echo "No user exists yet. Open http://localhost:$PORT/setup and enter this setup token:"
        echo
        echo "    $TOKEN"
        echo
        echo "It works once, and changes each time the container starts until setup is done."
    else
        echo "No user exists yet, but the setup token was not found in the log. Look for it with:"
        echo "    podman logs $NAME 2>&1 | grep 'setup token'"
    fi
else
    echo "Users already exist. Sign in at http://localhost:$PORT"
    echo "Forgot the password? podman exec $NAME dotnet RestOMatic.Web.dll reset-password <user name>"
fi
echo
echo "Logs: podman logs -f $NAME    Stop: scripts/container-down.sh"
