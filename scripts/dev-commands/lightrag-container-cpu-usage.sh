#!/usr/bin/env bash
# desc: Display CPU usage of lighrag containers

set -euo pipefail

# Repo root, regardless of where ntg is invoked from.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# --- your commands below ---
usage=$(docker stats --no-stream $(docker ps --format '{{.Names}}' | grep lightrag))

if [ -z "$usage" ]; then
	echo "No lightrag container found"
	exit 0
fi

for i in "${!usage[@]}"; do
	echo "${usage[$i]}"
done
