#!/usr/bin/env bash
# Uploads a basemap directory from scripts/fetch-map.sh (PMTiles file, fonts/, sprites/) to the root of the R2 map
# bucket and checks that tiles.kutaapp.com serves it with range requests. See README (Karta).
#
#   scripts/upload-map.sh DIR
#
# Credentials come from 1Password: `op run` resolves the op:// references in scripts/map-upload.env.tpl and passes
# them as environment variables to rclone only, never via a file, an argument or the shell history.
# Needs the 1Password CLI (signed in) and rclone.
set -euo pipefail
cd "$(dirname "$0")/.."

dir="${1:?Usage: $0 DIR}"
tiles=("$dir"/*.pmtiles)
[ ${#tiles[@]} -eq 1 ] && [ -f "${tiles[0]}" ] || { echo "[upload-map] Expected exactly one .pmtiles file in $dir" >&2; exit 1; }
[ -d "$dir/fonts" ] && [ -d "$dir/sprites" ] || { echo "[upload-map] $dir lacks fonts/ or sprites/" >&2; exit 1; }
file="$(basename "${tiles[0]}")"

# The bucket is in the EU jurisdiction, which has its own S3 endpoint. A token limited to one bucket may not check
# or create buckets, hence --s3-no-check-bucket.
op run --env-file=scripts/map-upload.env.tpl -- sh -c '
  for name in KUTA_MAP_ACCOUNT_ID KUTA_MAP_BUCKET RCLONE_CONFIG_R2_ACCESS_KEY_ID RCLONE_CONFIG_R2_SECRET_ACCESS_KEY; do
    eval "value=\${$name:-}"
    [ -n "$value" ] || { echo "[upload-map] $name is empty: check the field in kuta-map-r2" >&2; exit 1; }
  done
  echo "[upload-map] Copying to r2:$KUTA_MAP_BUCKET"
  export RCLONE_CONFIG_R2_TYPE=s3 RCLONE_CONFIG_R2_PROVIDER=Cloudflare
  export RCLONE_CONFIG_R2_ENDPOINT="https://$KUTA_MAP_ACCOUNT_ID.eu.r2.cloudflarestorage.com"
  rclone copy "$1" "r2:$KUTA_MAP_BUCKET" --s3-no-check-bucket --transfers 16 --progress
' _ "$dir"

echo "[upload-map] Checking https://tiles.kutaapp.com"
magic="$(curl -fsS -r 0-6 "https://tiles.kutaapp.com/$file" || true)"
[ "$magic" = PMTiles ] || { echo "[upload-map] $file is not served with range requests" >&2; exit 1; }
curl -fsS -o /dev/null "https://tiles.kutaapp.com/sprites/v4/light.json" || { echo "[upload-map] sprites/v4/light.json is not served" >&2; exit 1; }
echo "[upload-map] Done. The web build uses VITE_MAP_TILES=$file (default in web/vite.config.ts)."
