#!/usr/bin/env bash
# desc: On-server health check for the LightRAG Ubuntu VM (OS + Docker + Postgres)
#
# Run this ON the dedicated Azure Ubuntu VM (ntgagent@4.193.109.6), not on the
# Mac — it inspects OS-level resources (disk/memory/OOM) that the SSH tunnel can't
# see, plus the Docker daemon, the lightrag-postgres + lightrag-agent-* containers,
# and Postgres itself.
#
#   ssh -i ~/.ssh/ntg-vm ntgagent@4.193.109.6
#   cd ntg-agent/deploy/lightrag-postgres && ./health-check.sh
#
# It is read-only: it changes nothing, and every check degrades gracefully so a
# single missing tool or container never aborts the rest. Exit code is 0 when all
# checks pass, 1 if any WARN was raised, 2 if any FAIL was raised — so it is safe
# to wire into cron/monitoring later.
#
# Tunables (override via env): DISK_WARN/DISK_FAIL (%), MEM_WARN (% used),
#   PG_CONTAINER, PG_USER, PG_DB, AGENT_PORT_MIN, AGENT_PORT_MAX.

set -uo pipefail   # NOT -e: a failing probe must not abort the whole report.

# --- config / thresholds ------------------------------------------------------
DISK_WARN="${DISK_WARN:-85}"
DISK_FAIL="${DISK_FAIL:-95}"
MEM_WARN="${MEM_WARN:-90}"
PG_CONTAINER="${PG_CONTAINER:-lightrag-postgres}"
PG_USER="${PG_USER:-postgres}"
PG_DB="${PG_DB:-uploaded-documents}"
AGENT_PORT_MIN="${AGENT_PORT_MIN:-20000}"
AGENT_PORT_MAX="${AGENT_PORT_MAX:-20999}"

# --- output helpers -----------------------------------------------------------
if [ -t 1 ]; then
  C_RESET=$'\033[0m'; C_DIM=$'\033[2m'; C_GREEN=$'\033[32m'
  C_YELLOW=$'\033[33m'; C_RED=$'\033[31m'; C_BOLD=$'\033[1m'
else
  C_RESET=; C_DIM=; C_GREEN=; C_YELLOW=; C_RED=; C_BOLD=
fi

WARN_COUNT=0
FAIL_COUNT=0

section() { printf '\n%s== %s ==%s\n' "$C_BOLD" "$1" "$C_RESET"; }
ok()      { printf '  %s[ OK ]%s %s\n'   "$C_GREEN"  "$C_RESET" "$1"; }
info()    { printf '  %s[INFO]%s %s\n'   "$C_DIM"    "$C_RESET" "$1"; }
warn()    { printf '  %s[WARN]%s %s\n'   "$C_YELLOW" "$C_RESET" "$1"; WARN_COUNT=$((WARN_COUNT + 1)); }
fail()    { printf '  %s[FAIL]%s %s\n'   "$C_RED"    "$C_RESET" "$1"; FAIL_COUNT=$((FAIL_COUNT + 1)); }
have()    { command -v "$1" >/dev/null 2>&1; }

# docker may need sudo depending on group membership; resolve once.
DOCKER=""
if have docker; then
  if docker info >/dev/null 2>&1; then
    DOCKER="docker"
  elif sudo -n docker info >/dev/null 2>&1; then
    DOCKER="sudo docker"
  fi
fi
dk() { $DOCKER "$@"; }   # only call when $DOCKER is non-empty

# =============================================================================
printf '%s LightRAG VM health check %s\n' "$C_BOLD" "$C_RESET"
printf '%s host=%s  date=%s %s\n' "$C_DIM" "$(hostname)" "$(date '+%Y-%m-%d %H:%M:%S %Z')" "$C_RESET"

# --- 1. OS resources ----------------------------------------------------------
section "OS resources"

# Uptime / recent reboot
if UP="$(uptime -p 2>/dev/null)"; then info "Uptime: $UP"; fi
SECS_UP="$(cut -d. -f1 /proc/uptime 2>/dev/null || echo 0)"
if [ "${SECS_UP:-0}" -lt 600 ] 2>/dev/null; then
  warn "Machine booted less than 10 min ago — was there an unexpected reboot?"
fi

# Load average vs CPU count
NCPU="$(nproc 2>/dev/null || echo 1)"
LOAD1="$(awk '{print $1}' /proc/loadavg 2>/dev/null)"
if [ -n "${LOAD1:-}" ]; then
  # WARN when 1-min load exceeds 2x the core count.
  if awk -v l="$LOAD1" -v c="$NCPU" 'BEGIN{exit !(l > 2*c)}'; then
    warn "Load average ${LOAD1} is high for ${NCPU} CPU(s)"
  else
    ok "Load average ${LOAD1} across ${NCPU} CPU(s)"
  fi
