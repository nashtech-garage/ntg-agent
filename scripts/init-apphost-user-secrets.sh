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
APPHOST_PROJ="$REPO_ROOT/src/NTG.Agent.AppHost/NTG.Agent.AppHost.csproj"
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
  echo "Ensure Docker is running and your user has permission (for Linux/WSL: add user to docker group, then re-login; macOS: start Docker Desktop or run 'colima start')." >&2
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
LIGHTRAG_IMAGE_TAG,
LIGHTRAG_LLM_BINDING, LIGHTRAG_LLM_ENDPOINT, LIGHTRAG_LLM_API_KEY, LIGHTRAG_LLM_MODEL,
LIGHTRAG_EMBEDDING_BINDING, LIGHTRAG_EMBEDDING_ENDPOINT, LIGHTRAG_EMBEDDING_API_KEY,
LIGHTRAG_EMBEDDING_MODEL, LIGHTRAG_AZURE_API_VERSION, LIGHTRAG_AZURE_EMBEDDING_API_VERSION,
LIGHTRAG_AWS_REGION, LIGHTRAG_AWS_BEARER_TOKEN_BEDROCK, LIGHTRAG_AWS_ACCESS_KEY_ID,
LIGHTRAG_AWS_SECRET_ACCESS_KEY, LIGHTRAG_AWS_SESSION_TOKEN, LIGHTRAG_OLLAMA_LLM_NUM_CTX,
LIGHTRAG_DOCKER_HOST, LIGHTRAG_DOCKER_CERT_PATH, LIGHTRAG_DOCKER_CERT_PASSWORD,
LIGHTRAG_SERVER_HOST, LIGHTRAG_GATEWAY_URL, LIGHTRAG_WEBUI_GATEWAY_URL,
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

remove_secret() {
  local key="$1"
  if [[ "$DRY_RUN" -eq 1 ]]; then
    echo "[dry-run] would remove $key"
    return 0
  fi
  dotnet user-secrets remove "$key" --project "$APPHOST_PROJ" >/dev/null 2>&1 || true
  echo "removed $key"
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
  "LightRAG API key (optional; Enter to disable API-key authentication): " \
  1 \
  "LIGHTRAG_API_KEY" \
  "LIGHTRAG_API_KEY" \
  "__EMPTY__"

# The image tag is configuration-only: keep installation non-interactive for this value.
LIGHTRAG_IMAGE_TAG="${LIGHTRAG_IMAGE_TAG:-}"
if [[ -z "$LIGHTRAG_IMAGE_TAG" ]]; then
  LIGHTRAG_IMAGE_TAG="$(read_dotenv_value LIGHTRAG_IMAGE_TAG "$ENV_FILE" 2>/dev/null || true)"
fi
LIGHTRAG_IMAGE_TAG="${LIGHTRAG_IMAGE_TAG:-v1.4.16}"

# LightRAG lets the global LLM and embedding provider be selected independently.
# Initialize optional provider values because this script runs with `set -u` and
# non-selected providers do not pass through resolve_field below.
LIGHTRAG_AWS_REGION=""
LIGHTRAG_AWS_BEARER_TOKEN_BEDROCK=""
LIGHTRAG_AWS_ACCESS_KEY_ID=""
LIGHTRAG_AWS_SECRET_ACCESS_KEY=""
LIGHTRAG_AWS_SESSION_TOKEN=""
LIGHTRAG_OLLAMA_LLM_NUM_CTX=""

choose_binding() {
  local key="$1" prompt="$2" current selected
  current="${!key:-}"
  if [[ -z "$current" ]]; then
    current="$(read_dotenv_value "$key" "$ENV_FILE" 2>/dev/null || true)"
  fi
  if [[ -t 0 ]]; then
    read -r -p "$prompt [${current:-openai}]: " selected || true
    selected="${selected:-${current:-openai}}"
  else
    selected="${current:-openai}"
  fi
  selected="${selected,,}"
  case "$selected" in
    openai|azure_openai|ollama|gemini|bedrock|lollms) ;;
    *) echo "error: $key must be one of openai, ollama, lollms, azure_openai, bedrock, or gemini." >&2; exit 1 ;;
  esac
  printf -v "$key" '%s' "$selected"
}

