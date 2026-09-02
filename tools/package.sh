#!/usr/bin/env bash
# Build the WebGL player and package the Creator Console upload ZIP.
#
#   tools/package.sh
#
# The SDK's own "Minit → Build for Minit" produces Build/MinitWebGL plus a ZIP,
# but that ZIP carries only the player. The console also wants meta.json (it
# reads the draft's title, description and config from it) and the notices at
# the archive root, so this rebuilds the ZIP with those included and then
# checks the things that fail silently.
set -euo pipefail
cd "$(dirname "$0")/.."

UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity}"
OUT="Build/MinitWebGL"
ZIP="Build/minit-template-unity.zip"
LOG="Build/build.log"

if [ ! -x "$UNITY" ]; then
  echo "Unity not found at $UNITY - set UNITY=/path/to/Unity" >&2
  exit 1
fi

echo "==> regenerating assets"
node tools/gen-art.mjs   | tail -1
node tools/gen-audio.mjs | tail -2
node tools/gen-music.mjs | tail -1

echo "==> checking meta.json"
node tools/check-meta.mjs

echo "==> building (this takes a couple of minutes)"
mkdir -p Build
"$UNITY" -batchmode -nographics -projectPath "$PWD" -logFile "$LOG" \
  -executeMethod MinitGames.Editor.MinitBuild.BuildForMinit -quit
grep -E "^\[Minit\]" "$LOG" | tail -3 || true

# "Build for Minit" writes its own ZIP of the player alone. Ours supersedes it
# (it also carries meta.json and the notices), so remove it rather than leave
# two near-identical archives in Build/ for someone to upload the wrong one.
rm -f Build/*_minit.zip

# meta.json and the notices must sit at the top level of the ZIP, next to
# index.html.
cp meta.json "$OUT/meta.json"
cp THIRD-PARTY-NOTICES.txt "$OUT/THIRD-PARTY-NOTICES.txt"

echo "==> audio"
# A build that is silent inside the Minit app looks completely healthy from
# every other angle -- context running, engine mixing, buffers queued -- so this
# measures the audio graph rather than trusting any engine flag. It runs the
# built output behind a test double for the app's audio injection, with autoplay
# disabled so the context starts suspended exactly as it does in a WKWebView.
#
# It is in the packaging path deliberately: a silent build cannot be shipped.
# This template shipped that exact bug to a device before the check existed.
node tools/verify-audio.mjs "$OUT"

echo "==> packaging"
rm -f "$ZIP"
( cd "$OUT" && zip -qr "../../$ZIP" . -x '.*' -x '**/.*' )

echo "==> pre-flight"
listing=$(unzip -Z1 "$ZIP")
fail=0
for required in index.html meta.json THIRD-PARTY-NOTICES.txt; do
  grep -qx "$required" <<<"$listing" || { echo "MISSING at ZIP root: $required" >&2; fail=1; }
done
grep -qE '^Build/.*\.loader\.js$'    <<<"$listing" || { echo "MISSING: the Unity loader" >&2; fail=1; }
grep -qE '^Build/.*\.wasm(\.br|\.gz)?$' <<<"$listing" || { echo "MISSING: the wasm" >&2; fail=1; }
grep -qE '^Build/.*\.data(\.br|\.gz)?$' <<<"$listing" || { echo "MISSING: the data file" >&2; fail=1; }

# Project sources must never ride along.
forbidden=$(grep -E '(^|/)(Assets|Packages|ProjectSettings|Library|tools)/|\.cs$|\.unity$|\.meta$' <<<"$listing" || true)
if [ -n "$forbidden" ]; then
  echo "FORBIDDEN entries in ZIP:" >&2; echo "$forbidden" >&2; fail=1
fi

# The audio fixes live in the WebGL template and are the reason this template
# exists. Losing them means a game that is silent in the app and fine
# everywhere else - the hardest failure here to notice.
unzip -p "$ZIP" index.html | grep -q 'minit-audio' \
  || { echo "index.html has lost the audio context/gain recovery." >&2; fail=1; }
unzip -p "$ZIP" index.html | grep -q 'DROP-8164' \
  || { echo "index.html has lost the postMessage origin repair (DROP-8164)." >&2; fail=1; }

if unzip -p "$ZIP" index.html | grep -qE 'localStorage|sessionStorage'; then
  echo "index.html touches web storage, which the platform forbids." >&2; fail=1
fi

# Brotli with decompressionFallback off means the host MUST serve *.br with
# Content-Encoding. That is the platform's job, but a build that silently
# switched to uncompressed would quietly triple the download.
if ! grep -qE '\.br$' <<<"$listing"; then
  echo "note: no Brotli files - check compressionFormat is still Brotli." >&2
fi

size_bytes=$(stat -f%z "$ZIP")
if [ "$size_bytes" -gt 52428800 ]; then
  echo "ZIP is $(( size_bytes / 1048576 )) MB - over Minit's 50 MB hard limit." >&2; fail=1
elif [ "$size_bytes" -gt 5242880 ]; then
  echo "note: ZIP is $(( size_bytes / 1048576 )) MB - over the 5 MB recommendation." >&2
fi

[ "$fail" -eq 0 ] || { echo "pre-flight failed - not shipping this" >&2; exit 1; }

echo
echo "wrote $ZIP ($(du -h "$ZIP" | cut -f1))"
unzip -l "$ZIP" | tail -n +4 | head -12
echo
echo "Upload $ZIP at https://console.minit.games"
