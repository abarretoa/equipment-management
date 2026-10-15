# Deployment

The supported deployment model is small and deliberate: **one organisation,
one Linux server, one Docker Compose stack, one API instance**. The
step-by-step operator procedure (commands for first deployment, updates,
rollback, backup and restore) is in
[deploy/production/README.md](../deploy/production/README.md). This page
explains the model.

## Topology

```
Internet ──80/443──▶ web (Caddy) ──▶ api (ASP.NET Core, 1 instance) ──▶ postgres (PostgreSQL 18)
                        │                      │
                        │                      ├──▶ volume: private-storage
                        │                      ├──▶ volume: dataprotection-keys
                        │                      └──▶ external SMTP (TLS)
                        └── serves the SPA build (same origin as the API)
```

| Service | Role | Reachable from |
|---|---|---|
| `web` | Caddy: automatic TLS, HTTP→HTTPS redirect, static frontend, reverse proxy | Internet (80, 443) |
| `api` | The application, running as a non-root user | `web` only |
| `postgres` | PostgreSQL 18 | `api` only |
| `migrate` | One-shot EF Core migration bundle, run explicitly | — |

- **Same-origin HTTPS.** `/api/*` and `/health/*` go to the API. Every other
  path is the frontend, with the application shell as fallback so deep links
  work. Cookies stay first-party and `SameSite=Strict` works without CORS.
- **One API instance.** Rate limits are held in memory, so scaling the API
  horizontally is not supported.
- **Not exposed:** neither PostgreSQL nor the API publishes a port on the
  host.

## Persistent state

| Volume | Contents | Back up |
|---|---|---|
| `postgres-data` | The database | Yes: logical dump |
| `private-storage` | Condition photos, generated terms, signed copies | Yes |
| `dataprotection-keys` | Keys that encrypt session and CSRF cookies | Yes (losing them signs everyone out) |
| `caddy-data`, `caddy-config` | TLS certificates and Caddy state | Optional (re-issued automatically) |

Backups go **off the server**. The order is: database dump first, then
private storage, then the key ring. A restore must be tested before go-live.
Backup frequency and retention are the operator's decision.

## Configuration

All site-specific values and secrets come from `deploy/production/.env`
(git-ignored). It starts from `deploy/production/.env.example`, where every
value is empty. Example of the shape, with synthetic values only:

```dotenv
CLIENT_PROFILE=default
APP_DOMAIN=equipment.example.com

POSTGRES_DB=equipment_management
POSTGRES_USER=equipment_management
POSTGRES_PASSWORD=<long random letters and digits>

Documents__Issuer__LegalName=Acme Consulting, Lda.
Documents__Issuer__Address=Rua Exemplo 1, 1000-001 Lisboa
Documents__Issuer__TaxId=<tax id>

Email__Smtp__Host=smtp.example.com
Email__Smtp__Port=587
Email__Smtp__TlsMode=StartTls
Email__Smtp__Username=noreply@example.com
Email__Smtp__Password=<secret>
Email__Smtp__FromAddress=noreply@example.com
Email__Smtp__FromDisplayName=Equipment Management
Email__Smtp__ReplyToAddress=

Email__Signature__Name=Equipment Team
Email__Signature__Role=Human Resources
Email__Signature__Company=Acme Consulting
```

Fixed by `compose.yml`, not by the operator:

- `ASPNETCORE_ENVIRONMENT=Production`;
- the connection string;
- `PrivateStorage__RootPath` and `DataProtection__KeyRingPath`, which point
  at the volumes;
- `App__PublicBaseUrl=https://<APP_DOMAIN>` and `AllowedHosts`;
- the trusted proxy address.

### Startup validation

In Production the API refuses to start, naming every offending setting, if
any of these is missing, a placeholder or malformed:

- the document issuer;
- the SMTP settings or sender identity;
- the email signature;
- the HTTPS public URL;
- the storage or key-ring path;
- a bootstrap administrator that still uses example values.

It never contacts the SMTP server at startup, so a mail outage cannot block
it. With `CLIENT_PROFILE` set, the API also refuses an image built with a
different client profile.

### SMTP

Email goes through an external SMTP provider (`StartTls` on 587 or
`SslOnConnect` on 465).

- The sender is a real mailbox of the organisation, authenticated by the
  SMTP credentials.
- A failed send is recorded as a failed delivery and logged without personal
  data. It never undoes the business operation.

## Lifecycle of a release

1. **First deployment.**
   1. Build the images.
   2. Start PostgreSQL and run `migrate` once.
   3. Start the stack with the one-time bootstrap administrator enabled.
   4. Sign in, then disable the bootstrap.
2. **Update.**
   1. Back up.
   2. Build the new images.
   3. Stop the API and run `migrate`.
   4. Start the new API and wait for `/health/ready`.
   5. Switch the frontend.
   6. Run a short smoke test.
3. **Rollback.** Restore the previous images. If a migration ran, restore
   the backup taken before it.

Migrations are **never** applied automatically in production.

## Server prerequisites (summary)

- A supported Linux distribution with security updates, and Docker Engine
  with the Compose plugin.
- SSH key authentication; a firewall allowing only 80, 443 and SSH.
- DNS for `APP_DOMAIN` pointing at the server before the first start, so
  Caddy can obtain the certificate.
- Disk-space monitoring. Photos and PDFs accumulate and never shrink.

## Local trial of the production package

Synthetic values only:

- `APP_DOMAIN=localhost`: Caddy issues a locally trusted certificate.
- A local SMTP test server, for example Mailpit, with `TlsMode=None`. A
  warning is logged on every start.

The package still runs in Production mode, so every required value must be
supplied. There is no demo shortcut.

## Out of scope

The following are deliberately not part of the supported model:

- Kubernetes, load balancers or multiple API replicas;
- Redis, message queues or an email outbox;
- object storage;
- a bundled monitoring stack;
- multi-tenancy.
