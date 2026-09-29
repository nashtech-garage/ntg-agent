#!/usr/bin/env bash
# One-shot local setup: verify system prerequisites, wire repo tooling, collect
# secrets into .env, populate AppHost user-secrets, bring up the local LightRAG
# stack (deploy/lightrag-local), then launch the Aspire AppHost.
#
# System tools (dotnet, docker, node, ...) are checked first. On Ubuntu/Debian
# the missing ones are installed with sudo apt (Node via NodeSource); on macOS
# they are installed with Homebrew (brew must be present; Docker must already be
# running — not auto-installed). Elsewhere (Arch, Docker on WSL2) the script
# prints what is missing and how to get it, then exits. Everything repo-local is
# automatic and idempotent; re-running is safe.
#
# Usage: ./install-local.sh
#   (or, from nothing: curl -fsSL https://raw.githubusercontent.com/nashtech-garage/ntg-agent/main/install.sh | bash)
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

ENV_FILE="$REPO_ROOT/.env"
LIGHTRAG_COMPOSE_DIR="$REPO_ROOT/deploy/lightrag-local"

info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33mwarning:\033[0m %s\n' "$*" >&2; }

# --- Phase 1: system prerequisites (detect; apt-install on Ubuntu/Debian) -----

IS_WSL=false; grep -qi microsoft /proc/version 2>/dev/null && IS_WSL=true
HAVE_APT=false; command -v apt-get >/dev/null 2>&1 && HAVE_APT=true
IS_MAC=false; [[ "$(uname -s)" == "Darwin" ]] && IS_MAC=true
HAVE_BREW=false; command -v brew >/dev/null 2>&1 && HAVE_BREW=true

# sed -i portability: BSD sed (macOS) requires an explicit (possibly empty)
# backup-extension argument, GNU sed (Linux) does not.
SED_INPLACE=(-i)
$IS_MAC && SED_INPLACE=(-i '')

MISSING=()        # "label|hint" for every failed check
APT_PKGS=()       # what apt can install for us
BREW_PKGS=()      # what brew can install for us on macOS
NEED_NODE=false   # NodeSource (apt's nodejs is too old for Next.js 16)
NEED_DOCKER=false # docker.io + daemon + group (Linux only; macOS/WSL: user-managed)
require() {
  # $1 = label, $2 = check command (eval'd), $3 = install hint,
  # $4 = how apt systems fix it: package list, "node", "docker", or "" (manual only)
  # $5 = how brew fixes it on macOS: "cask:NAME", "NAME", or "" (manual only)
  eval "$2" >/dev/null 2>&1 && return
  MISSING+=("$1|$3")
  case "$4" in
    "") ;;
    node) NEED_NODE=true ;;
    docker) $IS_WSL || $IS_MAC || NEED_DOCKER=true ;;
    *) read -ra pkgs <<<"$4"; APT_PKGS+=("${pkgs[@]}") ;;
  esac
  if $IS_MAC && [[ -n "${5:-}" ]]; then
    BREW_PKGS+=("$5")
  fi
}

require ".NET 10 SDK" \
  "dotnet --list-sdks | grep -q '^10\.'" \
  "Ubuntu/WSL: sudo apt install dotnet-sdk-10.0 | macOS: brew install --cask dotnet-sdk | Arch: sudo pacman -S dotnet-sdk | https://dotnet.microsoft.com/download/dotnet/10.0" \
  "dotnet-sdk-10.0" \
  "cask:dotnet-sdk"
require "docker CLI" \
  "command -v docker" \
  "Ubuntu: sudo apt install docker.io | macOS: brew install docker (or install Docker Desktop / Colima) | Arch: sudo pacman -S docker | WSL: install Docker Desktop on Windows with WSL2 backend | https://docs.docker.com/engine/install/" \
  "docker" \
  ""
require "docker daemon access" \
  "docker info" \
  "Start the daemon (Linux: sudo systemctl enable --now docker, then sudo usermod -aG docker \$USER and re-login; macOS: start Docker Desktop or run 'colima start'; WSL: enable your distro under Docker Desktop > Settings > Resources > WSL integration)" \
  "docker" \
  ""
