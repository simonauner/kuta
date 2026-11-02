#!/usr/bin/env bash
# Fetches Kuta's basemap: a PMTiles extract of Protomaps' daily OpenStreetMap build plus the fonts and sprites
# the style uses (web/src/lib/map.ts). See README (Karta).
#
#   scripts/fetch-map.sh                       small area (Uppsala–Stockholm, ~100 MB) to web/.map-dev for local dev
#   scripts/fetch-map.sh --region nordics --out DIR
#                                              the Nordic countries for the R2 bucket (~10 GB)
#
# Needs the pmtiles CLI on PATH, or Go (then it runs github.com/protomaps/go-pmtiles). Reruns skip existing assets.
set -eo pipefail
cd "$(dirname "$0")/.."

region=dev
out=web/.map-dev
while [ $# -gt 0 ]; do
  case "$1" in
    --region) region="${2:-}"; shift 2 ;;
    --out) out="${2:-}"; shift 2 ;;
    *) echo "[fetch-map] Unknown argument: $1" >&2; exit 1 ;;
  esac
done

pmtiles_version=v1.31.2
pmtiles() {
  if command -v pmtiles >/dev/null; then command pmtiles "$@"
  else go run "github.com/protomaps/go-pmtiles@$pmtiles_version" "$@"; fi
}

# Latest daily planet build, e.g. 20261102.pmtiles
build=$(curl -fsS https://build-metadata.protomaps.dev/builds.json | grep -o '"key":"[0-9]*\.pmtiles"' | tail -1 | cut -d'"' -f4)
[ -n "$build" ] || { echo "[fetch-map] Could not find the latest Protomaps build" >&2; exit 1; }
mkdir -p "$out"

case "$region" in
  dev)
    file=basemap.pmtiles
    region_args=(--bbox=17.4,59.2,18.4,59.95) ;;
  nordics)
    file="nordics-${build}"
    # Sweden, Norway, Finland, Denmark with the Baltic and North Sea coasts, Iceland, the Faroe Islands and Svalbard
    region_file=$(mktemp)
    trap 'rm -f "$region_file"' EXIT
    cat > "$region_file" <<'EOF'
{"type":"MultiPolygon","coordinates":[
  [[[4.0,54.4],[32.0,54.4],[32.0,71.5],[4.0,71.5],[4.0,54.4]]],
  [[[-24.7,63.2],[-13.2,63.2],[-13.2,66.7],[-24.7,66.7],[-24.7,63.2]]],
  [[[-7.8,61.3],[-6.1,61.3],[-6.1,62.5],[-7.8,62.5],[-7.8,61.3]]],
  [[[9.5,76.3],[34.0,76.3],[34.0,81.0],[9.5,81.0],[9.5,76.3]]]
]}
EOF
    region_args=(--region="$region_file") ;;
  *) echo "[fetch-map] Unknown region: $region (dev or nordics)" >&2; exit 1 ;;
esac

echo "[fetch-map] Extracting $region from $build to $out/$file"
pmtiles extract "https://build.protomaps.com/$build" "$out/$file" "${region_args[@]}" --maxzoom=15

# Fonts and sprites from Protomaps' basemaps-assets, served next to the tiles so no third party sees map use
assets=https://protomaps.github.io/basemaps-assets
for name in light light@2x; do
  for ext in json png; do
    mkdir -p "$out/sprites/v4"
    [ -s "$out/sprites/v4/$name.$ext" ] || curl -fsS -o "$out/sprites/v4/$name.$ext" "$assets/sprites/v4/$name.$ext"
  done
done
echo "[fetch-map] Fetching fonts"
for font in "Noto Sans Regular" "Noto Sans Medium" "Noto Sans Italic" "Noto Sans Devanagari Regular v1"; do
  mkdir -p "$out/fonts/$font"
  for start in $(seq 0 256 65280); do echo "$font/$start-$((start + 255)).pbf"; done
done | while read -r glyphs; do
  [ -s "$out/fonts/$glyphs" ] || echo "$glyphs"
done | xargs -P 8 -I{} sh -c 'curl -fsS -o "$1/fonts/$2" "$3/fonts/$(printf %s "$2" | sed "s/ /%20/g")"' _ "$out" {} "$assets"

echo "[fetch-map] Done: $out/$file"
[ "$region" = dev ] || echo "[fetch-map] Upload $out to R2, then set VITE_MAP_TILES to $file in web/vite.config.ts"
