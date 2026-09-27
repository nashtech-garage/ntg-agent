#!/usr/bin/env bash
# Remove the local LightRAG stack and the local Aspire SQL Server data.
# Runtime .env files are removed; Docker images and .env.example files are intentionally preserved.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

LIGHTRAG_COMPOSE_DIR="$REPO_ROOT/deploy/lightrag-local"
LIGHTRAG_COMPOSE_FILE="$LIGHTRAG_COMPOSE_DIR/docker-compose.yml"
LIGHTRAG_VOLUME="lightrag-local_lightrag-postgres-data"
SQLSERVER_VOLUME="ntg-agent-local-dev-sqlserver-data"
LIGHTRAG_NETWORK="ntg-agent-lightrag"
ENV_FILES=(
  "$REPO_ROOT/.env"
  "$LIGHTRAG_COMPOSE_DIR/.env"
)

info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }

require_docker() {
  if ! command -v docker >/dev/null 2>&1; then
    echo "error: docker CLI is required." >&2
    exit 1
  fi
  if ! docker info >/dev/null 2>&1; then
    echo "error: Docker daemon is not running or is inaccessible." >&2
    exit 1
  fi
}

remove_container() {
  local container_name="$1"
  if docker container inspect "$container_name" >/dev/null 2>&1; then
    info "Removing container $container_name."
    docker rm -f "$container_name" >/dev/null
  fi
}

confirm_uninstall() {
  local answer
  if [[ ! -t 0 ]]; then
    echo "error: uninstall confirmation requires an interactive terminal." >&2
    exit 1
  fi

  while true; do
    read -r -p "This will clear all local LightRAG, agent, SQL Server data, volumes, and .env files. Continue? (Y/N): " answer
    case "$(printf '%s' "$answer" | tr '[:upper:]' '[:lower:]')" in
      y | yes) return ;;
      n | no) echo "Uninstallation cancelled."; exit 0 ;;
      *) echo "Please enter Y for Yes or N for No." >&2 ;;
    esac
  done
}

confirm_uninstall
require_docker

info "Removing the local LightRAG Compose stack."
# Compose down also removes the project network and named LightRAG volume when
# the Compose env file is available. The explicit cleanup below keeps this
# uninstall reliable even after deploy/lightrag-local/.env was deleted.
if [[ -f "$LIGHTRAG_COMPOSE_FILE" ]]; then
  docker compose -f "$LIGHTRAG_COMPOSE_FILE" down --volumes --remove-orphans >/dev/null 2>&1 || true
fi
remove_container lightrag-postgres
remove_container lightrag-gateway

# The Orchestrator creates one persistent container per agent outside Compose.
# Their stable prefix is defined by LightRagContainerManager.ContainerName.
mapfile -t agent_containers < <(
  docker ps -aq --filter "name=lightrag-agent-" 2>/dev/null || true
)
for container_id in "${agent_containers[@]}"; do
  [[ -n "$container_id" ]] && docker rm -f "$container_id" >/dev/null
done

# Aspire does not give the SQL resource a stable container_name. Find only
# containers attached to this repository's explicitly named SQL data volume.
mapfile -t sql_containers < <(
  docker ps -aq --filter "volume=$SQLSERVER_VOLUME" 2>/dev/null || true
)
for container_id in "${sql_containers[@]}"; do
  [[ -n "$container_id" ]] && docker rm -f "$container_id" >/dev/null
done

for volume_name in "$LIGHTRAG_VOLUME" "$SQLSERVER_VOLUME"; do
  if docker volume inspect "$volume_name" >/dev/null 2>&1; then
    info "Removing volume $volume_name."
    docker volume rm "$volume_name" >/dev/null
  fi
done

if docker network inspect "$LIGHTRAG_NETWORK" >/dev/null 2>&1; then
  info "Removing network $LIGHTRAG_NETWORK."
  docker network rm "$LIGHTRAG_NETWORK" >/dev/null || true
fi

for env_file in "${ENV_FILES[@]}"; do
  if [[ -f "$env_file" ]]; then
    info "Removing $env_file."
    rm -f "$env_file"
  fi
done

info "Local LightRAG containers and requested data volumes have been removed."
info "Docker images and .env.example files were preserved."