require "docker compose plugin" \
  "docker compose version" \
  "Ubuntu: sudo apt install docker-compose-v2 | macOS: bundled with Docker Desktop, or: brew install docker-compose | Arch: sudo pacman -S docker-compose | bundled with Docker Desktop | https://docs.docker.com/compose/install/" \
  "docker" \
  ""
require "node >= 20" \
  "node -e 'process.exit(parseInt(process.versions.node) >= 20 ? 0 : 1)'" \
  "Ubuntu: https://nodejs.org (LTS — apt's nodejs is often too old) | macOS: brew install node | Arch: sudo pacman -S nodejs npm" \
  "node" \
  "node"
require "openssl" \
  "command -v openssl" \
  "Ubuntu: sudo apt install openssl | macOS: ships with macOS (Command Line Tools) | Arch: sudo pacman -S openssl" \
  "openssl" \
  ""
require "git" \
  "command -v git" \
  "Ubuntu: sudo apt install git | macOS: xcode-select --install, or: brew install git | Arch: sudo pacman -S git" \
  "git" \
  "git"
require "curl" \
  "command -v curl" \
  "Ubuntu: sudo apt install curl | macOS: ships with macOS | Arch: sudo pacman -S curl" \
  "curl ca-certificates" \
  ""

print_missing() {
  echo "Missing prerequisites:" >&2
  for entry in "${MISSING[@]}"; do
    printf '  - %s\n      %s\n' "${entry%%|*}" "${entry#*|}" >&2
  done
}

