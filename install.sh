#!/usr/bin/env bash
# Bootstrap from nothing: clone the repo and hand off to ./install-local.sh.
#
#   curl -fsSL https://raw.githubusercontent.com/nashtech-garage/ntg-agent/main/install.sh | bash
#
# Overrides: NTG_BRANCH (default main), NTG_DIR (default ~/ntg-agent),
#            NTG_REPO_URL (default the GitHub repo).
set -euo pipefail

REPO_URL="${NTG_REPO_URL:-https://github.com/nashtech-garage/ntg-agent.git}"
BRANCH="${NTG_BRANCH:-main}"
DEST="${NTG_DIR:-$HOME/ntg-agent}"

info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }

if ! command -v git >/dev/null 2>&1; then
  if command -v apt-get >/dev/null 2>&1; then
    info "Installing git with sudo apt-get."
    sudo apt-get update -qq
    sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq git
  else
    echo "error: git is required. Install it and re-run." >&2
    exit 1
  fi
fi

if [[ -d "$DEST/.git" ]]; then
  info "Using existing checkout at $DEST."
else
  info "Cloning $REPO_URL ($BRANCH) into $DEST."
  git clone --branch "$BRANCH" "$REPO_URL" "$DEST"
fi
cd "$DEST"

# Piped through bash, stdin is the script itself — reattach the terminal so
# install-local.sh can prompt for secrets. Headless runs keep the non-TTY path.
if (exec </dev/tty) 2>/dev/null; then
  exec ./install-local.sh </dev/tty
fi
exec ./install-local.sh
