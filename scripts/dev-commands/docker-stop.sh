#!/usr/bin/env bash
# desc: Stop any container you want

set -euo pipefail

# Repo root, regardless of where ntg is invoked from.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# Get all running containers:
containers=($(docker ps --format "{{.Names}}" 2>/dev/null || echo ""))

if [ -z "$containers" ]; then
	echo "No running containers"
	exit 0
fi

echo "Running containers:"
for i in "${!containers[@]}"; do 
	printf '	%d) %s\n' "$((i + 1))" "${containers[$i]}"
done

# Prompt the user:
printf 'Choose containers to stop (space-separated): '
read -r input

# Validate choices and stop them
case "$input" in 
	q | Q | "")
		exit 0;;
esac

for choice in $input; do
	if ! [[ "$choice" =~ ^[0-9]+$ ]] || [ "$choice" -lt 1 ] || [ "$choice" -gt "${#containers[@]}" ]; then
		echo "Invalid choice: $choice" >&2
		exit 1
    fi

	# stop each containers
	selected_container="${containers[$((choice - 1))]}"
	docker stop "$selected_container"
done

# Confirm
echo 
echo "Stopped containers:"
docker ps -a --filter "status=exited" --format "table {{.Names}}"
