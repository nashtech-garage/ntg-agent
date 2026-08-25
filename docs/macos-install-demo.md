# Demo: One-Command Local Install on macOS

> Prerequisites for the presenter: macOS with Homebrew installed, .NET 10 SDK,
> Docker (Desktop or Colima), Node >= 20, and an Azure OpenAI resource (endpoint
> + API key) for Demo 3. The repo must be on branch `feature/auto-install-script`.

This guide covers three demo scenarios that together prove macOS is now a
first-class platform for the installer:

| Demo | What it shows | Docker needed? | Modifies system? |
|------|---------------|----------------|-------------------|
| 1 | Docker check-and-hint (user-managed Docker) | No (must be OFF) | No — exits before Phase 2 |
| 2 | Homebrew auto-install routing for non-Docker prereqs | Yes (must be ON) | Only `brew install` (no-op if already installed) |
| 3 | Full end-to-end happy path | Yes (must be ON) | Yes — writes .env, user-secrets, starts LightRAG, launches AppHost |

---

## Demo 1: Docker check-and-hint (Docker not running)

**What it proves:** On macOS, the script detects the platform, checks all
prerequisites, and when Docker is not running it prints macOS-specific guidance
instead of trying to auto-install Docker (which is a user-managed prerequisite).

### Steps

1. Make sure Docker is **not running** (quit Docker Desktop or `colima stop`).

2. Run the installer:

   ```bash
   cd /path/to/ntg-agent
   ./install-local.sh
   ```

3. **Expected output:**

   ```
   Missing prerequisites:
     - docker daemon access
         Start the daemon (Linux: sudo systemctl enable --now docker, then sudo
         usermod -aG docker $USER and re-login; macOS: start Docker Desktop or
         run 'colima start'; WSL: enable your distro under Docker Desktop >
         Settings > Resources > WSL integration)
   Some prerequisites cannot be auto-installed on macOS.
   Start your Docker daemon first:
     Docker Desktop, or: brew install colima docker docker-compose && colima start
   Then re-run ./install-local.sh
   ```

   Exit code: `1`

### Talking points

- The script detected `IS_MAC=true` and `HAVE_BREW=true`.
- All other prerequisites (.NET 10, Node, openssl, git, curl) passed.
- Docker's daemon check (`docker info`) failed, so it landed in the `MISSING`
  array with no corresponding `BREW_PKGS` entry (Docker has an empty brew spec —
  it is deliberately not auto-installed on macOS).
- Since `BREW_PKGS` count (0) < `MISSING` count (1), the script knew something
  couldn't be auto-fixed and printed the macOS Docker hint, then exited.
- **Contrast with Linux:** on Ubuntu the same missing daemon would trigger
  `sudo apt install docker.io`, `systemctl enable --now docker`, `usermod -aG
  docker`, and a `sg docker -c` re-exec — fully automated. On macOS we chose to
  leave Docker to the user.

---

## Demo 2: Homebrew auto-install routing (simulate a missing prerequisite)

**What it proves:** When a non-Docker prerequisite is missing and Homebrew is
present, the script routes to `brew install` automatically — casks for .NET SDK,
formulae for node/git — then re-execs itself to re-verify.

### Setup

1. Start Docker (so only the hidden prerequisite is missing):

   ```bash
   # Docker Desktop:
   open -a Docker
   # wait ~20s for the whale icon to become steady, then verify:
   docker info >/dev/null 2>&1 && echo "Docker is up"

   # OR Colima:
   colima start
   docker info >/dev/null 2>&1 && echo "Docker is up"
   ```

2. Simulate a missing .NET 10 SDK by hiding it from PATH (dotnet lives in
   `/usr/local/share/dotnet`, which is not in the restricted PATH below):

   ```bash
   PATH="/usr/bin:/bin:/usr/sbin:/sbin:/usr/local/bin:/opt/homebrew/bin" \
     ./install-local.sh
   ```

3. **Expected output (key lines):**

   ```
   Missing prerequisites:
     - .NET 10 SDK
         Ubuntu/WSL: sudo apt install dotnet-sdk-10.0 | macOS: brew install --cask
         dotnet-sdk | Arch: ... | https://dotnet.microsoft.com/...
   ==> Installing missing prerequisites with Homebrew (cask installs may prompt
       for your password).
   ==> Prerequisites installed; re-checking.
   ```

   Then `brew install --cask dotnet-sdk` runs. Since the cask is already
   installed on this machine, Homebrew prints `Warning: Cask 'dotnet-sdk' is
   already installed.` and exits 0. The script re-execs itself, but dotnet is
   still hidden from PATH, so the second pass (with `NTG_PREREQS_INSTALLED=1`)
   prints "Install the above and re-run ./install-local.sh" and exits 1.

   > **This second-pass exit is expected in this artificial scenario.** In a real
   > case where .NET 10 SDK is genuinely not installed, the cask install would
   > add it to PATH and the re-exec would find it and proceed.

