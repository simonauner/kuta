#!/usr/bin/env bash
# Startar Kuta lokalt i en terminal: Supabase (om det inte redan kör), API:t och webben (Vite med hot reload).
# Ctrl+C stoppar API:t och webben. Supabase fortsätter köra; stoppa det med `supabase stop`.
# API:t laddas inte om vid ändringar (dotnet watch går inte att stoppa med signaler i SDK 10.0.402): starta om skriptet.
set -eo pipefail
cd "$(dirname "$0")/.."

if ! supabase status >/dev/null 2>&1; then
  supabase start
  # Auth-containern kan starta före Kong och fastna på Supabases engelska standardmejl; en omstart läser in vår mall.
  docker restart supabase_auth_kuta >/dev/null
  until curl -sf http://127.0.0.1:55321/auth/v1/health -H 'apikey: sb_publishable_ACJWlzQHlZjBrEguHvfOxg_3BJgxAaH' >/dev/null; do
    sleep 1
  done
fi

use_node() { set +eu; source "${NVM_DIR:-$HOME/.nvm}/nvm.sh"; nvm use >/dev/null; }
[ -d web/node_modules ] || (cd web && use_node && npm install)
dotnet build api/Kuta.Api --nologo --verbosity quiet

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
(cd api/Kuta.Api && ASPNETCORE_ENVIRONMENT=Development exec dotnet bin/Debug/net10.0/Kuta.Api.dll) > >(prefix api) 2>&1 &
pids+=($!)
(cd web && use_node && exec ./node_modules/.bin/vite) > >(prefix web) 2>&1 &
pids+=($!)

echo "Webben:  http://localhost:55300"
echo "API:     http://localhost:55302"
echo "Mailpit: http://127.0.0.1:55324"

# Stoppa båda när någon av dem avslutas; bash 3.2 på macOS saknar wait -n.
while kill -0 "${pids[0]}" 2>/dev/null && kill -0 "${pids[1]}" 2>/dev/null; do sleep 1; done