fi

# Memory
if have free; then
  read -r MEM_TOTAL MEM_USED MEM_AVAIL < <(free -m | awk '/^Mem:/{print $2, $3, $7}')
  if [ -n "${MEM_TOTAL:-}" ] && [ "${MEM_TOTAL:-0}" -gt 0 ]; then
    MEM_PCT=$(( MEM_USED * 100 / MEM_TOTAL ))
    MSG="Memory ${MEM_USED}/${MEM_TOTAL} MB used (${MEM_PCT}%), ${MEM_AVAIL} MB available"
    if [ "$MEM_PCT" -ge "$MEM_WARN" ]; then warn "$MSG"; else ok "$MSG"; fi
  fi
  SWAP_USED="$(free -m | awk '/^Swap:/{print $3}')"
  [ "${SWAP_USED:-0}" -gt 0 ] 2>/dev/null && info "Swap in use: ${SWAP_USED} MB"
fi

# Disk — root plus the docker data dir (usually the first to fill).
while read -r target; do
  [ -n "$target" ] || continue
  read -r USE_PCT MOUNT AVAIL < <(df -P "$target" 2>/dev/null | awk 'NR==2{gsub("%","",$5); print $5, $6, $4}')
  [ -n "${USE_PCT:-}" ] || continue
  AVAIL_H="$(df -Ph "$target" 2>/dev/null | awk 'NR==2{print $4}')"
  MSG="Disk ${MOUNT} ${USE_PCT}% used (${AVAIL_H:-$AVAIL} free)"
  if   [ "$USE_PCT" -ge "$DISK_FAIL" ]; then fail "$MSG"
  elif [ "$USE_PCT" -ge "$DISK_WARN" ]; then warn "$MSG"
  else ok "$MSG"; fi
done < <(printf '%s\n' "/" "/var/lib/docker" | awk '!seen[$0]++')

# OOM killer + recent kernel/systemd errors
if have dmesg; then
  OOM="$( (dmesg -T 2>/dev/null || sudo -n dmesg -T 2>/dev/null) | grep -ic 'killed process\|out of memory' )"
  if [ "${OOM:-0}" -gt 0 ] 2>/dev/null; then
    warn "OOM-killer has fired ${OOM} time(s) this boot (dmesg) — a container was likely killed"
  else
    ok "No OOM-killer events in dmesg"
  fi
fi
if have journalctl; then
  ERRS="$(journalctl -p err -b --no-pager 2>/dev/null | wc -l)"
  [ "${ERRS:-0}" -gt 0 ] 2>/dev/null && info "journalctl reports ${ERRS} error-level line(s) since boot (review: journalctl -p err -b)"
fi

# --- 2. Docker daemon ---------------------------------------------------------
section "Docker daemon"
if [ -z "$DOCKER" ]; then
  fail "Docker CLI not reachable (not installed, daemon down, or no permission). Try: systemctl status docker"
else
  if [ "$DOCKER" = "sudo docker" ]; then
    info "Using 'sudo docker' (current user not in the docker group — usermod -aG docker \$USER)"
  fi
  DVER="$(dk version --format '{{.Server.Version}}' 2>/dev/null)"
  ok "Daemon reachable (server ${DVER:-?})"
  # Storage footprint — images/containers/volumes growth is the usual disk culprit.
  DF_LINE="$(dk system df --format '{{.Type}}={{.Size}}({{.Reclaimable}} reclaimable)' 2>/dev/null | tr '\n' ' ')"
  [ -n "$DF_LINE" ] && info "docker system df: ${DF_LINE}"
fi

# --- 3. Containers ------------------------------------------------------------
section "Containers"
if [ -n "$DOCKER" ]; then
  # Postgres container
  PG_STATE="$(dk ps -a --filter "name=^${PG_CONTAINER}$" --format '{{.State}}' 2>/dev/null | head -n1)"
  PG_STATUS="$(dk ps -a --filter "name=^${PG_CONTAINER}$" --format '{{.Status}}' 2>/dev/null | head -n1)"
  case "$PG_STATE" in
    running) ok "${PG_CONTAINER}: ${PG_STATUS}" ;;
    "")      fail "${PG_CONTAINER}: not found — is the stack up? (docker compose up -d)" ;;
    *)       fail "${PG_CONTAINER}: ${PG_STATE} (${PG_STATUS})" ;;
  esac

  # Per-agent containers
  AGENTS_RUNNING=0
  AGENTS_BAD=0
  AGENTS_OOR=0   # out of the reserved port range
  while IFS='|' read -r name state status; do
    [ -n "$name" ] || continue
    if [ "$state" = "running" ]; then
      AGENTS_RUNNING=$((AGENTS_RUNNING + 1))
      # Verify the published host port sits in the reserved band.
      pmap="$(dk port "$name" 9621/tcp 2>/dev/null | head -n1)"
      port="${pmap##*:}"
      if [ -n "$port" ] && { [ "$port" -lt "$AGENT_PORT_MIN" ] || [ "$port" -gt "$AGENT_PORT_MAX" ]; } 2>/dev/null; then
        warn "${name}: port ${port} is outside the reserved ${AGENT_PORT_MIN}-${AGENT_PORT_MAX} range (stale agent?)"
        AGENTS_OOR=$((AGENTS_OOR + 1))
      fi
    else
      warn "${name}: ${state} (${status})"
      AGENTS_BAD=$((AGENTS_BAD + 1))
    fi
  done < <(dk ps -a --filter "name=lightrag-agent-" --format '{{.Names}}|{{.State}}|{{.Status}}' 2>/dev/null)

  if [ "$AGENTS_RUNNING" -eq 0 ] && [ "$AGENTS_BAD" -eq 0 ]; then
    info "No lightrag-agent-* containers present (none provisioned yet, or all idle-shut-down)"
  else
    ok "lightrag-agent-* running: ${AGENTS_RUNNING}, not-running: ${AGENTS_BAD}, out-of-range ports: ${AGENTS_OOR}"
  fi

  # Anything stuck restarting is a crash loop — surface its recent logs.
  while IFS= read -r cname; do
    [ -n "$cname" ] || continue
    fail "${cname} is in a restart loop — last 15 log lines:"
    dk logs --tail 15 --timestamps "$cname" 2>&1 | sed 's/^/        /'
  done < <(dk ps --filter "name=lightrag" --filter "status=restarting" --format '{{.Names}}' 2>/dev/null)

  # Live resource snapshot for the lightrag containers.
  CIDS="$(dk ps --filter "name=lightrag" --format '{{.Names}}' 2>/dev/null)"
  if [ -n "$CIDS" ]; then
    printf '  %sresource usage:%s\n' "$C_DIM" "$C_RESET"
    # shellcheck disable=SC2086
    dk stats --no-stream --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.MemPerc}}' $CIDS 2>/dev/null | sed 's/^/    /'
  fi
else
  info "Skipped (Docker not reachable)"
fi

# --- 4. Postgres --------------------------------------------------------------
section "Postgres (${PG_CONTAINER} / ${PG_DB})"
if [ -n "$DOCKER" ] && [ "${PG_STATE:-}" = "running" ]; then
  if dk exec "$PG_CONTAINER" pg_isready -U "$PG_USER" >/dev/null 2>&1; then
    ok "pg_isready: accepting connections"

    # Extensions the LightRAG stack requires.
    EXTS="$(dk exec "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -tAc \
            "SELECT string_agg(extname, ',' ORDER BY extname) FROM pg_extension WHERE extname IN ('vector','age');" 2>/dev/null)"
    case "$EXTS" in
      *vector*age*|*age*vector*) ok "Extensions present: ${EXTS}" ;;
      *) fail "Required extensions missing (found: '${EXTS:-none}', need vector + age)" ;;
    esac

    # Connection saturation vs max_connections.
    CUR="$(dk exec "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -tAc \
           "SELECT count(*) FROM pg_stat_activity;" 2>/dev/null)"
    MAXC="$(dk exec "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -tAc "SHOW max_connections;" 2>/dev/null)"
    if [ -n "${CUR:-}" ] && [ -n "${MAXC:-}" ]; then
      MSG="Connections: ${CUR}/${MAXC}"
      if [ "$CUR" -ge $(( MAXC * 90 / 100 )) ] 2>/dev/null; then warn "$MSG (near limit)"; else ok "$MSG"; fi
    fi

    # DB size — for capacity awareness, informational only.
    DBSIZE="$(dk exec "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -tAc \
              "SELECT pg_size_pretty(pg_database_size('${PG_DB}'));" 2>/dev/null)"
    [ -n "${DBSIZE:-}" ] && info "Database ${PG_DB} size: ${DBSIZE}"
  else
    fail "pg_isready failed — Postgres is not accepting connections (check: docker logs ${PG_CONTAINER})"
  fi
else
  info "Skipped (Postgres container not running)"
fi

# --- summary ------------------------------------------------------------------
section "Summary"
if [ "$FAIL_COUNT" -gt 0 ]; then
  printf '  %s%d FAIL%s, %d WARN — action needed.\n' "$C_RED" "$FAIL_COUNT" "$C_RESET" "$WARN_COUNT"
  exit 2
elif [ "$WARN_COUNT" -gt 0 ]; then
  printf '  %s%d WARN%s, 0 FAIL — worth a look.\n' "$C_YELLOW" "$WARN_COUNT" "$C_RESET"
  exit 1
else
  printf '  %sAll checks passed.%s\n' "$C_GREEN" "$C_RESET"
  exit 0
fi
