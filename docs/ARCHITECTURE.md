# Architecture

Equipment Management is a **modular monolith**: one ASP.NET Core API, one
React single-page application and one PostgreSQL database. It is deployed as
a small Docker Compose stack behind a same-origin HTTPS reverse proxy.

```
Browser ──HTTPS──▶ Caddy ─┬─ /api/*, /health/* ──▶ ASP.NET Core API ──▶ PostgreSQL 18
                          │                              │
                          └─ everything else ──▶ SPA     ├──▶ private storage (filesystem volume)
                             (static build)              ├──▶ Data Protection key ring (volume)
                                                         └──▶ external SMTP server
```

## Repository layout

| Path | Contents |
|---|---|
| `src/backend/EquipmentManagement.Domain` | Entities, enums and pure rule classes. No framework dependency. |
| `src/backend/EquipmentManagement.Application` | Application-level service registration. |
| `src/backend/EquipmentManagement.Infrastructure` | EF Core persistence and migrations, domain services, identity, authorisation, files, email, PDF, Excel, client-profile loading, hosting checks. |
| `src/backend/EquipmentManagement.Api` | Controllers, request/response contracts, filters (CSRF), middleware, error handling, `Program.cs`. |
| `src/frontend` | React 19 + TypeScript SPA (Vite). |
| `tests/EquipmentManagement.UnitTests` | Domain rules, document rendering, email/MIME, import validation, configuration validation. |
| `tests/EquipmentManagement.IntegrationTests` | The API over HTTP against a real PostgreSQL database. |
| `clients/default` | The neutral demonstration client profile. |
| `deploy/production` | The Docker Compose deployment package. |

## Backend layers

- **Domain.** Lifecycle statuses, rule classes (for example
  `EquipmentLifecycleRules`, `HandoverRules`, `ReturnRules`,
  `RolePermissions`), entities and value types. The rules are plain C# and
  are unit-tested without a database.
- **Infrastructure.** One service per area (equipment, handovers and
  dispatch, Responsibility Terms, returns, maintenance, retirement, users,
  imports and exports). Every state-changing operation:
  - re-loads what it decides on inside a database transaction (Serializable
    for lifecycle changes);
  - re-checks the domain rules there;
  - writes the change and its audit row in the same transaction.

  Services return typed results instead of throwing for expected outcomes;
  the API maps them to HTTP status codes.
- **Api.** Thin controllers. Every action declares its permission policy.
  Mutating actions also require the CSRF token. Errors are returned as
  ProblemDetails, never stack traces.

## Persistence

- PostgreSQL 18 through EF Core 10 and Npgsql, with code-first migrations
  in `Infrastructure/Persistence/Migrations`.
- Migrations are applied automatically in Development only. In production
  they run as an explicit, one-shot step (an EF migration bundle, see
  [DEPLOYMENT.md](DEPLOYMENT.md)).
- Invariants are backed by the database, not only by code:
  - partial unique indexes: one active assignment per equipment, one current
    photo set per equipment, one current term per handover, one active
    dispatch per handover, and others;
  - check constraints;
  - `RESTRICT` foreign keys on evidence rows.
- Unique-constraint and serialization conflicts are classified and returned
  as **409 Conflict**, never as 500.

## Private storage

Photos, generated Responsibility Terms and signed copies are stored on the
local filesystem under a configured root. In production this root is a Docker
volume.

- Files are **never served statically**. Only authorised API endpoints
  stream them, after checking the caller's permission and the file's
  ownership chain.
- Storage keys are random; the original file name is metadata only.
- Uploads are validated by magic bytes and size before being stored.
- Write order is chosen so that a failure never leaves a database row
  pointing at a missing file:
  - an upload saves the file first and deletes it if the database write
    fails;
  - a delete removes the row first, then the file.

## Authentication and sessions

- ASP.NET Core Identity (`AddIdentityCore`) with **cookie authentication**.
  There are no bearer tokens or JWTs.
- The auth cookie is HttpOnly, Secure and `SameSite=Strict`, with an 8-hour
  sliding lifetime.
- **Every request** re-validates:
  - that the user exists and is active;
  - their security stamp;
  - a fingerprint of the role-permission map.

  Deactivation, a role change, a password change or a logout therefore
  takes effect on the next request.
- **CSRF:** a double-submit antiforgery token (cookie plus `X-CSRF-TOKEN`
  header) is required on every mutating endpoint, including login. It is
  re-issued after the login transition.
- The keys that protect the cookies (ASP.NET Core Data Protection) are
  persisted to a configured directory under an application name taken from
  the client profile, so sessions survive a redeploy.

Details are in [SECURITY.md](SECURITY.md).

## Frontend

- React 19, TypeScript, Vite and Tailwind CSS 4.
- TanStack Query for server state, React Router for routing, and
  react-hook-form + zod for forms.
- Organised by feature (`src/features/<area>`), with a shared component
  library and design tokens in `src/shared` and `src/app`.
- The SPA calls the API on the same origin. It never stores tokens: the
  browser sends the HttpOnly cookie, and the SPA adds the CSRF header to
  mutations.
- All user data is rendered as React text; there is no raw-HTML rendering.
- The client profile is resolved at build time through the
  `@client-profile` alias: brand configuration, CSS tokens, title and
  favicon.

## Documents, spreadsheets and email

- **PDF:** Responsibility Terms are rendered with PDFsharp-MigraDoc and
  embedded Liberation Sans fonts. The output is deterministic and does not
  depend on the server's locale or installed fonts.
- **Excel:** ClosedXML builds import templates (with in-cell dropdowns) and
  exports. Imports parse `.xlsx` only and never evaluate formulas. Exports
  write every user value as text, so it can never become a formula.
- **Email:** MailKit/MimeKit over SMTP. Bodies are plain text; messages are
  serialised 7-bit clean (RFC 2047 headers, quoted-printable UTF-8 bodies).
  - The sender identity, Reply-To and signature are deployment
    configuration.
  - A durable delivery-attempt row is committed before each send.
  - An email failure never rolls back the business transaction.

## Deployment-profile model and client isolation

The product is **white-label**. Organisation-specific identity and content
(names, logos, colours, Responsibility Term clauses and amounts) live in a
**client profile**, `clients/<profile>/`. The profile is selected and
compiled in **at build time**; see [WHITE_LABEL.md](WHITE_LABEL.md).

**One deployment serves exactly one organisation.** This is **not** database
multi-tenancy:

- there is no tenant identifier in the schema;
- there is no company switcher and no cross-organisation administrator;
- databases, file storage, keys, SMTP accounts and users are never shared.

A second organisation is a second, fully separate deployment, with its own:

- database and backups;
- private storage;
- Data Protection key ring;
- SMTP account;
- users and secrets;
- host name.

Isolation is therefore physical, not a filter in queries.

## Operational endpoints

- `/health/live`: the process is running.
- `/health/ready`: the database is reachable and private storage is
  writable.

Every request carries a correlation ID, which is returned to the client and
stored on audit rows.

## Notes for reviewers

- Code comments cite design decisions (`ADR-nnn`), development stages
  ("Phase n", "Iteration n") and sections of the product specification
  (`docs/PROJECT_SPEC.md, section N`). They are resolved by
  [ARCHITECTURE_DECISIONS.md](ARCHITECTURE_DECISIONS.md) and
  [PROJECT_SPEC.md](PROJECT_SPEC.md).
- The user interface, generated documents and emails are in Portuguese
  (pt-PT). Code, comments and documentation are in English.
