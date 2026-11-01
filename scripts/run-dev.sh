#!/usr/bin/env bash
# Starts Kuta locally in one terminal: Supabase (unless already running), the API and the web (Vite with hot reload).
# Ctrl+C stops the API and the web. Supabase keeps running; the script prints how to stop it.
# The API does not reload on changes (dotnet watch cannot be stopped with signals in SDK 10.0.402): restart the script.
#
#   scripts/run-dev.sh                 ports from 55300 (supabase/config.toml as is)
#   scripts/run-dev.sh --ports 55400   an own instance from 55400, with its own Supabase stack and database
#
# Ports from BAS: web BAS, preview BAS+1, API BAS+2, Supabase BAS+20–29 (Mailpit BAS+24).
set -eo pipefail
cd "$(dirname "$0")/.."
root="$(pwd)"

base=55300
while [ $# -gt 0 ]; do
  case "$1" in
    --ports) base="${2:-}"; shift 2 ;;
    --ports=*) base="${1#--ports=}"; shift ;;
    *) echo "[run-dev] Unknown argument: $1 (use --ports BAS)" >&2; exit 1 ;;
  esac
done
if ! [[ "$base" =~ ^[0-9]+$ ]] || [ "$base" -lt 1024 ] || [ "$base" -gt 65500 ]; then
  echo "[run-dev] --ports must be a number between 1024 and 65500" >&2
  exit 1
fi

if [ "$base" = 55300 ]; then
  workdir=.
  project=kuta
else
  # Own copy of the Supabase config with the ports moved and its own project_id (own containers and data)
  workdir=".context/supabase-$base"
  project="kuta-$base"
  mkdir -p "$workdir/supabase"
  cp -R supabase/templates "$workdir/supabase/"
  rewrite=(-e "s/^project_id = \"kuta\"/project_id = \"$project\"/")
  for offset in $(seq 0 29); do rewrite+=(-e "s/$((55300 + offset))/$((base + offset))/g"); done
  sed "${rewrite[@]}" supabase/config.toml > "$workdir/supabase/config.toml"
fi

if ! supabase status --workdir "$workdir" >/dev/null 2>&1; then
  supabase start --workdir "$workdir"
  # The auth container can start before Kong and get stuck on Supabase's English default email; a restart loads our template.
  docker restart "supabase_auth_$project" >/dev/null
  until curl -sf "http://127.0.0.1:$((base + 21))/auth/v1/health" \
    -H "apikey: $(supabase status --workdir "$workdir" -o env | sed -n 's/^PUBLISHABLE_KEY="\(.*\)"$/\1/p')" >/dev/null; do
    sleep 1
  done
fi

use_node() { set +eu; source "${NVM_DIR:-$HOME/.nvm}/nvm.sh"; nvm use >/dev/null; }
[ -d web/node_modules ] || (cd web && use_node && npm install)
# Own build directory per instance, so instances starting at the same time do not write to the same bin/obj
build="$root/.context/api-$base"
dotnet build api/Kuta.Api --nologo --verbosity quiet --artifacts-path "$build"

# Prefix on every line to show which process is writing
prefix() { awk -v p="[$1] " '{ print p $0; fflush() }'; }

pids=()
cleanup() {
  trap - EXIT
  kill "${pids[@]}" 2>/dev/null || true
  wait 2>/dev/null || true
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# exec + process substitution so that $! is the server itself and not the prefix process
(cd api/Kuta.Api && export ASPNETCORE_ENVIRONMENT=Development \
  Urls="http://localhost:$((base + 2))" \
  ConnectionStrings__Default="Host=127.0.0.1;Port=$((base + 22));Database=postgres;Username=postgres;Password=postgres" \
  Supabase__Url="http://127.0.0.1:$((base + 21))" \
  Storage__ServiceUrl="http://127.0.0.1:$((base + 21))/storage/v1/s3" &&
  exec dotnet "$build/bin/Kuta.Api/debug/Kuta.Api.dll") > >(prefix api) 2>&1 &
pids+=($!)
(cd web && use_node && export KUTA_WEB_PORT="$base" KUTA_API_PORT="$((base + 2))" && exec ./node_modules/.bin/vite) \
  > >(prefix web) 2>&1 &
pids+=($!)

echo "Web:     http://localhost:$base"
echo "API:     http://localhost:$((base + 2))"
echo "Mailpit: http://127.0.0.1:$((base + 24))"
echo "Stop Supabase: supabase stop --workdir $workdir"

# Stop both when either of them exits; bash 3.2 on macOS lacks wait -n.
while kill -0 "${pids[0]}" 2>/dev/null && kill -0 "${pids[1]}" 2>/dev/null; do sleep 1; done
