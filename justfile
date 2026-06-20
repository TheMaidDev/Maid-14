#!/usr/bin/env -S just --justfile

default: docker-up-all

# Pull images for all services, skipping services that have no build context.
docker-pull:
    docker compose  pull --ignore-buildable

# Start all services.
docker-up-all:
    docker compose up -d

# Start one service in detached mode.
docker-up image='postgres':
    docker compose up -d {{ image }}

# Stop all running containers in the current Docker Compose project without removing containers, networks, or volumes.
docker-stop:
    docker compose stop

# Stop and remove containers, networks, default resources, and optionally volumes created by the current Docker Compose project.
docker-down remove_volumes='false':
    #!/usr/bin/env bash
    if [ "{{ remove_volumes }}" = "true" ]; then \
        docker compose down --volumes; \
    else \
        docker compose down; \
    fi

# Open an interactive shell inside a running service container.
docker-interact image='postgres':
    docker compose exec '{{ image }}' bash

# Stream logs from all services and follow output
docker-logs:
    docker compose logs -f

# Attach to a running service container without signal proxying.
docker-attach image='postgres':
    -docker compose attach --sig-proxy=false '{{ image }}' sh

# Show a one-time snapshot of resource usage statistics for containers in the current Docker Compose project.
docker-stats:
    docker compose stats --no-stream

# Remove dangling Docker images that are no longer referenced by any tag.
docker-remove-unused-images:
    #!/usr/bin/env bash
    set -euo pipefail
    docker images -f "dangling=true" -q | xargs -r docker rmi

# Generate a cryptographically secure random alphanumeric token of length `N`. Uses `openssl rand` as the entropy source, encodes as Base64, removes padding and non-alphanumeric output, then retries until the result is exactly `N` characters using only `[A-Za-z0-9]`.
generate-token length='32':
    #!/usr/bin/env bash
    set -euo pipefail
    while true; do
    s=$(openssl rand -base64 "$(({{ length }} + 3))" | tr -d '\n')
    s="${s%%=*}"   # remove all trailing '='
    s="${s:0:{{ length }}}"   # cut back to requested length
    # allow only base64 alnum
    if [[ "$s" =~ ^[A-Za-z0-9]+$ && ${#s} -eq {{ length }} ]]; then
        echo "$s"
        break
    fi
    done
