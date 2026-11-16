#!/bin/sh
# Build a small, names-only copy of the music library for the app's Refresh.
# Refresh only reads file *names* (Artist/Album/Song), so empty placeholder
# files are enough: the archive is tiny and holds no actual music.
#
# Run on the Mac:   k8s/music-sample.sh [number of artists, default 5]
# It writes ~/music-sample.tar.gz; load it into the shared CephFS volume that
# the app copies mount with k8s/music-load.sh.
set -e
LIB="${MUSIC_DIR:-$HOME/Music/iTunes/iTunes Media/Music}"
COUNT="${1:-5}"
OUT="$HOME/music-sample.tar.gz"
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT

[ -d "$LIB" ] || { echo "music library not found: $LIB (set MUSIC_DIR)"; exit 1; }

# the first COUNT artists (folders), skipping hidden ones
ls "$LIB" | grep -v '^\.' | head -n "$COUNT" | while IFS= read -r artist; do
    [ -d "$LIB/$artist" ] || continue
    # recreate Artist/Album/... folders and empty files with the same names
    (cd "$LIB" && find "./$artist" -type d) | while IFS= read -r d; do mkdir -p "$STAGE/$d"; done
    (cd "$LIB" && find "./$artist" -type f ! -name '.*') | while IFS= read -r f; do : > "$STAGE/$f"; done
done
chmod -R a+rX "$STAGE"        # readable by the app's non-root user

tar -czf "$OUT" -C "$STAGE" .
echo "wrote $OUT: $(cd "$STAGE" && find . -type f | wc -l | tr -d ' ') songs in $(cd "$STAGE" && find . -mindepth 2 -maxdepth 2 -type d | wc -l | tr -d ' ') albums by $COUNT artists:"
ls "$STAGE" | sed 's/^/  /'
echo
echo "Load it into the app's shared music volume:  k8s/music-load.sh $OUT"
