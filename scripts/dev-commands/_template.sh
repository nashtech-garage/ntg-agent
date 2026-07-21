#!/usr/bin/env bash
# desc: TODO describe this command (this line shows up in `./ntg help`)
#
# This is a drop-in ntg command. The dispatcher discovers it automatically:
# the filename (without .sh) is the command name, and the `# desc:` line above
# is its menu description. Extra args passed to `./ntg <name> ...` arrive as $@.
#
set -euo pipefail

# Repo root, regardless of where ntg is invoked from.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# --- your commands below ---
echo "TODO: implement this command"