provider_value() {
  local binding="$1" role="$2" key model_default endpoint_default
  if [[ "$role" == llm ]]; then key=LIGHTRAG_LLM_API_KEY; model_default=gpt-5.1; endpoint_default=https://api.openai.com/v1
  else key=LIGHTRAG_EMBEDDING_API_KEY; model_default=text-embedding-3-large; endpoint_default=https://api.openai.com/v1; fi
  case "$binding" in
    azure_openai) endpoint_default=https://resource.openai.azure.com/ ;;
    ollama) endpoint_default=http://localhost:11434; [[ "$role" == llm ]] && model_default=qwen3.5:9b || model_default=nomic-embed-text ;;
    gemini) endpoint_default=DEFAULT_GEMINI_ENDPOINT; [[ "$role" == llm ]] && model_default=gemini-flash-latest || model_default=gemini-embedding-001 ;;
    bedrock) endpoint_default=DEFAULT_BEDROCK_ENDPOINT; [[ "$role" == llm ]] && model_default=us.amazon.nova-lite-v1:0 || model_default=amazon.titan-embed-text-v2:0 ;;
    lollms) endpoint_default=http://localhost:9600 ;;
  esac
  local endpoint_key model_key
  [[ "$role" == llm ]] && endpoint_key=LIGHTRAG_LLM_ENDPOINT || endpoint_key=LIGHTRAG_EMBEDDING_ENDPOINT
  [[ "$role" == llm ]] && model_key=LIGHTRAG_LLM_MODEL || model_key=LIGHTRAG_EMBEDDING_MODEL
  resolve_field "$endpoint_key" "$role provider endpoint [Enter for $endpoint_default]: " 0 "$endpoint_key" "$endpoint_key" "$endpoint_default"
  resolve_field "$model_key" "$role model/deployment [Enter for $model_default]: " 0 "$model_key" "$model_key" "$model_default"
  if [[ "$binding" == openai || "$binding" == azure_openai || "$binding" == gemini ]]; then
    resolve_field "$key" "$role provider API key: " 1 "$key" "$key" "__EMPTY__"
    [[ -n "${!key}" ]] || { echo "error: $key is required for $binding." >&2; exit 1; }
  else
    resolve_field "$key" "$role provider API key [Enter to use ambient credentials]: " 1 "$key" "$key" "__EMPTY__"
  fi
}

choose_binding LIGHTRAG_LLM_BINDING "Global LLM provider (openai, ollama, lollms, azure_openai, bedrock, gemini); default: openai"
provider_value "$LIGHTRAG_LLM_BINDING" llm

choose_binding LIGHTRAG_EMBEDDING_BINDING "Embedding provider (openai, ollama, lollms, azure_openai, bedrock); default: openai"
if [[ "$LIGHTRAG_EMBEDDING_BINDING" == gemini ]]; then
  echo "error: the current LightRAG EMBEDDING_BINDING contract does not support gemini; choose openai, azure_openai, ollama, bedrock, or lollms for embeddings." >&2
  exit 1
fi
provider_value "$LIGHTRAG_EMBEDDING_BINDING" embedding

# Azure API versions are fixed defaults from .env.example, not interactive settings.
LIGHTRAG_AZURE_API_VERSION="${LIGHTRAG_AZURE_API_VERSION:-}"
if [[ -z "$LIGHTRAG_AZURE_API_VERSION" ]]; then
  LIGHTRAG_AZURE_API_VERSION="$(read_dotenv_value LIGHTRAG_AZURE_API_VERSION "$ENV_FILE" 2>/dev/null || true)"
fi
LIGHTRAG_AZURE_API_VERSION="${LIGHTRAG_AZURE_API_VERSION:-2024-08-01-preview}"