if (( ${#MISSING[@]} > 0 )); then
  print_missing

  # Second pass (NTG_PREREQS_INSTALLED set by the re-exec below) that still
  # finds something missing means we couldn't fix it — stop instead of looping.
  if [[ -n "${NTG_PREREQS_INSTALLED:-}" ]]; then
    echo "Install the above and re-run ./install-local.sh" >&2
    exit 1
  fi

  # --- macOS / Homebrew path ---------------------------------------------
  if $IS_MAC; then
    if ! $HAVE_BREW; then
      echo "error: Homebrew is required on macOS. Install it:" >&2
      echo "  /bin/bash -c \"\$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)\"" >&2
      echo "  See https://brew.sh — then re-run ./install-local.sh" >&2
      exit 1
    fi
    # If fewer brew-fixable packages than missing items, something can't be
    # auto-fixed (e.g. Docker daemon not running) — hints + exit.
    if (( ${#BREW_PKGS[@]} < ${#MISSING[@]} )); then
      echo "Some prerequisites cannot be auto-installed on macOS." >&2
      echo "Start your Docker daemon first:" >&2
      echo "  Docker Desktop, or: brew install colima docker docker-compose && colima start" >&2
      echo "Then re-run ./install-local.sh" >&2
      exit 1
    fi
    info "Installing missing prerequisites with Homebrew (cask installs may prompt for your password)."
    CASKS=(); FORMULAE=()
    for spec in "${BREW_PKGS[@]}"; do
      if [[ "$spec" == cask:* ]]; then CASKS+=("${spec#cask:}")
      else FORMULAE+=("$spec"); fi
    done
    (( ${#CASKS[@]} > 0 )) && brew install --cask "${CASKS[@]}"
    (( ${#FORMULAE[@]} > 0 )) && brew install "${FORMULAE[@]}"
    export NTG_PREREQS_INSTALLED=1
    info "Prerequisites installed; re-checking."
    exec "$REPO_ROOT/install-local.sh"
  fi

  # --- Linux / apt path --------------------------------------------------
  if ! $HAVE_APT || { (( ${#APT_PKGS[@]} == 0 )) && ! $NEED_NODE && ! $NEED_DOCKER; }; then
    echo "Install the above and re-run ./install-local.sh" >&2
    exit 1
  fi

  info "Installing missing prerequisites with sudo apt-get (you may be asked for your password)."
  sudo -v || { echo "error: sudo is required to install prerequisites. Install them manually and re-run." >&2; exit 1; }
  $NEED_DOCKER && APT_PKGS+=(docker.io docker-compose-v2)
  $NEED_NODE && APT_PKGS+=(curl ca-certificates)
  sudo apt-get update -qq
  sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq "${APT_PKGS[@]}"
  if $NEED_NODE; then
    info "Installing Node 22 LTS from NodeSource."
    curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
    sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq nodejs
  fi
  if $NEED_DOCKER; then
    sudo systemctl enable --now docker
    sudo usermod -aG docker "$(id -un)"
  fi

  # Re-run the checks from the top. `sg docker` gives this process the docker
  # group membership that would otherwise need a logout/login.
  export NTG_PREREQS_INSTALLED=1
  info "Prerequisites installed; re-checking."
  if $NEED_DOCKER; then
    exec sg docker -c "exec $(printf '%q' "$REPO_ROOT/install-local.sh")"
  fi
  exec "$REPO_ROOT/install-local.sh"
fi
info "All system prerequisites present."

# --- Phase 2: repo-local tooling ---------------------------------------------

info "Activating repo git hooks (.githooks)."
git config core.hooksPath .githooks
chmod +x .githooks/commit-msg .githooks/pre-push 2>/dev/null || true

# Global dotnet tools land in ~/.dotnet/tools, which is not on PATH in a fresh
# shell — without this the init-secrets step can't find the dotnet-ef we just installed.
export PATH="$PATH:$HOME/.dotnet/tools"
if ! dotnet ef --version >/dev/null 2>&1; then
  info "Installing dotnet-ef global tool (required by the AppHost migration runners)."
  dotnet tool install --global dotnet-ef
else
  info "dotnet-ef already installed."
fi

info "Ensuring ASP.NET Core HTTPS dev certificate."
dotnet dev-certs https >/dev/null 2>&1 || true
if ! dotnet dev-certs https --trust >/dev/null 2>&1; then
  warn "Could not mark the HTTPS dev cert as trusted (common on Linux)."
  warn "The stack still runs; your browser may warn on https://localhost:17050."
fi

# --- Phase 3: .env (create if missing, fill only empty keys) -----------------

# Print value for KEY from .env (empty output if absent/empty).
env_get() {
  local line val
  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ "$line" =~ ^[[:space:]]*# ]] && continue
    [[ "$line" =~ ^[[:space:]]*$1= ]] || continue
    val="${line#*=}"
    val="${val%%#*}"
    val="${val#"${val%%[![:space:]]*}"}"
    val="${val%"${val##*[![:space:]]}"}"
    printf '%s' "$val"
    return 0
  done < "$ENV_FILE"
}

# Replace KEY=... in .env. Escapes sed-special chars so secrets with \ & | survive.
env_set() {
  local v="$2"
  v="${v//\\/\\\\}"; v="${v//&/\\&}"; v="${v//|/\\|}"
  if grep -q "^[[:space:]]*$1=" "$ENV_FILE"; then
    sed "${SED_INPLACE[@]}" "s|^[[:space:]]*$1=.*|$1=$v|" "$ENV_FILE"
  else
    printf '%s=%s\n' "$1" "$2" >> "$ENV_FILE"
  fi
}

if [[ ! -f "$ENV_FILE" ]]; then
  info "Creating .env from .env.example."
  cp .env.example "$ENV_FILE"
else
  info ".env already exists; filling only missing values."
fi

prompt_value() {
  # $1 = key, $2 = prompt text, $3 = "secret" to hide input
  local val
  while [[ -z "$(env_get "$1")" ]]; do
    if [[ ! -t 0 ]]; then
      echo "error: $1 is empty in .env and no TTY to prompt. Set it in .env and re-run." >&2
      exit 1
    fi
    if [[ "${3:-}" == secret ]]; then
      read -r -s -p "$2: " val; echo
    else
      read -r -p "$2: " val
    fi
    [[ -n "$val" ]] && env_set "$1" "$val"
  done
}

prompt_default() {
  local key="$1" prompt="$2" default="$3" val
  val="$(env_get "$key")"
  if [[ -z "$val" && -t 0 ]]; then read -r -p "$prompt [$default]: " val; fi
  env_set "$key" "${val:-$default}"
}

prompt_optional() {
  local key="$1" prompt="$2" val
  val="$(env_get "$key")"
  if [[ -z "$val" && -t 0 ]]; then
    if [[ "${3:-}" == secret ]]; then read -r -s -p "$prompt: " val; echo; else read -r -p "$prompt: " val; fi
  fi
  [[ -n "$val" ]] && env_set "$key" "$val"
}

prompt_binding() {
  local key="$1" prompt="$2" binding
  binding="$(env_get "$key")"
  if [[ -z "$binding" && -t 0 ]]; then read -r -p "$prompt [openai]: " binding; fi
  binding="${binding:-openai}"; binding="${binding,,}"
  case "$binding" in openai|azure_openai|ollama|gemini|bedrock|lollms) ;; *) echo "error: unsupported LightRAG provider '$binding'." >&2; exit 1 ;; esac
  env_set "$key" "$binding"
}

prompt_binding LIGHTRAG_LLM_BINDING "Global LLM provider (openai, azure_openai, ollama, gemini, bedrock, lollms)"
prompt_binding LIGHTRAG_EMBEDDING_BINDING "Embedding provider (openai, azure_openai, ollama, gemini, bedrock, lollms)"
[[ "$(env_get LIGHTRAG_EMBEDDING_BINDING)" != gemini ]] || { echo "error: LightRAG does not support gemini as EMBEDDING_BINDING." >&2; exit 1; }

configure_provider() {
  local prefix="$1" binding="$2" role="$3" endpoint_default model_default api_key
  case "$binding" in
    openai) endpoint_default=https://api.openai.com/v1; [[ "$role" == llm ]] && model_default=gpt-5.1 || model_default=text-embedding-3-large ;;
    azure_openai) endpoint_default=https://resource.openai.azure.com/; [[ "$role" == llm ]] && model_default=gpt-5.1 || model_default=text-embedding-3-large ;;
    ollama) endpoint_default=http://localhost:11434; [[ "$role" == llm ]] && model_default=qwen3.5:9b || model_default=nomic-embed-text ;;
    gemini) endpoint_default=DEFAULT_GEMINI_ENDPOINT; model_default=gemini-flash-latest ;;
    bedrock) endpoint_default=DEFAULT_BEDROCK_ENDPOINT; [[ "$role" == llm ]] && model_default=us.amazon.nova-lite-v1:0 || model_default=amazon.titan-embed-text-v2:0 ;;
    lollms) endpoint_default=http://localhost:9600; [[ "$role" == llm ]] && model_default=gpt-4 || model_default=all-MiniLM-L6-v2 ;;
  esac
  prompt_default "${prefix}_ENDPOINT" "$role provider endpoint" "$endpoint_default"
  prompt_default "${prefix}_MODEL" "$role model/deployment" "$model_default"
  api_key="${prefix}_API_KEY"
  if [[ "$binding" == openai || "$binding" == azure_openai || "$binding" == gemini ]]; then prompt_value "$api_key" "$role provider API key" secret; fi
}

configure_provider LIGHTRAG_LLM "$(env_get LIGHTRAG_LLM_BINDING)" llm
configure_provider LIGHTRAG_EMBEDDING "$(env_get LIGHTRAG_EMBEDDING_BINDING)" embedding
prompt_default LIGHTRAG_AZURE_API_VERSION "Azure LLM API version" "2024-08-01-preview"
prompt_default LIGHTRAG_AZURE_EMBEDDING_API_VERSION "Azure embedding API version" "2024-08-01-preview"
prompt_default LIGHTRAG_OLLAMA_LLM_NUM_CTX "Ollama LLM context window" "32768"

if [[ "$(env_get LIGHTRAG_LLM_BINDING)" == bedrock || "$(env_get LIGHTRAG_EMBEDDING_BINDING)" == bedrock ]]; then
  prompt_value LIGHTRAG_AWS_REGION "AWS region for Bedrock"
  prompt_optional LIGHTRAG_AWS_BEARER_TOKEN_BEDROCK "AWS Bedrock bearer token (leave empty to use IAM credentials)" secret
  prompt_optional LIGHTRAG_AWS_ACCESS_KEY_ID "AWS access key ID (leave empty to use ambient credentials)"
  prompt_optional LIGHTRAG_AWS_SECRET_ACCESS_KEY "AWS secret access key (leave empty to use ambient credentials)" secret
  prompt_optional LIGHTRAG_AWS_SESSION_TOKEN "AWS session token (optional)" secret
  aws_bearer="$(env_get LIGHTRAG_AWS_BEARER_TOKEN_BEDROCK)"
  aws_access="$(env_get LIGHTRAG_AWS_ACCESS_KEY_ID)"
  aws_secret="$(env_get LIGHTRAG_AWS_SECRET_ACCESS_KEY)"
  if [[ -z "$aws_bearer" && ( -z "$aws_access" || -z "$aws_secret" ) ]]; then
    echo "error: configure an AWS Bedrock bearer token or both access and secret keys." >&2
    exit 1
  fi
fi

gen_if_empty() {
  # $1 = key, $2 = generated value
  if [[ -z "$(env_get "$1")" ]]; then
    env_set "$1" "$2"
    info "Generated $1."
  fi
}

# Aa1! suffix guarantees SQL Server password complexity after base64.
gen_if_empty SA_PASSWORD "$(openssl rand -base64 24 | tr -d '\n\r=/+')Aa1!"
gen_if_empty LIGHTRAG_PG_PASSWORD "$(openssl rand -base64 32 | tr -d '\n\r')"
gen_if_empty LIGHTRAG_API_KEY "$(openssl rand -base64 48 | tr -d '\n\r')"
gen_if_empty LIGHTRAG_WEBUI_GATEWAY_URL "http://localhost:8080"

# --- Phase 4: AppHost user-secrets -------------------------------------------

# < /dev/null forces the init script's non-TTY path: env -> .env -> default,
# no prompts — every needed value is already in .env at this point.
info "Writing AppHost user-secrets from .env."
./scripts/init-apphost-user-secrets.sh < /dev/null

# --- Phase 5: local LightRAG stack (Postgres + nginx gateway) ----------------

LIGHTRAG_ENV="$LIGHTRAG_COMPOSE_DIR/.env"
info "Synchronizing $LIGHTRAG_ENV (POSTGRES_PASSWORD must match lightrag-pg-password)."
printf 'POSTGRES_PASSWORD=%s\n' "$(env_get LIGHTRAG_PG_PASSWORD)" > "$LIGHTRAG_ENV"

info "Starting the local LightRAG stack (first build compiles Apache AGE — can take several minutes)."
docker compose -f "$LIGHTRAG_COMPOSE_DIR/docker-compose.yml" up -d --build

info "Waiting for lightrag-postgres to become healthy."
for i in $(seq 1 90); do
  status="$(docker inspect -f '{{.State.Health.Status}}' lightrag-postgres 2>/dev/null || echo missing)"
  [[ "$status" == "healthy" ]] && break
  if [[ "$status" == "missing" || "$(docker inspect -f '{{.State.Status}}' lightrag-postgres 2>/dev/null)" == "exited" ]]; then
    echo "error: lightrag-postgres is not running. Check: docker compose -f $LIGHTRAG_COMPOSE_DIR/docker-compose.yml logs" >&2
    exit 1
  fi
  sleep 2
done
if [[ "$status" != "healthy" ]]; then
  echo "error: lightrag-postgres did not become healthy in time." >&2
  exit 1
fi

info "Waiting for the nginx gateway on http://localhost:8080/gateway-health."
for i in $(seq 1 30); do
  curl -fs http://localhost:8080/gateway-health >/dev/null 2>&1 && break
  sleep 2
done
if ! curl -fs http://localhost:8080/gateway-health >/dev/null 2>&1; then
  echo "error: gateway health check failed. Check: docker logs lightrag-gateway" >&2
  exit 1
fi
info "LightRAG stack is up (Postgres 127.0.0.1:5432, gateway 127.0.0.1:8080)."

# --- Phase 6: launch ----------------------------------------------------------

cat <<'EOF'

Setup complete. Launching the Aspire AppHost...

  Dashboard:   https://localhost:17050
  Admin login: admin@ntgagent.com / Ntg@123 (seeded)

The Default Agent's provider is configured from the selected global LLM settings
on first startup; change it any time in the Admin UI.

EOF
exec ./start-local-lightrag.sh
