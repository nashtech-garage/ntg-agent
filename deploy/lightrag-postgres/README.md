# LightRAG Postgres + gateway + remote Docker — Azure Ubuntu VM over TLS

The LightRAG knowledge stack (the pgvector + Apache AGE Postgres, the nginx gateway, and the
per-agent `lightrag-agent-*` containers) runs on a dedicated Azure Ubuntu VM. The Orchestrator
stays on the main machine and reaches all three channels **directly over TLS** — there is no SSH
tunnel at runtime, so nothing has to be kept alive before the Orchestrator starts.

| Channel | Endpoint | Transport |
|---|---|---|
| Docker daemon | `https://4.193.109.6:2376` | Mutual TLS — the daemon runs with `tlsverify` and admits only CA-signed client certificates |
| nginx gateway | `https://4.193.109.6/agents/{agentId}/*` | TLS, gated by the `X-API-Key` header |
| Postgres | `4.193.109.6:5432` | TLS when offered (`SSL Mode=Prefer`) |

Server: `ntgagent@4.193.109.6` (has sudo; Docker already installed).

The gateway routes `/agents/{agentId}/*` to the `lightrag-agent-{agentId}` container **by name**
over the `ntg-agent-lightrag` Docker network — agent containers publish no host ports, so the
gateway is the only inbound path to them. The Orchestrator attaches the gateway (and Postgres)
to that network automatically at runtime.

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
cp .env.example .env                   # set POSTGRES_PASSWORD (= AppHost lightrag-pg-password),
                                       # PG_CERT_DIR and GATEWAY_CERT_DIR
```

## 2. Stage the certificates

Postgres refuses to start if its key file is group/world readable, or is not owned by the user
the server runs as (uid `999` in this image). The certificates in `~/docker-certs` are owned by
the SSH user, so stage a copy for Postgres rather than mounting them directly:

```bash
mkdir -p ~/docker-certs/pg
cp ~/docker-certs/server-cert.pem ~/docker-certs/server-key.pem ~/docker-certs/pg/
sudo chown 999:999 ~/docker-certs/pg/*
sudo chmod 600 ~/docker-certs/pg/server-key.pem
```

`PG_CERT_DIR` in `.env` points at that directory. The gateway runs as root in its container and
can read the originals directly — set `GATEWAY_CERT_DIR=/home/ntgagent/docker-certs`. Then:

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

# The gateway is serving TLS
curl -k https://127.0.0.1/gateway-health   # → ok

# The daemon is listening on 2376
sudo ss -tlnp | grep 2376
```

## 4. Open the inbound ports (Azure NSG)

Portal → Virtual Machine → Networking → Inbound security rules. **Restrict every rule to the
team's source IPs — never `0.0.0.0/0`.** The Docker API is root-equivalent on this host.

| Port | Purpose | Priority |
|---|---|---|
| `2376` | Docker daemon (usually already present) | 300 |
| `443` | nginx gateway (per-agent LightRAG APIs) | 310 |
| `5432` | Postgres | 320 |

The old `20000-20999` rule is obsolete — agent containers no longer publish host ports; delete
the rule.

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
| `lightrag-gateway-url` | `https://4.193.109.6` |
| `lightrag-postgres-port` | `5432` |
| `lightrag-pg-password` | the same value as `POSTGRES_PASSWORD` above |

Leave every `lightrag-docker-*` / `lightrag-server-*` / `lightrag-gateway-*` value empty for a
plain all-local dev run (local Docker socket + the `deploy/lightrag-local` compose stack, whose
gateway serves plain HTTP on `http://localhost:8080` — the code's default `GatewayUrl`).

## 6. Start the NTG-Agent as usual (on the main machine)

On first run the Orchestrator pulls `ghcr.io/hkuds/lightrag` on the remote daemon and spawns
`lightrag-agent-*` containers there, reachable only through the gateway. Containers created
before the gateway existed are recreated automatically (their env no longer matches), and the
old `Agents.LightRagPort` column is dropped by the EF `RemoveAgentLightRagPort` migration — no
manual database cleanup is needed.

---

## Quick checklist

- [ ] `ntgagent` in the `docker` group (re-logged in)
- [ ] certs staged for Postgres (`~/docker-certs/pg`, owned by uid 999); `GATEWAY_CERT_DIR` set
- [ ] `docker compose up -d`; `\dx` shows `vector` + `age`; `\conninfo` reports SSL
- [ ] `curl -k https://127.0.0.1/gateway-health` → ok
- [ ] NSG inbound rules for 2376, 443, 5432 — restricted to team IPs; 20000-20999 deleted
- [ ] `client.pfx` copied down and the AppHost parameters set
- [ ] Orchestrator spawns a `lightrag-agent-*` on the VM, reachable via the gateway

## Troubleshooting

**`LightRagDaemonUnavailableException` on startup** — the server is down, the NSG rule for 2376
does not cover your current IP, or the client certificate path/password is wrong. Check in that
order; your home IP changing is the most common cause.

**Postgres container restarts in a loop** — almost always the key file permissions from step 2.
`docker logs lightrag-postgres` will say `private key file "/certs/server-key.pem" has group or
world access`.

**Agent containers start but queries fail / `LightRagContainerNotReadyException`** — the gateway
is down or unreachable: check `docker ps` for `lightrag-gateway`, the NSG rule for 443, and
`curl -k https://4.193.109.6/gateway-health` from the Mac. A 502 from
`curl -k https://4.193.109.6/agents/{agentId}/health` means the gateway is fine but that agent's
container is stopped — the Orchestrator restarts it on the next request.
