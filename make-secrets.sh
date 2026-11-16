#!/bin/sh
# Run once per cluster: MySQL keeps the passwords it was first created with,
# so new ones later would no longer match. A second run stops at
# "namespace already exists" (set -e).
set -e
export KUBECONFIG="${KUBECONFIG:-$HOME/.kube/lab-k3s.yaml}"
# set up some passwords
P=$(openssl rand -hex 16); R=$(openssl rand -hex 16)
kubectl create namespace musicdb
kubectl -n musicdb create secret generic musicdb-db \
    --from-literal=MYSQL_PASSWORD="$P" \
    --from-literal=MYSQL_ROOT_PASSWORD="$R" \
    --from-literal=ConnectionStrings__DefaultConnection="server=mysql;port=3306;database=MusicDB;user=musicdb;password=$P"
unset P R

# EOF:
