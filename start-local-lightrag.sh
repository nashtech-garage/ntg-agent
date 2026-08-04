#!/usr/bin/env bash
# Run the full stack with LightRAG on the LOCAL Docker daemon, regardless of the
# remote values stored in user-secrets. Env vars override user-secrets, and the
# code treats whitespace as "disabled" (Aspire re-prompts on truly empty params).
# Requires the local LightRAG stack (deploy/lightrag-local compose: Postgres on
# 127.0.0.1:5432 + nginx gateway on 127.0.0.1:8080) to be running.
set -euo pipefail
cd "$(dirname "$0")"

# exported via `env` because bash identifiers cannot contain hyphens
exec env \
  "Parameters__lightrag-docker-host= " \
  "Parameters__lightrag-gateway-url= " \
  "Parameters__lightrag-server-host= " \
  "Parameters__lightrag-socks-proxy= " \
  "Parameters__lightrag-postgres-port=5432" \
  dotnet run --project NTG.Agent.AppHost --launch-profile https "$@"
