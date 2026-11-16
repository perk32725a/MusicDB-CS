#!/bin/sh
# Load a music archive (default ~/music-sample.tar.gz, made by
# k8s/music-sample.sh) into the shared CephFS volume 'music' that the app
# copies mount read-only at /music.
#
#   k8s/music-load.sh [archive.tar.gz]              add/overwrite files
#   REPLACE=1 k8s/music-load.sh [archive.tar.gz]    empty the volume first
#
# The app mounts the volume read-only, so a short-lived 'loader' pod mounts
# it with write access, unpacks the archive (streamed from the Mac through
# kubectl), and is deleted again. Then click Refresh in the app.
set -e
export KUBECONFIG="${KUBECONFIG:-$HOME/.kube/lab-k3s.yaml}"
ARCHIVE="${1:-$HOME/music-sample.tar.gz}"
K="kubectl -n musicdb"

[ -f "$ARCHIVE" ] || { echo "archive not found: $ARCHIVE (make one with k8s/music-sample.sh)"; exit 1; }
$K get pvc music >/dev/null || { echo "claim 'music' not found: kubectl apply -f k8s/15-music.yaml"; exit 1; }

cat <<'EOF' | $K apply -f - >/dev/null
apiVersion: v1
kind: Pod
metadata: {name: music-loader, labels: {app: music-loader}}
spec:
  restartPolicy: Never
  containers:
    - name: c
      image: busybox:1.37
      command: ["sleep", "600"]                  # deleted as soon as the load is done
      volumeMounts: [{name: m, mountPath: /music}]
  volumes:
    - name: m
      persistentVolumeClaim: {claimName: music}  # read-write here, unlike in the app
EOF
trap '$K delete pod music-loader --wait=false >/dev/null 2>&1 || true' EXIT
$K wait --for=condition=Ready pod/music-loader --timeout=180s >/dev/null

if [ "${REPLACE:-0}" = 1 ]; then
    echo "emptying the volume first"
    $K exec music-loader -- sh -c 'find /music -mindepth 1 -delete'
fi
echo "loading $ARCHIVE"
$K exec -i music-loader -- tar -xzf - -C /music < "$ARCHIVE"
$K exec music-loader -- chmod -R a+rX /music      # readable by the app's non-root user
echo "the volume now holds: $($K exec music-loader -- sh -c 'echo "$(find /music -type f | wc -l) files, $(ls /music | wc -l) artists:"; ls /music | sed "s/^/  /"')"
echo "done; the loader pod is being deleted."
echo "To use it: the app must mount the 'music' claim (kubectl apply -f k8s/40-app.yaml),"
echo "then click the app's own 'Refresh' link (next to 'Add New'; the browser's reload"
echo "button doesn't rescan). Check: kubectl -n musicdb logs -l app=musicdb-app --since=5m | grep Refresh"