LIGHTRAG_AZURE_EMBEDDING_API_VERSION="${LIGHTRAG_AZURE_EMBEDDING_API_VERSION:-}"
if [[ -z "$LIGHTRAG_AZURE_EMBEDDING_API_VERSION" ]]; then
  LIGHTRAG_AZURE_EMBEDDING_API_VERSION="$(read_dotenv_value LIGHTRAG_AZURE_EMBEDDING_API_VERSION "$ENV_FILE" 2>/dev/null || true)"
fi
LIGHTRAG_AZURE_EMBEDDING_API_VERSION="${LIGHTRAG_AZURE_EMBEDDING_API_VERSION:-2024-08-01-preview}"
resolve_field LIGHTRAG_OLLAMA_LLM_NUM_CTX "Ollama LLM context window [Enter for 32768]: " 0 "LIGHTRAG_OLLAMA_LLM_NUM_CTX" "LIGHTRAG_OLLAMA_LLM_NUM_CTX" "32768"

if [[ "$LIGHTRAG_LLM_BINDING" == bedrock || "$LIGHTRAG_EMBEDDING_BINDING" == bedrock ]]; then
  resolve_field LIGHTRAG_AWS_REGION "AWS region for Bedrock: " 0 "LIGHTRAG_AWS_REGION" "LIGHTRAG_AWS_REGION" "__EMPTY__"
  [[ -n "$LIGHTRAG_AWS_REGION" ]] || { echo "error: LIGHTRAG_AWS_REGION is required for bedrock." >&2; exit 1; }
  resolve_field LIGHTRAG_AWS_BEARER_TOKEN_BEDROCK "AWS Bedrock bearer token [Enter to use IAM credentials]: " 1 "LIGHTRAG_AWS_BEARER_TOKEN_BEDROCK" "LIGHTRAG_AWS_BEARER_TOKEN_BEDROCK" "__EMPTY__"
  resolve_field LIGHTRAG_AWS_ACCESS_KEY_ID "AWS access key ID [Enter to use ambient credentials]: " 0 "LIGHTRAG_AWS_ACCESS_KEY_ID" "LIGHTRAG_AWS_ACCESS_KEY_ID" "__EMPTY__"
  resolve_field LIGHTRAG_AWS_SECRET_ACCESS_KEY "AWS secret access key [Enter to use ambient credentials]: " 1 "LIGHTRAG_AWS_SECRET_ACCESS_KEY" "LIGHTRAG_AWS_SECRET_ACCESS_KEY" "__EMPTY__"
  resolve_field LIGHTRAG_AWS_SESSION_TOKEN "AWS session token [Enter if not using temporary credentials]: " 1 "LIGHTRAG_AWS_SESSION_TOKEN" "LIGHTRAG_AWS_SESSION_TOKEN" "__EMPTY__"
  if [[ -z "$LIGHTRAG_AWS_BEARER_TOKEN_BEDROCK" && ( -z "$LIGHTRAG_AWS_ACCESS_KEY_ID" || -z "$LIGHTRAG_AWS_SECRET_ACCESS_KEY" ) ]]; then
    echo "error: configure an AWS Bedrock bearer token or both access and secret keys." >&2; exit 1
  fi
fi

# The image tag is configuration-only: keep installation non-interactive for this value.
LIGHTRAG_IMAGE_TAG="${LIGHTRAG_IMAGE_TAG:-}"
if [[ -z "$LIGHTRAG_IMAGE_TAG" ]]; then
  LIGHTRAG_IMAGE_TAG="$(read_dotenv_value LIGHTRAG_IMAGE_TAG "$ENV_FILE" 2>/dev/null || true)"
fi
LIGHTRAG_IMAGE_TAG="${LIGHTRAG_IMAGE_TAG:-v1.4.16}"

