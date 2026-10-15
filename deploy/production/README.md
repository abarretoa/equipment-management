# Equipment Management — production deployment

The official way to run one client deployment of Equipment Management V1: one
Linux VPS, Docker Compose, one API instance, same-origin HTTPS. One deployment
serves exactly one organisation, with its own database, private storage, keys,
SMTP account, users and backups. Nothing is shared between deployments.

| Service | Role | Reachable from |
|---|---|---|
| `web` | Caddy: TLS, HTTP→HTTPS redirect, frontend files, reverse proxy | Internet, ports 80 and 443 |
| `api` | The application (one instance) | `web` only |
| `postgres` | PostgreSQL 18 | `api` only |
| `migrate` | Applies database migrations, then exits. Runs only when asked | — |

Routing: `/api/*` and `/health/*` go to the API; everything else is the
frontend, with unknown paths answered by the application shell so deep links
work.

**Single instance.** V1 is supported with exactly one `api` container. Do not
scale it: rate limits are held in memory per process.

## Files

| File | Purpose |
|---|---|
| `compose.yml` | The stack |
| `Dockerfile.api` | API image (target `api`) and migration image (target `migrate`) |
| `Dockerfile.web` | Frontend build served by Caddy |
| `Caddyfile` | Reverse proxy configuration |
| `.env.example` | Template for `.env`, which holds every secret and site value |

## Client profile

The organisation's identity (name, logo, colours, Responsibility Term wording,
indemnities) is a **client profile**, `clients/<profile>/` at the repository
root, compiled into both images at build time (`docs/WHITE_LABEL.md`). Choose
it with `CLIENT_PROFILE` in `.env`. Left empty, the build uses
`clients/active-profile.txt` when that file exists, otherwise the neutral
`default` demonstration profile.

A client profile may ship its own `deploy/.env.example` with that client's
fixed values (profile, compose project name, database name). Start from that
file instead of the generic one below when it exists.

With `CLIENT_PROFILE` set, the API also refuses to start if its image was
built with a different profile.

## Server prerequisites

Minimum operator setup. Keep it proportional: a few internal users.

- A Linux server (Ubuntu LTS or similar) with security updates applied
  automatically or on a regular schedule, and Docker Engine with the Compose
  plugin.
- Multi-factor authentication on the hosting provider account.
- SSH with key authentication only (password login disabled). Restrict SSH to
  known addresses where practical.
- Firewall: only ports **80** and **443** open to the internet, plus SSH.
  PostgreSQL and the API are never published; the compose file exposes neither.
- The Docker API (TCP socket) is not exposed.
- DNS: an `A`/`AAAA` record for the chosen host name pointing at the server
  **before** the first start. Caddy needs ports 80 and 443 reachable to obtain
  the certificate.
- Disk-space monitoring with an alert. Photos and PDFs accumulate in the
  `private-storage` volume and never shrink.
