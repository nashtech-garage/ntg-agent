#!/usr/bin/env bash
# desc: Show the ports this project uses and which are currently in use
#
# Lists the known fixed ports from NTG.Agent.AppHost (dashboard, OTLP, resource
# service, SQL Server, Elasticsearch, Kibana) with their purpose and whether they
# are currently listening. App services and per-agent LightRAG containers get
# DYNAMIC ports from Aspire, so those listeners are shown separately.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# port|description  (known fixed ports — see NTG.Agent.AppHost Program.cs + launchSettings.json)
KNOWN_PORTS=(
  "17050|Aspire dashboard (https)"
  "15285|Aspire dashboard (http)"
  "21030|Dashboard OTLP endpoint (https)"
  "19221|Dashboard OTLP endpoint (http)"
  "22084|Resource service endpoint (https)"
  "20185|Resource service endpoint (http)"
  "1433|SQL Server"
  "9200|Elasticsearch"
  "5601|Kibana"
)

is_listening() {
  lsof -nP -iTCP:"$1" -sTCP:LISTEN >/dev/null 2>&1
}

printf 'Known project ports\n'
printf '%-7s  %-10s  %s\n' "PORT" "STATUS" "PURPOSE"
printf '%-7s  %-10s  %s\n' "-----" "------" "-------"
for entry in "${KNOWN_PORTS[@]}"; do
  port="${entry%%|*}"
  purpose="${entry#*|}"
  if is_listening "$port"; then
    status="IN USE"
  else
    status="free"
  fi
  printf '%-7s  %-10s  %s\n' "$port" "$status" "$purpose"
done

printf '\nDynamic listeners (Aspire-assigned ports for app services)\n'
dotnet_ports="$(lsof -nP -iTCP -sTCP:LISTEN 2>/dev/null | awk 'NR==1 || /dotnet/' || true)"
if [ -n "$dotnet_ports" ] && [ "$(printf '%s\n' "$dotnet_ports" | wc -l)" -gt 1 ]; then
  printf '%s\n' "$dotnet_ports"
else
  printf '  (no dotnet processes listening — is the AppHost running?)\n'
fi

printf '\nDocker-published ports (containers incl. per-agent LightRAG)\n'
if command -v docker >/dev/null 2>&1; then
  docker_ports="$(docker ps --format '{{.Names}}\t{{.Ports}}' 2>/dev/null || true)"
  if [ -n "$docker_ports" ]; then
    printf '%s\n' "$docker_ports"
  else
    printf '  (no running containers)\n'
  fi
else
  printf '  (docker not available)\n'
fi
