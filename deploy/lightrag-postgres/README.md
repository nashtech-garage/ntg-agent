# LightRAG Postgres + remote Docker — Azure Ubuntu VM over an SSH tunnel

The LightRAG knowledge stack (the pgvector + Apache AGE Postgres, and the per-agent
`lightrag-agent-*` containers) runs on a dedicated Azure Ubuntu VM. The Orchestrator
stays on the main machine and drives the VM's Docker daemon **over an SSH tunnel** —
no Azure NSG change is needed, because it rides the already-open port 22.

Three channels ride one SSH connection:

| Channel | SSH mechanism | Why |
|---|---|---|
| Docker daemon | `-L 2375:/var/run/docker.sock` | Drive the remote daemon; the socket is never put on TCP |
| Postgres (reset path) | `-L 55432:127.0.0.1:5432` | `ResetVectorSchemaAsync` connects directly |
| Per-agent container ports | `-D 1080` (SOCKS5) | Reach any reserved `lightrag-agent` port (20000–20999) with no per-port setup |

Server: `ntgagent@20.24.151.145` (has sudo; Docker already installed).

---

## 1. Server prep (on the VM, via SSH)

Let the SSH user reach the Docker socket (needed to forward it), then start Postgres:

```bash
sudo usermod -aG docker ntgagent      # then log out/in so the group takes effect

git clone <repo-url> ntg-agent        # the compose build context needs scripts/
cd ntg-agent/deploy/lightrag-postgres
cp .env.example .env                   # set POSTGRES_PASSWORD (= AppHost lightrag-pg-password)
docker compose up -d
```

Postgres binds to the VM's **loopback** (`127.0.0.1:5432`) only — nothing public.

## 2. SSH key auth (recommended for an unattended tunnel)

```bash
# on the Mac
ssh-keygen -t ed25519 -f ~/.ssh/ntg-vm           # if you don't have a key
ssh-copy-id -i ~/.ssh/ntg-vm.pub ntgagent@20.24.151.145
```

## 3. Open the tunnel (from the Mac)

```bash
ssh -i ~/.ssh/ntg-vm \
    -o ExitOnForwardFailure=yes \
    -D 1080 \
    -L 2375:/var/run/docker.sock \
    -L 55432:127.0.0.1:5432 \
    ntgagent@20.24.151.145
```

`-o ExitOnForwardFailure=yes` makes a local-port collision fail loudly instead of
silently pointing the Orchestrator at the wrong service. If `2375`/`55432`/`1080` are
taken on your Mac, pick other local ports and match them in the Orchestrator config
(step 5).

Verify over the tunnel:

```bash
docker -H tcp://localhost:2375 info                               # daemon reachable

# before query the databases, install psql:
brew install libpq
# link the psql:
brew link --force libpq

# try querying:
psql -h localhost -U postgres -d uploaded-documents -c '\dx'      # lists vector + age
```

## 4. Keep the tunnel up

The tunnel is critical infra — run it under **autossh** (or a launchd/systemd unit)
with auto-restart, started before the Orchestrator:

```bash
brew install autossh
autossh -M 0 -f -N \
    -i ~/.ssh/ntg-vm -o ExitOnForwardFailure=yes -o ServerAliveInterval=30 \
    -D 1080 -L 2375:/var/run/docker.sock -L 55432:127.0.0.1:5432 \
    ntgagent@20.24.151.145
```

## 5. Orchestrator config (on the main machine)

Set the AppHost parameters (user-secrets / launch profile):

- `Parameters:lightrag-docker-host` = `tcp://localhost:2375`
- `Parameters:lightrag-socks-proxy` = `socks5://localhost:1080`
- `Parameters:lightrag-pg-password` = the same value as `POSTGRES_PASSWORD` above

The other LightRAG host settings default correctly for the tunnel (`ServerHost=localhost`,
`PortBindHostIp=127.0.0.1`, `PostgresHost`→`ServerHost`, `PostgresPort=55432`), so they
need no override. (Leave the two params empty for a plain all-local dev run.)

## 6. Remove stale agents before starting the NTG system because these agents' lightrag ports may be out-of-range: 20000–20999 (on the main machine)

```bash
# find the SQL Server container name first:
docker ps --filter "name=sqlserver" --format "{{.Names}}"

# drop it (remember to paste the container name into <sqlserver-container>):
docker exec <sqlserver-container> /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P 'Admin123_Strong!' -C \
    -Q "IF DB_ID('NTGAgent') IS NOT NULL BEGIN ALTER DATABASE [NTGAgent] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [NTGAgent]; PRINT 'NTGAgent 
  dropped'; END ELSE PRINT 'NTGAgent did not exist';"

```

## 7. Start the NTG-Agent as usual (on the main machine)

On first run the Orchestrator pulls `ghcr.io/hkuds/lightrag` on the remote daemon and
spawns `lightrag-agent-*` containers there, each on its reserved port (20000–20999),
reached through the SOCKS proxy.

---

## Quick checklist

- [ ] `ntgagent` in the `docker` group (re-logged in)
- [ ] `docker compose up -d`; `\dx` shows `vector` + `age`
- [ ] tunnel up; `docker -H tcp://localhost:2375 info` works from the Mac
- [ ] AppHost params set (`lightrag-docker-host`, `lightrag-socks-proxy`, `lightrag-pg-password`)
- [ ] Orchestrator spawns a `lightrag-agent-*` on the VM (`docker -H tcp://localhost:2375 ps`)
