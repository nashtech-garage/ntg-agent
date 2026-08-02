# LightRAG Postgres + remote Docker — Azure Ubuntu VM over TLS

The LightRAG knowledge stack (the pgvector + Apache AGE Postgres, and the per-agent
`lightrag-agent-*` containers) runs on a dedicated Azure Ubuntu VM. The Orchestrator stays on
the main machine and reaches all three channels **directly over TLS** — there is no SSH tunnel
at runtime, so nothing has to be kept alive before the Orchestrator starts.

| Channel | Endpoint | Transport |
|---|---|---|
| Docker daemon | `https://4.193.109.6:2376` | Mutual TLS — the daemon runs with `tlsverify` and admits only CA-signed client certificates |
| Per-agent container ports | `https://4.193.109.6:{20000–20999}` | TLS, gated by the `X-API-Key` header |
| Postgres | `4.193.109.6:5432` | TLS (`SSL Mode=Require`) |

Server: `ntgagent@4.193.109.6` (has sudo; Docker already installed).

> **Server certificates are not validated by the client.** They are signed by a private CA
> (`docker-ca`) whose root is deliberately not distributed, so there is no trust anchor to check
> them against. Traffic is encrypted but servers are unauthenticated — inbound access is
> restricted by the Azure NSG rules in step 4, which are therefore load-bearing, not optional.

---

## 1. Server prep (on the VM, via SSH)

```bash
sudo usermod -aG docker ntgagent      # then log out/in so the group takes effect

git clone <repo-url> ntg-agent        # the compose build context needs scripts/
cd ntg-agent/deploy/lightrag-postgres
cp .env.example .env                   # set POSTGRES_PASSWORD (= AppHost lightrag-pg-password)
```

## 2. Stage the certificates for Postgres

Postgres refuses to start if its key file is group/world readable, or is not owned by the user
the server runs as (uid `999` in this image). The certificates in `~/docker-certs` are owned by
the SSH user, so stage a copy for Postgres rather than mounting them directly:

```bash
mkdir -p ~/docker-certs/pg
cp ~/docker-certs/server-cert.pem ~/docker-certs/server-key.pem ~/docker-certs/pg/
sudo chown 999:999 ~/docker-certs/pg/*
sudo chmod 600 ~/docker-certs/pg/server-key.pem
```

`PG_CERT_DIR` in `.env` points at that directory. Then start Postgres:

```bash
docker compose up -d
docker compose ps
```

`ssl=on` is additive — `pg_hba.conf` keeps its default rules, so the per-agent containers
reaching Postgres over the Docker bridge are unaffected.

## 3. Verify TLS is live

```bash
# Postgres is serving TLS
psql "host=127.0.0.1 port=5432 user=postgres dbname=uploaded-documents sslmode=require" -c '\conninfo'
# → "SSL connection (protocol: TLSv1.3, ...)"

# The daemon is listening on 2376
sudo ss -tlnp | grep 2376
```

## 4. Open the inbound ports (Azure NSG)

Portal → Virtual Machine → Networking → Inbound security rules. **Restrict every rule to the
team's source IPs — never `0.0.0.0/0`.** The Docker API is root-equivalent on this host.

| Port | Purpose | Priority |
|---|---|---|
| `2376` | Docker daemon (usually already present) | 300 |
| `20000-20999` | Per-agent LightRAG containers | 310 |
| `5432` | Postgres | 320 |

## 5. Orchestrator config (on the main machine)

Copy the client certificate down once, then set the AppHost parameters.
`scripts/init-apphost-user-secrets.sh` prompts for all of these:

```bash
scp ntgagent@4.193.109.6:~/docker-certs/client.pfx ./client.pfx   # gitignored (*.pfx)
```