# LightRAG lets the global LLM and embedding provider be selected independently.
# Initialize optional provider values because this script runs with `set -u` and
# non-selected providers do not pass through resolve_field below.
LIGHTRAG_AWS_REGION=""
LIGHTRAG_AWS_BEARER_TOKEN_BEDROCK=""
LIGHTRAG_AWS_ACCESS_KEY_ID=""
LIGHTRAG_AWS_SECRET_ACCESS_KEY=""
LIGHTRAG_AWS_SESSION_TOKEN=""
LIGHTRAG_OLLAMA_LLM_NUM_CTX=""

choose_binding() {
  local key="$1" prompt="$2" current selected
  current="${!key:-}"
  if [[ -z "$current" ]]; then
    current="$(read_dotenv_value "$key" "$ENV_FILE" 2>/dev/null || true)"
  fi
  if [[ -t 0 ]]; then
    read -r -p "$prompt [${current:-openai}]: " selected || true
    selected="${selected:-${current:-openai}}"
  else
    selected="${current:-openai}"
  fi
  selected="${selected,,}"
  case "$selected" in
    openai|azure_openai|ollama|gemini|bedrock|lollms) ;;
    *) echo "error: $key must be one of openai, ollama, lollms, azure_openai, bedrock, or gemini." >&2; exit 1 ;;
  esac
  printf -v "$key" '%s' "$selected"
}

choose_binding LIGHTRAG_LLM_BINDING "Global LLM provider (openai, ollama, lollms, azure_openai, bedrock, gemini); default: openai"
choose_binding LIGHTRAG_EMBEDDING_BINDING "Embedding provider (openai, ollama, lollms, azure_openai, bedrock); default: openai"
if [[ "$LIGHTRAG_EMBEDDING_BINDING" == gemini ]]; then
  echo "error: the current LightRAG EMBEDDING_BINDING contract does not support gemini; choose openai, azure_openai, ollama, bedrock, or lollms for embeddings." >&2
  exit 1
fi

provider_value() {
  local binding="$1" role="$2" key model_default endpoint_default
  if [[ "$role" == llm ]]; then key=LIGHTRAG_LLM_API_KEY; model_default=gpt-5.1; endpoint_default=https://api.openai.com/v1
  else key=LIGHTRAG_EMBEDDING_API_KEY; model_default=text-embedding-3-large; endpoint_default=https://api.openai.com/v1; fi
  case "$binding" in
    azure_openai) endpoint_default=https://resource.openai.azure.com/ ;;
    ollama) endpoint_default=http://localhost:11434; [[ "$role" == llm ]] && model_default=qwen3.5:9b || model_default=nomic-embed-text ;;
    gemini) endpoint_default=DEFAULT_GEMINI_ENDPOINT; [[ "$role" == llm ]] && model_default=gemini-flash-latest || model_default=gemini-embedding-001 ;;
    bedrock) endpoint_default=DEFAULT_BEDROCK_ENDPOINT; [[ "$role" == llm ]] && model_default=us.amazon.nova-lite-v1:0 || model_default=amazon.titan-embed-text-v2:0 ;;
    lollms) endpoint_default=http://localhost:9600 ;;
  esac
  local endpoint_key model_key
  [[ "$role" == llm ]] && endpoint_key=LIGHTRAG_LLM_ENDPOINT || endpoint_key=LIGHTRAG_EMBEDDING_ENDPOINT
  [[ "$role" == llm ]] && model_key=LIGHTRAG_LLM_MODEL || model_key=LIGHTRAG_EMBEDDING_MODEL
  resolve_field "$endpoint_key" "$role provider endpoint [Enter for $endpoint_default]: " 0 "$endpoint_key" "$endpoint_key" "$endpoint_default"
  resolve_field "$model_key" "$role model/deployment [Enter for $model_default]: " 0 "$model_key" "$model_key" "$model_default"
  if [[ "$binding" == openai || "$binding" == azure_openai || "$binding" == gemini ]]; then
    resolve_field "$key" "$role provider API key: " 1 "$key" "$key" "__EMPTY__"
    [[ -n "${!key}" ]] || { echo "error: $key is required for $binding." >&2; exit 1; }
  else
    resolve_field "$key" "$role provider API key [Enter to use ambient credentials]: " 1 "$key" "$key" "__EMPTY__"
  fi
}

