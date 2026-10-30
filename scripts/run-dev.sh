#!/usr/bin/env bash
# Startar Kuta lokalt i en terminal: Supabase (om det inte redan kör), API:t och webben (Vite med hot reload).
# Ctrl+C stoppar API:t och webben. Supabase fortsätter köra; skriptet skriver ut hur det stoppas.
# API:t laddas inte om vid ändringar (dotnet watch går inte att stoppa med signaler i SDK 10.0.402): starta om skriptet.
#
#   scripts/run-dev.sh                 portar från 55300 (supabase/config.toml som den är)
#   scripts/run-dev.sh --ports 55400   en egen instans från 55400, med egen Supabase-stack och databas
#
# Portar från BAS: webben BAS, förhandsvisning BAS+1, API:t BAS+2, Supabase BAS+20–29 (Mailpit BAS+24).
set -eo pipefail
cd "$(dirname "$0")/.."
root="$(pwd)"

base=55300
while [ $# -gt 0 ]; do
  case "$1" in
    --ports) base="${2:-}"; shift 2 ;;
    --ports=*) base="${1#--ports=}"; shift ;;
    *) echo "[run-dev] Okänt argument: $1 (använd --ports BAS)" >&2; exit 1 ;;
  esac
done
if ! [[ "$base" =~ ^[0-9]+$ ]] || [ "$base" -lt 1024 ] || [ "$base" -gt 65500 ]; then
  echo "[run-dev] --ports måste vara ett tal mellan 1024 och 65500" >&2
  exit 1
fi

if [ "$base" = 55300 ]; then
  workdir=.
  project=kuta
else
  # Egen kopia av Supabase-konfigurationen med portarna flyttade och eget project_id (egna containrar och data)
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
  # Auth-containern kan starta före Kong och fastna på Supabases engelska standardmejl; en omstart läser in vår mall.
  docker restart "supabase_auth_$project" >/dev/null
  until curl -sf "http://127.0.0.1:$((base + 21))/auth/v1/health" \
    -H "apikey: $(supabase status --workdir "$workdir" -o env | sed -n 's/^PUBLISHABLE_KEY="\(.*\)"$/\1/p')" >/dev/null; do
    sleep 1
  done
fi

use_node() { set +eu; source "${NVM_DIR:-$HOME/.nvm}/nvm.sh"; nvm use >/dev/null; }
[ -d web/node_modules ] || (cd web && use_node && npm install)
# Egen byggkatalog per instans, så att instanser som startar samtidigt inte skriver i samma bin/obj
build="$root/.context/api-$base"
dotnet build api/Kuta.Api --nologo --verbosity quiet --artifacts-path "$build"

# Prefix på varje rad så att det syns vilken process som skriver
prefix() { awk -v p="[$1] " '{ print p $0; fflush() }'; }

pids=()
cleanup() {
  trap - EXIT
  kill "${pids[@]}" 2>/dev/null || true
  wait 2>/dev/null || true
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# exec + process substitution så att $! är själva servern och inte prefix-processen
(cd api/Kuta.Api && export ASPNETCORE_ENVIRONMENT=Development \
  Urls="http://localhost:$((base + 2))" \
  ConnectionStrings__Default="Host=127.0.0.1;Port=$((base + 22));Database=postgres;Username=postgres;Password=postgres" \
  Supabase__Url="http://127.0.0.1:$((base + 21))" &&
  exec dotnet "$build/bin/Kuta.Api/debug/Kuta.Api.dll") > >(prefix api) 2>&1 &
pids+=($!)
(cd web && use_node && export KUTA_WEB_PORT="$base" KUTA_API_PORT="$((base + 2))" && exec ./node_modules/.bin/vite) \
  > >(prefix web) 2>&1 &
pids+=($!)

echo "Webben:  http://localhost:$base"
echo "API:     http://localhost:$((base + 2))"
echo "Mailpit: http://127.0.0.1:$((base + 24))"
echo "Stoppa Supabase: supabase stop --workdir $workdir"

# Stoppa båda när någon av dem avslutas; bash 3.2 på macOS saknar wait -n.
while kill -0 "${pids[0]}" 2>/dev/null && kill -0 "${pids[1]}" 2>/dev/null; do sleep 1; done