- Off-server backups (see [Backups](#backups)).
- Hosting in a region that fits the organisation's data-protection obligations.

## Configuration

```bash
cd deploy/production
cp .env.example .env
chmod 600 .env
```

Fill in `.env`. It is git-ignored; never commit it.

| Setting | Meaning |
|---|---|
| `CLIENT_PROFILE` | Client profile to build (see [Client profile](#client-profile)) |
| `COMPOSE_PROJECT_NAME` | Optional. Prefix of this deployment's containers, images and volumes. Default `equipment-management` |
| `APP_DOMAIN` | Public host name, e.g. `equipment.example.com`. No scheme, no path |
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | Database name, user and password. Long random password, letters and digits only |
| `Documents__Issuer__LegalName`, `…__Address`, `…__TaxId` | The organisation's legal identity printed on every Responsibility Term |
| `Email__Smtp__Host`, `…__Port`, `…__TlsMode` | Mail server. `StartTls` (587) or `SslOnConnect` (465) |
| `Email__Smtp__Username`, `…__Password` | Both, or neither |
| `Email__Smtp__FromAddress`, `…__FromDisplayName` | Sender identity: the responsible HR person's corporate mailbox and name (both required) |
| `Email__Smtp__ReplyToAddress` | Optional; empty means replies go to `FromAddress` |
| `Email__Signature__Name`, `…__Role`, `…__Company` | Signature on Consultant emails (all required) |
| `BootstrapAdmin__*` | First administrator, first deployment only |

Set by `compose.yml` itself (do not put these in `.env`):

| Setting | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__Default` | Built from `POSTGRES_*`; host `postgres` |
| `PrivateStorage__RootPath` | `/data/private-storage` (volume `private-storage`) |
| `DataProtection__KeyRingPath` | `/data/dataprotection-keys` (volume `dataprotection-keys`) |
| `App__PublicBaseUrl` | `https://<APP_DOMAIN>` |
| `AllowedHosts` | `<APP_DOMAIN>;localhost` (`localhost` is the container's own health check) |
| `ForwardedHeaders__Enabled` | `true` |
| `ForwardedHeaders__KnownProxies__0` | `172.30.84.2`, the fixed address of the `web` container |

### What the API checks at startup

In Production the API refuses to start, listing every problem in one message
(`docker compose logs api`), when:

- an issuer value is missing, blank, or still example text;
- the SMTP host, port, TLS mode or sender address is missing or malformed, or
  only one of user name and password is set;
- `App__PublicBaseUrl` is missing or not an absolute `https://` URL;
- `PrivateStorage__RootPath` or `DataProtection__KeyRingPath` is missing, not
  absolute, or not writable by the application user;
- the bootstrap administrator is enabled with the example email or password.

It does **not** contact the mail server at startup, so a mail outage never
stops the application. Failed sends are recorded in the application and
logged (see [Operations](#operations)).

### Reverse proxy facts

- **HTTP→HTTPS redirect is done by Caddy.** The API does not redirect.
- Request bodies up to **70 MB** are accepted (`request_body` in `Caddyfile`).
  The API's own largest limit is 60 MB (equipment registration and return
  receipt with photographs). Any other proxy placed in front must allow at
  least 60 MB; common defaults of 1 MB break photo uploads.
- Caddy passes the original `Host` and sets `X-Forwarded-For` and
  `X-Forwarded-Proto`, discarding any such header sent by a client. The API
  trusts those headers only from the `web` container's address. There is no
  trust-all setting.
- The internal network uses `172.30.84.0/24`. If that collides with another
  network on the host, change the subnet, the `ip_range`, the `web` address
  and `ForwardedHeaders__KnownProxies__0` together in `compose.yml`.

## Persistent volumes

| Volume | Contents | Loss means |
|---|---|---|
| `postgres-data` | The database | All records lost |
| `private-storage` | Photos, Responsibility Term PDFs, signed terms | Evidence files lost; irreplaceable |
| `dataprotection-keys` | Keys that protect the session and CSRF cookies | Everyone must sign in again; nothing else |
| `caddy-data` | TLS certificates and their keys | Caddy requests new certificates |
| `caddy-config` | Caddy's saved configuration | Nothing; rebuilt on start |

The API runs as a non-root user (uid 1654). The named volumes above are
created writable for that user automatically. If you replace a named volume
with a bind mount, give the host directory to that user first:
`chown -R 1654:1654 <directory>`.

The key ring is stored unencrypted, readable only by the application user.
Treat the volume and its backups as secret.

Never run `docker compose down -v`: `-v` deletes these volumes.

## First deployment

Run from `deploy/production` on the server, in a checkout of the release.

```bash
# 1. Build the three images from this exact source.
docker compose --profile tools build

# 2. Start the database only.
docker compose up -d postgres

# 3. Create the schema. Explicit, once per deployment.
docker compose --profile tools run --rm migrate

# 4. In .env set BootstrapAdmin__Enabled=true and the four BootstrapAdmin
#    values (see below), then start everything.
docker compose up -d

# 5. Wait until all services are healthy.
docker compose ps
curl -fsS https://<APP_DOMAIN>/health/ready
```

`migrate` exits with an error, changing nothing further, if the connection
fails or a migration fails. It is safe to run again: an up-to-date database is
left untouched. The API never migrates by itself; started against an
unmigrated database it stops with a log line pointing at this step.

### First administrator

1. In `.env`: `BootstrapAdmin__Enabled=true`, plus email, first name, last
   name, phone and a strong password (12+ characters with upper case, lower
   case and a digit). Deliver the password to that person out of band.
2. `docker compose up -d api`. The log shows
   `Bootstrap Administration user provisioned.`
3. Sign in at `https://<APP_DOMAIN>` as that administrator.
4. In `.env`: set `BootstrapAdmin__Enabled=false` and blank
   `BootstrapAdmin__Password`. Run `docker compose up -d api` again.
5. Create the other users from the application (Utilizadores). They receive a
   temporary password and must change it at first sign-in.

Leaving the bootstrap enabled is harmless (it does nothing once the account
exists), but the password should not stay in the file.

## Updating to a new release

```bash
git fetch && git checkout <release-tag>
cd deploy/production

# 1. Back up first (see Backups).
# 2. Build the new images.
docker compose --profile tools build
# 3. Stop the API. Users see the frontend but cannot work for a moment.
docker compose stop api
# 4. Apply migrations once. A release without migrations reports "already up to date".
docker compose --profile tools run --rm migrate
# 5. Start the new API and wait for it to be healthy.
docker compose up -d api
docker compose ps
# 6. Switch to the new frontend.
docker compose up -d web
# 7. Short smoke test: sign in, open an equipment item, view one photo,
#    download one Responsibility Term.
```

Users stay signed in across an update, because the session keys are on the
`dataprotection-keys` volume. A release that changes the permission map signs
everyone out once, by design.

There is no rolling upgrade. That is appropriate for a single instance and a
few users.

### Rollback

- **Release without migrations:** check out the previous tag, rebuild, and
  `docker compose up -d`.
- **Release with migrations:** restore the database and the private storage
  from the backup taken in step 1, then start the previous release. Do not
  point an older release at a newer schema.

## Backups

Nothing in the application backs itself up. The operator must schedule this.

**Backup set**

1. PostgreSQL database.
2. `private-storage` volume.
3. `dataprotection-keys` volume.
4. `.env`, kept in the operator's secret store (not in the same archive as
   the data).

**Destination: off the server.** A second directory on the same VPS is not a
backup. Copy each run to storage in another location (object storage, another
host), with access restricted and, where practical, encrypted.

**Order matters.** Dump the database first, then copy the private storage.
The application writes a file before the database row that refers to it, so
in this order every row in the dump has its file. The only side effect is a
harmless extra file now and then.

```bash
# The compose project name: COMPOSE_PROJECT_NAME from .env, or the default.
PROJECT=equipment-management
STAMP=$(date -u +%Y%m%dT%H%M%SZ)
mkdir -p /var/backups/$PROJECT/$STAMP && cd /var/backups/$PROJECT/$STAMP

# 1. Database first.
docker compose -f /path/to/deploy/production/compose.yml exec -T postgres \
  sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > db.dump

# 2. Private storage second, then the key ring.
docker run --rm -v ${PROJECT}_private-storage:/src:ro -v "$PWD":/dst alpine \
  tar czf /dst/private-storage.tgz -C /src .
docker run --rm -v ${PROJECT}_dataprotection-keys:/src:ro -v "$PWD":/dst alpine \
  tar czf /dst/dataprotection-keys.tgz -C /src .

# 3. Copy this directory OFF the server, then verify the copy arrived.
```

The database user and name are read inside the `postgres` container, so
these commands carry no credentials.

**Not specified — BUSINESS DECISION REQUIRED:** how often backups run and how
much data loss is acceptable (RPO), how long a restore may take (RTO), and how
long backups are kept. These are the operating organisation's decisions; this
document does not choose them.

### Restore

Into a new, empty environment (new server or new volumes):

```bash
cd deploy/production            # with the same .env as before
docker compose up -d postgres

# 1. Database.
docker compose exec -T postgres \
  sh -c 'pg_restore --no-owner --exit-on-error -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < db.dump

# 2. Create the API's volumes (correct ownership), then fill them.
docker compose create api
PROJECT=equipment-management   # COMPOSE_PROJECT_NAME from .env, or the default
docker run --rm -v ${PROJECT}_private-storage:/dst -v "$PWD":/b:ro alpine \
  tar xzf /b/private-storage.tgz -C /dst
docker run --rm -v ${PROJECT}_dataprotection-keys:/dst -v "$PWD":/b:ro alpine \
  tar xzf /b/dataprotection-keys.tgz -C /dst

# 3. Bring the schema up to the release being started (no-op if it matches), then start.
docker compose --profile tools run --rm migrate
docker compose up -d

# 4. Validate: /health/ready, sign in, open one photo, download one term.
```

Use the database dump and the storage archive from the same backup run. Start
the same release that produced the backup, or a newer one.

**Do a restore test on the real environment before go-live**, and repeat it
periodically. This procedure was rehearsed on disposable local infrastructure
(records, photo and term hashes, and sessions all came back); that does not
prove the production schedule or the off-server copy.

## Operations

- **Health:** `/health/live` (process is up) and `/health/ready` (database
  reachable and private storage writable). `docker compose ps` shows the same
  through the containers' health checks. Point an external uptime monitor at
  `https://<APP_DOMAIN>/health/ready`.
- **Logs:** `docker compose logs -f api`. JSON, one line per event, UTC. They
  contain no passwords, cookies, tokens or email addresses. Docker's default
  log driver keeps logs without limit: configure rotation on the host
  (`max-size` / `max-file` in `/etc/docker/daemon.json`).
- **Failed email:** one warning per failed send:
  `Email delivery failed. MessageType=… DeliveryId=… FailureCategory=… ErrorType=… CorrelationId=…`.
  `DeliveryId` matches the attempt shown in the application; `CorrelationId`
  matches the `X-Correlation-ID` response header and the audit record. The
  user can retry the send from the application.
- **Expected log lines on start:** `No XML encryptor configured` (the key ring
  is stored unencrypted on its private volume, as documented above) and one
  `Failed to determine the https port for redirect` (the health check uses
  plain HTTP inside the container; Caddy does the public redirect).
- **Restart after a server reboot:** automatic (`restart: unless-stopped`).

## What is deliberately not here

No Kubernetes, load balancer, replicas, Redis, queue, mail worker or
monitoring stack. None is needed for one instance and a few users. Revisit
only if the deployment model changes.

No multi-tenancy either: a second organisation is a second, fully separate
deployment of this package with its own client profile and `.env`.