provider_value "$LIGHTRAG_LLM_BINDING" llm
provider_value "$LIGHTRAG_EMBEDDING_BINDING" embedding

resolve_field LIGHTRAG_AZURE_API_VERSION "Azure LLM API version [Enter for 2024-08-01-preview]: " 0 "LIGHTRAG_AZURE_API_VERSION" "LIGHTRAG_AZURE_API_VERSION" "2024-08-01-preview"
resolve_field LIGHTRAG_AZURE_EMBEDDING_API_VERSION "Azure embedding API version [Enter for 2024-08-01-preview]: " 0 "LIGHTRAG_AZURE_EMBEDDING_API_VERSION" "LIGHTRAG_AZURE_EMBEDDING_API_VERSION" "2024-08-01-preview"
resolve_field LIGHTRAG_OLLAMA_LLM_NUM_CTX "Ollama LLM context window [Enter for 32768]: " 0 "LIGHTRAG_OLLAMA_LLM_NUM_CTX" "LIGHTRAG_OLLAMA_LLM_NUM_CTX" "32768"

if [[ "$LIGHTRAG_LLM_BINDING" == bedrock || "$LIGHTRAG_EMBEDDING_BINDING" == bedrock ]]; then
  resolve_field LIGHTRAG_AWS_REGION "AWS region for Bedrock: " 0 "LIGHTRAG_AWS_REGION" "LIGHTRAG_AWS_REGION" "__EMPTY__"
  [[ -n "$LIGHTRAG_AWS_REGION" ]] || { echo "error: LIGHTRAG_AWS_REGION is required for bedrock." >&2; exit 1; }
  resolve_field LIGHTRAG_AWS_BEARER_TOKEN_BEDROCK "AWS Bedrock bearer token [Enter to use IAM credentials]: " 1 "LIGHTRAG_AWS_BEARER_TOKEN_BEDROCK" "LIGHTRAG_AWS_BEARER_TOKEN_BEDROCK" "__EMPTY__"
  resolve_field LIGHTRAG_AWS_ACCESS_KEY_ID "AWS access key ID [Enter to use ambient credentials]: " 0 "LIGHTRAG_AWS_ACCESS_KEY_ID" "LIGHTRAG_AWS_ACCESS_KEY_ID" "__EMPTY__"
  resolve_field LIGHTRAG_AWS_SECRET_ACCESS_KEY "AWS secret access key [Enter to use ambient credentials]: " 1 "LIGHTRAG_AWS_SECRET_ACCESS_KEY" "LIGHTRAG_AWS_SECRET_ACCESS_KEY" "__EMPTY__"
  resolve_field LIGHTRAG_AWS_SESSION_TOKEN "AWS session token [Enter if not using temporary credentials]: " 1 "LIGHTRAG_AWS_SESSION_TOKEN" "LIGHTRAG_AWS_SESSION_TOKEN" "__EMPTY__"
  if [[ -z "$LIGHTRAG_AWS_BEARER_TOKEN_BEDROCK" && ( -z "$LIGHTRAG_AWS_ACCESS_KEY_ID" || -z "$LIGHTRAG_AWS_SECRET_ACCESS_KEY" ) ]]; then
    echo "error: configure an AWS Bedrock bearer token or both access and secret keys." >&2; exit 1
  fi
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

resolve_field LIGHTRAG_WEBUI_GATEWAY_URL \
  "LightRAG WebUI gateway base URL (e.g. https://lightrag.example.com) [required for remote; Enter for local default]: " \
  0 \
  "LIGHTRAG_WEBUI_GATEWAY_URL" \
  "LIGHTRAG_WEBUI_GATEWAY_URL" \
  "__EMPTY__"