| Parameter | Value |
|---|---|
| `lightrag-docker-host` | `https://4.193.109.6:2376` |
| `lightrag-docker-cert-path` | absolute path to `client.pfx` |
| `lightrag-docker-cert-password` | the PFX password |
| `lightrag-server-host` | `4.193.109.6` |
| `lightrag-server-cert-dir` | `/home/ntgagent/docker-certs` (a path **on the server**) |
| `lightrag-port-bind-host-ip` | `0.0.0.0` |
| `lightrag-postgres-port` | `5432` |
| `lightrag-pg-password` | the same value as `POSTGRES_PASSWORD` above |

Leave every `lightrag-docker-*` / `lightrag-server-*` value empty for a plain all-local dev run
against the local Docker socket.

## 6. Apply the port-reservation migration (once per shared database)

Every developer's Orchestrator reserves LightRAG host ports from a **single shared ledger** in this
Postgres, so two people can never be handed the same port on the shared Docker host. Allocating from
each developer's own local SQL Server used to cause exactly that collision (two agents both assigned
`20001`, second `docker start` fails with "port is already allocated").

The table is deliberately **not** created by the application — apply it once:

```bash
# on the VM
psql -h localhost -p 5432 -U postgres -d uploaded-documents \
     -f deploy/lightrag-postgres/migrations/001_create_agent_port_reservations.sql

# or from the Mac
psql "host=4.193.109.6 port=5432 user=postgres dbname=uploaded-documents sslmode=require" \
     -f deploy/lightrag-postgres/migrations/001_create_agent_port_reservations.sql
```

Verify it landed:

```bash
psql "host=4.193.109.6 port=5432 user=postgres dbname=uploaded-documents sslmode=require" \
     -c '\d agent_port_reservations'
```

Only **one** person needs to run this — the table is shared by the whole team. If it is missing,
agent provisioning fails with a message pointing back to this script.

## 7. Remove stale agents before starting the NTG system

Existing `Agents.LightRagPort` values point at reservations that no longer exist in a fresh
ledger, and may be outside the 20000–20999 range entirely:

```bash
# find the SQL Server container name first:
docker ps --filter "name=sqlserver" --format "{{.Names}}"

# drop it (remember to paste the container name into <sqlserver-container>):
docker exec <sqlserver-container> /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P 'Admin123_Strong!' -C \
    -Q "IF DB_ID('NTGAgent') IS NOT NULL BEGIN ALTER DATABASE [NTGAgent] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [NTGAgent]; PRINT 'NTGAgent dropped'; END ELSE PRINT 'NTGAgent did not exist';"
```

## 8. Start the NTG-Agent as usual (on the main machine)

On first run the Orchestrator pulls `ghcr.io/hkuds/lightrag` on the remote daemon and spawns
`lightrag-agent-*` containers there, each on its reserved port (20000–20999) serving HTTPS.
Documents must be re-uploaded — the previous VM's Postgres volume did not survive the rebuild.

---

## Quick checklist

- [ ] `ntgagent` in the `docker` group (re-logged in)
- [ ] certs staged for Postgres (`~/docker-certs/pg`, owned by uid 999)
- [ ] `docker compose up -d`; `\dx` shows `vector` + `age`; `\conninfo` reports SSL
- [ ] NSG inbound rules for 2376, 20000-20999, 5432 — restricted to team IPs
- [ ] `client.pfx` copied down and the AppHost parameters set
- [ ] port-reservation migration applied (`\d agent_port_reservations` succeeds)
- [ ] local `NTGAgent` database dropped
- [ ] Orchestrator spawns a `lightrag-agent-*` on the VM

## Troubleshooting

**`LightRagDaemonUnavailableException` on startup** — the server is down, the NSG rule for 2376
does not cover your current IP, or the client certificate path/password is wrong. Check in that
order; your home IP changing is the most common cause.

**Postgres container restarts in a loop** — almost always the key file permissions from step 2.
`docker logs lightrag-postgres` will say `private key file "/certs/server-key.pem" has group or
world access`.

**Agent containers start but queries time out** — the NSG rule for 20000–20999 is missing, so the
container's port is bound but unreachable. Confirm with
`curl -k https://4.193.109.6:{port}/health` from the Mac.
