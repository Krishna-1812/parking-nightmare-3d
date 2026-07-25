#!/bin/sh
# Parking Nightmare 3D build: syntax-check parts, concat game, stamp SW version.
# Paths are relative to this script, so the repo builds on any machine.
set -e
ROOT="$(cd "$(dirname "$0")" && pwd)"
SRC="$ROOT/src"

cat "$SRC/n3_b.js" "$SRC/n3_c.js" "$SRC/n3_d.js" "$SRC/n3_e.js" "$SRC/n3_f.js" > "$SRC/_combined.js"
node --check "$SRC/_combined.js"
{
  cat "$SRC/n3_a.html"
  echo '<script>'
  cat "$SRC/three.min.js"
  echo '</script><script>'
  cat "$SRC/_combined.js"
  echo '</script></body></html>'
} > "$ROOT/index.html"

STAMP=$(date +%Y%m%d%H%M%S)
sed "s/__BUILD__/$STAMP/" "$SRC/sw_template.js" > "$ROOT/sw.js"
echo "BUILD OK $STAMP"