case "$LIGHTRAG_GATEWAY_URL" in
  ""|http://localhost|http://localhost/*|https://localhost|https://localhost/*|http://127.0.0.1|http://127.0.0.1/*|https://127.0.0.1|https://127.0.0.1/*|http://[::1]|http://[::1]/*|https://[::1]|https://[::1]/*)
    ;;
  *)
    if [[ -z "$LIGHTRAG_WEBUI_GATEWAY_URL" ]]; then
      echo "error: LIGHTRAG_WEBUI_GATEWAY_URL is required when LIGHTRAG_GATEWAY_URL points to a remote gateway." >&2
      echo "Set it to the wildcard WebUI base, for example https://lightrag.example.com." >&2
      exit 1
    fi
    ;;
esac

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
if [[ -n "$LIGHTRAG_API_KEY" ]]; then
  set_secret "Parameters:lightrag-api-key" "$LIGHTRAG_API_KEY"
else
  remove_secret "Parameters:lightrag-api-key"
fi
set_secret "Parameters:lightrag-image-tag" "$LIGHTRAG_IMAGE_TAG"
set_secret "Parameters:lightrag-llm-binding" "$LIGHTRAG_LLM_BINDING"
set_secret "Parameters:lightrag-llm-endpoint" "$LIGHTRAG_LLM_ENDPOINT"
set_secret "Parameters:lightrag-llm-api-key" "$LIGHTRAG_LLM_API_KEY"
set_secret "Parameters:lightrag-embedding-api-key" "$LIGHTRAG_EMBEDDING_API_KEY"
set_secret "Parameters:lightrag-llm-model" "$LIGHTRAG_LLM_MODEL"
set_secret "Parameters:lightrag-embedding-binding" "$LIGHTRAG_EMBEDDING_BINDING"
set_secret "Parameters:lightrag-embedding-endpoint" "$LIGHTRAG_EMBEDDING_ENDPOINT"
set_secret "Parameters:lightrag-embedding-model" "$LIGHTRAG_EMBEDDING_MODEL"
set_secret "Parameters:lightrag-azure-api-version" "$LIGHTRAG_AZURE_API_VERSION"
set_secret "Parameters:lightrag-azure-embedding-api-version" "$LIGHTRAG_AZURE_EMBEDDING_API_VERSION"
set_secret "Parameters:lightrag-aws-region" "$LIGHTRAG_AWS_REGION"
set_secret "Parameters:lightrag-aws-bearer-token-bedrock" "$LIGHTRAG_AWS_BEARER_TOKEN_BEDROCK"
set_secret "Parameters:lightrag-aws-access-key-id" "$LIGHTRAG_AWS_ACCESS_KEY_ID"
set_secret "Parameters:lightrag-aws-secret-access-key" "$LIGHTRAG_AWS_SECRET_ACCESS_KEY"
set_secret "Parameters:lightrag-aws-session-token" "$LIGHTRAG_AWS_SESSION_TOKEN"
set_secret "Parameters:lightrag-ollama-llm-num-ctx" "$LIGHTRAG_OLLAMA_LLM_NUM_CTX"
set_secret "Parameters:lightrag-docker-host" "$LIGHTRAG_DOCKER_HOST"
set_secret "Parameters:lightrag-docker-cert-path" "$LIGHTRAG_DOCKER_CERT_PATH"
set_secret "Parameters:lightrag-docker-cert-password" "$LIGHTRAG_DOCKER_CERT_PASSWORD"
set_secret "Parameters:lightrag-server-host" "$LIGHTRAG_SERVER_HOST"
set_secret "Parameters:lightrag-gateway-url" "$LIGHTRAG_GATEWAY_URL"
set_secret "Parameters:lightrag-webui-gateway-url" "$LIGHTRAG_WEBUI_GATEWAY_URL"
set_secret "Parameters:lightrag-postgres-port" "$LIGHTRAG_POSTGRES_PORT"

if [[ "$DRY_RUN" -eq 1 ]]; then
  echo "Dry run finished; no secrets were written."
else
  echo "Done. Run: dotnet run --project src/NTG.Agent.AppHost"
fi
