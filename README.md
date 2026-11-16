# MusicDB-CS

A small ASP.NET Core MVC music database (albums and songs, with search and a
"Refresh" that scans a music folder), which I first wrote in 2018 on .NET Core
2.1 with SQLite, and in 2026 rebuilt to run as several copies on Kubernetes.
It's a home lab project: the app is simple on purpose, so the interesting part
is everything around it.

The Kubernetes cluster it runs on (3 Ubuntu VMs with k3s and Ceph) is in the
companion repo [homelab-kvm-ceph-k8s](https://github.com/perk32725a/homelab-kvm-ceph-k8s).

## What changed from the 2018 version

- **.NET Core 2.1 → .NET 10**, **SQLite → MySQL** (Oracle's
  `MySql.EntityFrameworkCore`), migrations regenerated.
- **Refresh** rewritten from ~60,000 queries (5+ minutes on a real library) to
  2 queries and an in-memory comparison (under a second). The old substring
  matching had also attached songs to the wrong album and duplicated others.
- **Bugs found by testing:** song Create/Edit always returned 500 (a controller
  named `Songs` shadowed the model type); the Edit ID check could never fail
  (`id` was bound from the form before the URL; fixed with `[FromRoute]`).
- **Redis:** cache-aside for the album list, searches and album details, with
  versioned keys (a write bumps the version instead of hunting down keys); if
  Redis is down, pages come from MySQL and Redis is skipped for 30 s. Redis
  also holds the ASP.NET **Data Protection keys**, so a form loaded from one
  copy can be posted to another.
- **Ready for containers:** configuration from environment variables, logging
  to the console (`ILogger`), health endpoints `/healthz/live` and
  `/healthz/ready` (MySQL reachable), reverse-proxy and HTTPS-redirect
  settings, and `--migrate-only` so migrations run once instead of in every
  copy.

## Architecture

```
            HAProxy (round robin, health checks, no sticky sessions)
               │                      │
          app copy 1             app copy 2          (Deployment, 2 replicas,
               │  \                /  │               spread across nodes)
               │   └── Redis ─────┘   │              cache + shared keys
               └──────── MySQL ───────┘              data on a Ceph RBD volume
          /music: one shared CephFS volume, mounted read-only by every copy
```

## Running it

**With Docker Compose** (MySQL, Redis, a migrate step, two app copies behind
HAProxy):

```
cp .env.example .env          # then put real passwords in .env (never committed)
docker compose up -d --build
```

Site on http://localhost:8080, HAProxy stats on http://localhost:8404. The
music folder mounted at `/music` comes from `MUSIC_DIR` in `.env`.

**On Kubernetes** (manifests in `k8s/`, tested on k3s v1.37):

```
./make-secrets.sh             # namespace + Secret with random passwords; run once
kubectl apply -f k8s/
k8s/music-sample.sh           # optional: a names-only sample of a music library
k8s/music-load.sh             # load it into the shared music volume
```

The image has to be available to the cluster (built locally and imported on
each node, or pushed to a registry). `10-mysql.yaml` and `15-music.yaml` use
the StorageClasses `ceph-rbd` and `cephfs` from the companion repo; change
`storageClassName` to use other storage.

**Locally with `dotnet run`:** start only MySQL and Redis
(`docker compose up -d mysql redis`), set `ConnectionStrings__DefaultConnection`
(see `.env`), and put a music library in `./music` (Artist/Album/song files).

## Configuration

| Setting (environment variable) | Meaning |
| ------------------------------ | ------- |
| `ConnectionStrings__DefaultConnection` | MySQL connection string |
| `ConnectionStrings__Redis` | Redis; if empty, an in-memory cache and local keys (fine for one copy only) |
| `MusicLibrary__Path` | folder that Refresh scans, `Artist/Album/song` (default `/music`) |
| `ReverseProxy__Enabled` | trust `X-Forwarded-For`/`-Proto` from a proxy |
| `Https__Redirect` | redirect http → https (off: the proxy serves the site) |
| `Database__MigrateOnStartup` | migrate at startup (off when a separate step does it) |

## Tested

Behind HAProxy with two copies: requests alternate; deleting a pod under load
lost 0 of 134 requests; a form from one copy posts fine to the other (and
returns 400 without the shared keys); powering off a node lost 0 of 725
requests after a HAProxy DNS fix; with the database down the site returns a
clean 503 and recovers when it's back; and with MySQL on Ceph, a power cut of
MySQL's node was recovered (after fencing the node) with no data lost. Details
in the commit history and the companion repo.

## Notes

- No login or user accounts: this is a lab app, not meant for the internet.
- The page layout is still largely the 2018 scaffolding.
