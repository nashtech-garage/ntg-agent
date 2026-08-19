#!/usr/bin/env bash
# One-shot local setup: verify system prerequisites, wire repo tooling, collect
# secrets into .env, populate AppHost user-secrets, bring up the local LightRAG
# stack (deploy/lightrag-local), then launch the Aspire AppHost.
#
# System tools (dotnet, docker, node, ...) are checked first. On Ubuntu/Debian
# the missing ones are installed with sudo apt (Node via NodeSource); elsewhere
# (Arch, macOS, Docker on WSL2) the script prints what is missing and how to get
# it, then exits. Everything repo-local is automatic and idempotent; re-running
# is safe.
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

MISSING=()        # "label|hint" for every failed check
APT_PKGS=()       # what apt can install for us
NEED_NODE=false   # NodeSource (apt's nodejs is too old for Next.js 16)
NEED_DOCKER=false # docker.io + daemon + group (not on WSL: Docker Desktop owns it there)
require() {
  # $1 = label, $2 = check command (eval'd), $3 = install hint,
  # $4 = how apt systems fix it: package list, "node", "docker", or "" (manual only)
  eval "$2" >/dev/null 2>&1 && return
  MISSING+=("$1|$3")
  case "$4" in
    "") ;;
    node) NEED_NODE=true ;;
    docker) $IS_WSL || NEED_DOCKER=true ;;
    *) read -ra pkgs <<<"$4"; APT_PKGS+=("${pkgs[@]}") ;;
  esac
}

require ".NET 10 SDK" \
  "dotnet --list-sdks | grep -q '^10\.'" \
  "Ubuntu/WSL: sudo apt install dotnet-sdk-10.0 | Arch: sudo pacman -S dotnet-sdk | https://dotnet.microsoft.com/download/dotnet/10.0" \
  "dotnet-sdk-10.0"
require "docker CLI" \
  "command -v docker" \
  "Ubuntu: sudo apt install docker.io | Arch: sudo pacman -S docker | WSL: install Docker Desktop on Windows with WSL2 backend | https://docs.docker.com/engine/install/" \
  "docker"
require "docker daemon access" \
  "docker info" \
  "Start the daemon (sudo systemctl enable --now docker) and add yourself to the docker group (sudo usermod -aG docker \$USER, then re-login). WSL: enable your distro under Docker Desktop > Settings > Resources > WSL integration" \
  "docker"
require "docker compose plugin" \
  "docker compose version" \
  "Ubuntu: sudo apt install docker-compose-v2 | Arch: sudo pacman -S docker-compose | bundled with Docker Desktop | https://docs.docker.com/compose/install/" \
  "docker"
require "node >= 20" \
  "node -e 'process.exit(parseInt(process.versions.node) >= 20 ? 0 : 1)'" \
  "Ubuntu: https://nodejs.org (LTS — apt's nodejs is often too old) | Arch: sudo pacman -S nodejs npm" \
  "node"
require "openssl" \
  "command -v openssl" \
  "Ubuntu: sudo apt install openssl | Arch: sudo pacman -S openssl" \
  "openssl"
require "git" \
  "command -v git" \
  "Ubuntu: sudo apt install git | Arch: sudo pacman -S git" \
  "git"
require "curl" \
  "command -v curl" \
  "Ubuntu: sudo apt install curl | Arch: sudo pacman -S curl" \
  "curl ca-certificates"

print_missing() {
  echo "Missing prerequisites:" >&2
  for entry in "${MISSING[@]}"; do
    printf '  - %s\n      %s\n' "${entry%%|*}" "${entry#*|}" >&2
  done
}

if (( ${#MISSING[@]} > 0 )); then
  print_missing
  # Second pass (NTG_PREREQS_INSTALLED set by the re-exec below) that still
  # finds something missing means apt couldn't fix it — stop instead of looping.
  if ! $HAVE_APT || [[ -n "${NTG_PREREQS_INSTALLED:-}" ]] \
     || { (( ${#APT_PKGS[@]} == 0 )) && ! $NEED_NODE && ! $NEED_DOCKER; }; then
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
    sed -i "s|^[[:space:]]*$1=.*|$1=$v|" "$ENV_FILE"
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

prompt_secret() {
  # $1 = key, $2 = prompt text
  local val
  while [[ -z "$(env_get "$1")" ]]; do
    if [[ ! -t 0 ]]; then
      echo "error: $1 is empty in .env and no TTY to prompt. Set it in .env and re-run." >&2
      exit 1
    fi
    read -r -s -p "$2: " val
    echo
    [[ -n "$val" ]] && env_set "$1" "$val"
  done
}

prompt_secret GITHUB_TOKEN "GitHub PAT with models:read (https://github.com/settings/tokens)"
prompt_secret LIGHTRAG_EMBEDDING_API_KEY "Azure OpenAI API key (LightRAG LLM + embeddings)"

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

# --- Phase 4: AppHost user-secrets -------------------------------------------

# < /dev/null forces the init script's non-TTY path: env -> .env -> default,
# no prompts — every needed value is already in .env at this point.
info "Writing AppHost user-secrets from .env."
./scripts/init-apphost-user-secrets.sh < /dev/null

# --- Phase 5: local LightRAG stack (Postgres + nginx gateway) ----------------

LIGHTRAG_ENV="$LIGHTRAG_COMPOSE_DIR/.env"
if [[ ! -f "$LIGHTRAG_ENV" ]] || ! grep -q '^POSTGRES_PASSWORD=..*' "$LIGHTRAG_ENV"; then
  info "Writing $LIGHTRAG_ENV (POSTGRES_PASSWORD must match lightrag-pg-password)."
  printf 'POSTGRES_PASSWORD=%s\n' "$(env_get LIGHTRAG_PG_PASSWORD)" > "$LIGHTRAG_ENV"
fi

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

The Default Agent's provider (Azure OpenAI, gpt-5.1, via the LightRAG key) is
configured automatically on first startup; change it any time in the Admin UI.

EOF
exec ./start-local-lightrag.sh