### Talking points

- The `require()` function's 5th argument (`cask:dotnet-sdk`) told the script
  this prerequisite is Homebrew-fixable on macOS.
- `BREW_PKGS` count (1) == `MISSING` count (1) → all missing items are
  brew-fixable → the script proceeds to `brew install`.
- The script separates casks (`brew install --cask`) from formulae
  (`brew install`) automatically via the `cask:` prefix.
- The `NTG_PREREQS_INSTALLED` guard prevents infinite re-exec loops.

### To also demo the "Homebrew missing" path (optional)

```bash
# Temporarily hide Homebrew from PATH, with Docker running and dotnet hidden:
PATH="/usr/bin:/bin:/usr/sbin:/sbin:/usr/local/bin" ./install-local.sh
```

Expected: `error: Homebrew is required on macOS. Install it:` + the Homebrew
install one-liner + `See https://brew.sh`, then exit 1.

---

## Demo 3: Full end-to-end happy path

**What it proves:** With all prerequisites present and Docker running, the
installer completes every phase on macOS — from prerequisite check through to a
running, chat-ready AppHost — identical to the Linux experience.

### Setup

1. Make sure Docker is running:

   ```bash
   docker info >/dev/null 2>&1 && echo "Docker is up" || echo "Start Docker first"
   ```

2. Make sure all prerequisites are visible (no PATH tricks):

   ```bash
   command -v dotnet && dotnet --list-sdks | grep '^10\.'
   command -v node && node -v
   docker info >/dev/null 2>&1 && echo "Docker daemon OK"
   ```

3. Have your Azure OpenAI endpoint and API key ready (you'll be prompted).

4. Run the installer:

   ```bash
   ./install-local.sh
   ```

### What happens, phase by phase

| Phase | What you'll see | Duration |
|-------|-----------------|----------|
| 1 — Prerequisites | `All system prerequisites present.` | instant |
| 2 — Repo tooling | `Activating repo git hooks`, `Installing dotnet-ef` (if missing), `Ensuring ASP.NET Core HTTPS dev certificate` (may prompt for keychain password) | ~5-15s |
| 3 — .env | `Creating .env from .env.example`, then prompts: `Azure OpenAI endpoint (...)` and `Azure OpenAI API key` (hidden input). Auto-generates `SA_PASSWORD`, `LIGHTRAG_PG_PASSWORD`, `LIGHTRAG_API_KEY`. | interactive |
| 4 — User-secrets | `Writing AppHost user-secrets from .env.` | ~2s |
| 5 — LightRAG | `Starting the local LightRAG stack (first build compiles Apache AGE — can take several minutes).` Builds the Postgres image with pgvector + AGE, then waits for health checks on Postgres (port 5432) and the nginx gateway (port 8080). | first run: 5-10 min; subsequent: ~30s |
| 6 — Launch | `Setup complete. Launching the Aspire AppHost...` then `exec ./start-local-lightrag.sh` starts `dotnet run --project NTG.Agent.AppHost`. | AppHost startup: ~30-60s |

5. Once the AppHost is up, open the Aspire dashboard:

   ```
   https://localhost:17050
   ```

6. Log into the webclient (default admin: `admin@ntgagent.com` / `Ntg@123`) and
   send a chat message. The Default Agent's provider (Azure OpenAI, `gpt-5.1`) is
   seeded automatically on first startup — no Admin UI configuration needed.

### Re-running is safe

If you already have `.env` and user-secrets from a previous run, re-running
`./install-local.sh` keeps existing values and only fills in missing pieces.
The LightRAG containers are brought up with `docker compose up -d` (idempotent).

---

## Quick reference: the three platform paths

| | Linux (Ubuntu/Debian) | macOS | Windows (WSL2) |
|---|---|---|---|
| **Package manager** | `sudo apt-get` | `brew` (must be pre-installed) | n/a (apt inside WSL) |
| **.NET 10 SDK** | `apt install dotnet-sdk-10.0` | `brew install --cask dotnet-sdk` | `apt install dotnet-sdk-10.0` |
| **Node >= 20** | NodeSource `setup_22.x` | `brew install node` | NodeSource `setup_22.x` |
| **Docker** | Auto-installed + daemon started + group added | **User-managed** (check + hint) | **User-managed** (Docker Desktop) |
| **Re-exec after install** | `sg docker -c` (docker group) | plain `exec` (no group concept) | n/a (Docker from Desktop) |
