#!/usr/bin/env bash
# Importerar en Fly-apps hemligheter från 1Password (portat från Kredovis): mallen bredvid konfigurationen
# (fly.toml -> fly.secrets.tpl) innehåller bara op://-referenser, som `op inject` löser upp.
#
#   scripts/fly-secrets.sh FLY_CONFIG [flaggor till fly secrets import, t.ex. --stage]
#
# Värdena går från op till flyctl i minnet: aldrig via en fil, ett argument eller skalhistoriken.
set -euo pipefail

config="${1:?Användning: $0 FLY_CONFIG [--stage]}"
shift
template="${config%.toml}.secrets.tpl"
app="$(sed -n "s/^app = '\(.*\)'/\1/p" "$config")"
[ -n "$app" ] || { echo "Kunde inte läsa appens namn från $config." >&2; exit 1; }
[ -f "$template" ] || { echo "$template finns inte." >&2; exit 1; }

# En referens som inte går att lösa upp ska stoppa här, inte importera en halv uppsättning.
secrets="$(grep -v -e '^#' -e '^[[:space:]]*$' "$template" | op inject)"
printf '%s\n' "$secrets" | flyctl secrets import --app "$app" "$@"
