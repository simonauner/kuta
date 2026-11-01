#!/usr/bin/env bash
# Imports a Fly app's secrets from 1Password: the template next to the config
# (fly.toml -> fly.secrets.tpl) contains only op:// references, which `op inject` resolves.
#
#   scripts/fly-secrets.sh FLY_CONFIG [flags for fly secrets import, e.g. --stage]
#
# The values go from op to flyctl in memory: never via a file, an argument or the shell history.
set -euo pipefail

config="${1:?Usage: $0 FLY_CONFIG [--stage]}"
shift
template="${config%.toml}.secrets.tpl"
app="$(sed -n "s/^app = '\(.*\)'/\1/p" "$config")"
[ -n "$app" ] || { echo "Could not read the app name from $config." >&2; exit 1; }
[ -f "$template" ] || { echo "$template does not exist." >&2; exit 1; }

# A reference that cannot be resolved must stop here, not import half a set.
secrets="$(grep -v -e '^#' -e '^[[:space:]]*$' "$template" | op inject)"
printf '%s\n' "$secrets" | flyctl secrets import --app "$app" "$@"
