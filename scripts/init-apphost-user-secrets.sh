#!/usr/bin/env bash
# Initialize NTG.Agent.AppHost user secrets required for local .NET Aspire runs.
# Keys match README.md and NTG.Agent.AppHost/Program.cs (AddParameter names).
#
# Resolution order (each value, same for TTY and non-TTY):
#   exported env var → prompt (TTY only) → $REPO_ROOT/.env → default (if any).
# Kernel Memory may then be openssl-generated if still empty.
#
# Usage:
#   ./scripts/init-apphost-user-secrets.sh
#   ./scripts/init-apphost-user-secrets.sh --dry-run
#
# Setting the corresponding env var (e.g. GITHUB_TOKEN=xyz ./init-...) skips the prompt.
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
Kernel Memory: if still empty after that, openssl generates a key.

Env/.env keys: GITHUB_TOKEN, KERNEL_MEMORY_API_KEY, KERNEL_MEMORY_API_KEY_2,
GOOGLE_API_KEY, GOOGLE_SEARCH_ENGINE_ID,
LIGHTRAG_PG_PASSWORD, LIGHTRAG_API_KEY,
AZURE_OPENAI_API_KEY, AZURE_EMBEDDING_API_KEY.
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

resolve_field GITHUB_TOKEN \
  "GitHub PAT (models:read) [Enter for .env]: " \
  1 \
  "GITHUB_TOKEN" \
  "GITHUB_TOKEN" \
  "__EMPTY__"

if [[ -z "$GITHUB_TOKEN" ]]; then
  echo "error: GITHUB_TOKEN is required (prompt, .env GITHUB_TOKEN, or export GITHUB_TOKEN)" >&2
  exit 1
fi

# Validate a Kernel Memory access key: length >= 32 and KM's allowed charset (rejects
# '/' and '+' from base64). $1=value, $2=name for messages. (-, ] kept tr-safe by position.)
validate_km_key() {
  local val="$1" name="$2"
  if ((${#val} < 32)); then
    echo "error: $name must be at least 32 characters" >&2
    exit 1
  fi
  if [[ -n "$(printf '%s' "$val" | tr -d 'A-Za-z0-9,.;:_!@#$^*~=|[]{}()-')" ]]; then
    echo "error: $name has characters Kernel Memory rejects (e.g. '/', '+', whitespace)." >&2
    echo "       Allowed: letters, digits, and , . ; : _ - ! @ # \$ ^ * ~ = | [ ] { } ( )" >&2
    exit 1
  fi
}

# KM requires TWO distinct access keys (AccessKey1/AccessKey2) to allow rotation, even
# though the app only ever presents AccessKey1. Generated keys use hex (0-9a-f): base64
# would emit '/' and '+', which KM's validator rejects.
resolve_field KERNEL_MEMORY_API_KEY \
  "Kernel Memory API key 1 (32+ chars) [Enter for .env or auto-generate]: " \
  1 \
  "KERNEL_MEMORY_API_KEY" \
  "KERNEL_MEMORY_API_KEY" \
  "__EMPTY__"

resolve_field KERNEL_MEMORY_API_KEY_2 \
  "Kernel Memory API key 2 / rotation spare (32+ chars) [Enter for .env or auto-generate]: " \
  1 \
  "KERNEL_MEMORY_API_KEY_2" \
  "KERNEL_MEMORY_API_KEY_2" \
  "__EMPTY__"

if [[ -z "$KERNEL_MEMORY_API_KEY" || -z "$KERNEL_MEMORY_API_KEY_2" ]]; then
  if ! command -v openssl >/dev/null 2>&1; then
    echo "error: Kernel Memory key(s) missing; install openssl for auto-generation or set in .env" >&2
    exit 1
  fi
  [[ -z "$KERNEL_MEMORY_API_KEY" ]] && KERNEL_MEMORY_API_KEY="$(openssl rand -hex 32 | tr -d '\n\r')"
  # Regenerate key 2 until it differs from key 1 (KM rejects identical keys).
  while [[ -z "$KERNEL_MEMORY_API_KEY_2" || "$KERNEL_MEMORY_API_KEY_2" == "$KERNEL_MEMORY_API_KEY" ]]; do
    KERNEL_MEMORY_API_KEY_2="$(openssl rand -hex 32 | tr -d '\n\r')"
  done
  echo "Generated Kernel Memory access key(s)."
fi

validate_km_key "$KERNEL_MEMORY_API_KEY" "KERNEL_MEMORY_API_KEY"
validate_km_key "$KERNEL_MEMORY_API_KEY_2" "KERNEL_MEMORY_API_KEY_2"

if [[ "$KERNEL_MEMORY_API_KEY" == "$KERNEL_MEMORY_API_KEY_2" ]]; then
  echo "error: KERNEL_MEMORY_API_KEY and KERNEL_MEMORY_API_KEY_2 must differ (KM requires distinct keys for rotation)." >&2
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

resolve_field AZURE_OPENAI_API_KEY \
  "Azure OpenAI API key (LightRAG LLM, gpt-5.4-mini deployment) [Enter for .env]: " \
  1 \
  "AZURE_OPENAI_API_KEY" \
  "AZURE_OPENAI_API_KEY" \
  "__EMPTY__"

if [[ -z "$AZURE_OPENAI_API_KEY" ]]; then
  echo "error: AZURE_OPENAI_API_KEY is required (prompt, .env AZURE_OPENAI_API_KEY, or export AZURE_OPENAI_API_KEY)" >&2
  exit 1
fi

resolve_field AZURE_EMBEDDING_API_KEY \
  "Azure OpenAI API key (LightRAG embeddings, text-embedding-3-large) [Enter for .env]: " \
  1 \
  "AZURE_EMBEDDING_API_KEY" \
  "AZURE_EMBEDDING_API_KEY" \
  "__EMPTY__"

if [[ -z "$AZURE_EMBEDDING_API_KEY" ]]; then
  echo "error: AZURE_EMBEDDING_API_KEY is required (prompt, .env AZURE_EMBEDDING_API_KEY, or export AZURE_EMBEDDING_API_KEY)" >&2
  exit 1
fi

set_secret "Parameters:github-token" "$GITHUB_TOKEN"
set_secret "Parameters:kernel-memory-api-key" "$KERNEL_MEMORY_API_KEY"
set_secret "Parameters:kernel-memory-api-key-2" "$KERNEL_MEMORY_API_KEY_2"
set_secret "Parameters:google-api-key" "$GOOGLE_API_KEY"
set_secret "Parameters:google-search-engine-id" "$GOOGLE_SEARCH_ENGINE_ID"
set_secret "Parameters:lightrag-pg-password" "$LIGHTRAG_PG_PASSWORD"
set_secret "Parameters:lightrag-api-key" "$LIGHTRAG_API_KEY"
set_secret "Parameters:azure-openai-api-key" "$AZURE_OPENAI_API_KEY"
set_secret "Parameters:azure-embedding-api-key" "$AZURE_EMBEDDING_API_KEY"

if [[ "$DRY_RUN" -eq 1 ]]; then
  echo "Dry run finished; no secrets were written."
else
  echo "Done. Run: dotnet run --project NTG.Agent.AppHost"
fi
