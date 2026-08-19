#!/usr/bin/env bash
# Initialize NTG.Agent.AppHost user secrets required for local .NET Aspire runs.
# Keys match README.md and NTG.Agent.AppHost/Program.cs (AddParameter names).
#
# Resolution order (each value, same for TTY and non-TTY):
#   exported env var → prompt (TTY only) → $REPO_ROOT/.env → default (if any).
#
# Usage:
#   ./scripts/init-apphost-user-secrets.sh
#   ./scripts/init-apphost-user-secrets.sh --dry-run
#
# Setting the corresponding env var (e.g. SA_PASSWORD=xyz ./init-...) skips the prompt.
# Without a TTY (e.g. CI): prompts are skipped; each value uses env, then .env, then defaults.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
APPHOST_PROJ="$REPO_ROOT/NTG.Agent.AppHost/NTG.Agent.AppHost.csproj"
ENV_FILE="$REPO_ROOT/.env"

if [[ ! -f "$APPHOST_PROJ" ]]; then
  echo "error: AppHost project not found at $APPHOST_PROJ" >&2
  exit 1
fi

check_command() {
  local cmd="$1"
  local install_hint="$2"
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "error: $cmd is not on PATH. $install_hint" >&2
    exit 1
  fi
}

check_command "dotnet" "Install the .NET SDK and retry."
check_command "docker" "Install Docker and ensure the CLI is available."

if ! dotnet ef --version >/dev/null 2>&1; then
  echo "error: dotnet-ef is not available." >&2
  echo "Install it with: dotnet tool install --global dotnet-ef" >&2
  exit 1
fi

if ! docker info >/dev/null 2>&1; then
  echo "error: cannot access Docker daemon (permission denied or daemon not running)." >&2
  echo "Ensure Docker is running and your user has permission (for Linux/WSL: add user to docker group, then re-login)." >&2
  exit 1
fi

usage() {
  cat <<'EOF'
Usage: init-apphost-user-secrets.sh [-n|--dry-run] [-h|--help]

Sets NTG.Agent.AppHost user secrets. Per value:
  exported env var → prompt (TTY only) → $REPO_ROOT/.env → default.

Env/.env keys: SA_PASSWORD,
GOOGLE_API_KEY, GOOGLE_SEARCH_ENGINE_ID,
LIGHTRAG_PG_PASSWORD, LIGHTRAG_API_KEY,
LIGHTRAG_EMBEDDING_API_KEY,
LIGHTRAG_DOCKER_HOST, LIGHTRAG_DOCKER_CERT_PATH, LIGHTRAG_DOCKER_CERT_PASSWORD,
LIGHTRAG_SERVER_HOST, LIGHTRAG_GATEWAY_URL,
LIGHTRAG_POSTGRES_PORT.

Leave the LIGHTRAG_DOCKER_* / LIGHTRAG_SERVER_* / LIGHTRAG_GATEWAY_* values empty
for a plain all-local run (local Docker socket + the deploy/lightrag-local stack).
EOF
}

DRY_RUN=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    -n|--dry-run)
      DRY_RUN=1
      shift
      ;;
    *)
      echo "error: unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

# Print value for KEY from FILE (first non-empty match). stdout only; exit 1 if missing/empty.
read_dotenv_value() {
  local key="$1"
  local file="$2"
  [[ -f "$file" ]] || return 1
  local line val
  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ -z "${line// }" ]] && continue
    [[ "$line" =~ ^[[:space:]]*# ]] && continue
    [[ "$line" =~ ^[[:space:]]*${key}= ]] || continue
    val="${line#*=}"
    val="${val%%#*}"
    val="${val#"${val%%[![:space:]]*}"}"
    val="${val%"${val##*[![:space:]]}"}"
    if [[ "$val" == \"*\" ]]; then val="${val:1:-1}"
    elif [[ "$val" == \'*\' ]]; then val="${val:1:-1}"; fi
    [[ -n "$val" ]] || return 1
    printf '%s' "$val"
    return 0
  done < "$file"
  return 1
}

# $1 = name of bash variable to set (indirect)
# $2 = prompt (TTY only; skipped if env var is already set)
# $3 = 1 if secret (read -s), else 0
# $4 = space-separated dotenv keys to try (in order)
# $5 = env var name (optional; checked before prompt in both TTY and non-TTY)
# $6 = default literal (optional; __EMPTY__ means none)
#
# Resolution: env var → prompt (TTY only) → .env → default
resolve_field() {
  local __out="$1"
  local __prompt="$2"
  local __secret="$3"
  local __dotenv_keys="$4"
  local __env_name="${5:-}"
  local __default="${6:-__EMPTY__}"

  local __val=""
  local __tty=0
  [[ -t 0 ]] && __tty=1

  if [[ -n "$__env_name" ]]; then
    __val="${!__env_name:-}"
  fi

  if [[ -z "$__val" && "$__tty" -eq 1 ]]; then
    if [[ "$__secret" == 1 ]]; then
      read -r -s -p "$__prompt" __val || true
      echo
    else
      read -r -p "$__prompt" __val || true
    fi
  fi

  if [[ -z "$__val" ]]; then
    local __k
    for __k in $__dotenv_keys; do
      if __val="$(read_dotenv_value "$__k" "$ENV_FILE" 2>/dev/null)"; then
        break
      fi
      __val=""
    done
  fi

  if [[ -z "$__val" && "$__default" != "__EMPTY__" ]]; then
    __val="$__default"
  fi

  printf -v "$__out" '%s' "$__val"
}

mask_value() {
  local v="$1"
  local n="${#v}"
  if (( n <= 8 )); then
    echo "(length $n)"
  else
    echo "${v:0:4}...${v: -4} (length $n)"
  fi
}

set_secret() {
  local key="$1"
  local value="$2"
  if [[ "$DRY_RUN" -eq 1 ]]; then
    echo "[dry-run] would set $key => $(mask_value "$value")"
    return 0
  fi
  dotnet user-secrets set "$key" "$value" --project "$APPHOST_PROJ" >/dev/null
  echo "set $key"
}

resolve_field SA_PASSWORD \
  "SQL Server SA password (complexity rules apply) [Enter for .env]: " \
  1 \
  "SA_PASSWORD" \
  "SA_PASSWORD" \
  "__EMPTY__"

if [[ -z "$SA_PASSWORD" ]]; then
  echo "error: SA_PASSWORD is required (prompt, .env SA_PASSWORD, or export SA_PASSWORD)" >&2
  echo "Without Parameters:sql-sa-password, Aspire silently holds every app at the parameter prompt." >&2
  exit 1
fi

resolve_field GOOGLE_API_KEY \
  "Google API key (MCP) [Enter for .env or default placeholder]: " \
  1 \
  "GOOGLE_API_KEY" \
  "GOOGLE_API_KEY" \
  "placeholder"

resolve_field GOOGLE_SEARCH_ENGINE_ID \
  "Google Search Engine ID [Enter for .env or default placeholder]: " \
  0 \
  "GOOGLE_SEARCH_ENGINE_ID" \
  "GOOGLE_SEARCH_ENGINE_ID" \
  "placeholder"

resolve_field LIGHTRAG_PG_PASSWORD \
  "LightRAG PostgreSQL password [Enter for .env or auto-generate]: " \
  1 \
  "LIGHTRAG_PG_PASSWORD" \
  "LIGHTRAG_PG_PASSWORD" \
  "__EMPTY__"

if [[ -z "$LIGHTRAG_PG_PASSWORD" ]]; then
  if command -v openssl >/dev/null 2>&1; then
    LIGHTRAG_PG_PASSWORD="$(openssl rand -base64 32 | tr -d '\n\r')"
    echo "Generated LIGHTRAG_PG_PASSWORD (${#LIGHTRAG_PG_PASSWORD} characters)."
  else
    echo "error: LIGHTRAG_PG_PASSWORD missing; install openssl for auto-generation or set in .env" >&2
    exit 1
  fi
fi

resolve_field LIGHTRAG_API_KEY \
  "LightRAG API key (32+ chars) [Enter for .env or auto-generate]: " \
  1 \
  "LIGHTRAG_API_KEY" \
  "LIGHTRAG_API_KEY" \
  "__EMPTY__"

if [[ -z "$LIGHTRAG_API_KEY" ]]; then
  if command -v openssl >/dev/null 2>&1; then
    LIGHTRAG_API_KEY="$(openssl rand -base64 48 | tr -d '\n\r')"
    echo "Generated LIGHTRAG_API_KEY (${#LIGHTRAG_API_KEY} characters)."
  else
    echo "error: LIGHTRAG_API_KEY missing; install openssl for auto-generation or set in .env" >&2
    exit 1
  fi
fi

resolve_field LIGHTRAG_EMBEDDING_API_KEY \
  "Azure OpenAI API key (LightRAG LLM + embeddings, hcm resource) [Enter for .env]: " \
  1 \
  "LIGHTRAG_EMBEDDING_API_KEY" \
  "LIGHTRAG_EMBEDDING_API_KEY" \
  "__EMPTY__"

if [[ -z "$LIGHTRAG_EMBEDDING_API_KEY" ]]; then
  echo "error: LIGHTRAG_EMBEDDING_API_KEY is required (prompt, .env LIGHTRAG_EMBEDDING_API_KEY, or export LIGHTRAG_EMBEDDING_API_KEY)" >&2
  exit 1
fi

# --- Remote LightRAG server (TLS) -------------------------------------------------
# All optional: leave every value empty for a plain all-local run against the local
# Docker socket. Set them to drive the dedicated Ubuntu server over TLS instead.

resolve_field LIGHTRAG_DOCKER_HOST \
  "Remote Docker daemon URL (e.g. https://4.193.109.6:2376) [Enter to skip]: " \
  0 \
  "LIGHTRAG_DOCKER_HOST" \
  "LIGHTRAG_DOCKER_HOST" \
  "__EMPTY__"

resolve_field LIGHTRAG_DOCKER_CERT_PATH \
  "Path to the Docker client certificate (client.pfx) [Enter to skip]: " \
  0 \
  "LIGHTRAG_DOCKER_CERT_PATH" \
  "LIGHTRAG_DOCKER_CERT_PATH" \
  "__EMPTY__"

resolve_field LIGHTRAG_DOCKER_CERT_PASSWORD \
  "Password for that client certificate [Enter to skip]: " \
  1 \
  "LIGHTRAG_DOCKER_CERT_PASSWORD" \
  "LIGHTRAG_DOCKER_CERT_PASSWORD" \
  "__EMPTY__"

resolve_field LIGHTRAG_SERVER_HOST \
  "LightRAG server host (e.g. 4.193.109.6) [Enter to skip]: " \
  0 \
  "LIGHTRAG_SERVER_HOST" \
  "LIGHTRAG_SERVER_HOST" \
  "__EMPTY__"

resolve_field LIGHTRAG_GATEWAY_URL \
  "LightRAG gateway URL (e.g. https://4.193.109.6) [Enter for http://localhost:8080]: " \
  0 \
  "LIGHTRAG_GATEWAY_URL" \
  "LIGHTRAG_GATEWAY_URL" \
  "__EMPTY__"

resolve_field LIGHTRAG_POSTGRES_PORT \
  "LightRAG Postgres port [Enter for 5432]: " \
  0 \
  "LIGHTRAG_POSTGRES_PORT" \
  "LIGHTRAG_POSTGRES_PORT" \
  "5432"

# A cert path is useless without the daemon URL and vice versa — fail early rather than
# letting the Orchestrator start and throw on the first container operation.
if [[ -n "$LIGHTRAG_DOCKER_HOST" && -z "$LIGHTRAG_DOCKER_CERT_PATH" ]]; then
  echo "error: LIGHTRAG_DOCKER_HOST is set but LIGHTRAG_DOCKER_CERT_PATH is empty." >&2
  echo "The remote daemon runs with tlsverify and requires a client certificate." >&2
  exit 1
fi

if [[ -n "$LIGHTRAG_DOCKER_CERT_PATH" && ! -f "$LIGHTRAG_DOCKER_CERT_PATH" ]]; then
  echo "error: client certificate not found at '$LIGHTRAG_DOCKER_CERT_PATH'." >&2
  exit 1
fi

set_secret "Parameters:sql-sa-password" "$SA_PASSWORD"
set_secret "Parameters:google-api-key" "$GOOGLE_API_KEY"
set_secret "Parameters:google-search-engine-id" "$GOOGLE_SEARCH_ENGINE_ID"
set_secret "Parameters:lightrag-pg-password" "$LIGHTRAG_PG_PASSWORD"
set_secret "Parameters:lightrag-api-key" "$LIGHTRAG_API_KEY"
set_secret "Parameters:lightrag-embedding-api-key" "$LIGHTRAG_EMBEDDING_API_KEY"
set_secret "Parameters:lightrag-docker-host" "$LIGHTRAG_DOCKER_HOST"
set_secret "Parameters:lightrag-docker-cert-path" "$LIGHTRAG_DOCKER_CERT_PATH"
set_secret "Parameters:lightrag-docker-cert-password" "$LIGHTRAG_DOCKER_CERT_PASSWORD"
set_secret "Parameters:lightrag-server-host" "$LIGHTRAG_SERVER_HOST"
set_secret "Parameters:lightrag-gateway-url" "$LIGHTRAG_GATEWAY_URL"
set_secret "Parameters:lightrag-postgres-port" "$LIGHTRAG_POSTGRES_PORT"

if [[ "$DRY_RUN" -eq 1 ]]; then
  echo "Dry run finished; no secrets were written."
else
  echo "Done. Run: dotnet run --project NTG.Agent.AppHost"
fi
