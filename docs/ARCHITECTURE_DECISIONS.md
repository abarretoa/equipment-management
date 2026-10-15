# Architecture Decisions (summary)

Code comments cite design decisions as `ADR-nnn`, or as "the ADR on …", and
cite the stage of development that introduced something as "Phase n" or
"Iteration n". This page resolves those references. Each decision is reduced
to the rule it establishes and the main reason for it.

Superseded decisions are kept, marked as such, because older comments and
migrations still refer to them.

- [Development phases](#development-phases)
- [Iterations](#iterations)
- [Decisions](#decisions)

## Development phases

The product was built in phases. "Phase n scope" in a comment means the rule
was set while building that phase.

| Phase | Delivered |
|---|---|
| 0 | Solution layout, Docker Compose, PostgreSQL, EF Core migrations, Identity wiring, structured logging, test projects |
| 1 | Sign-in, internal users, roles and permissions, audit log |
| 2 | Consultants, client companies, allocations |
| 3 | Equipment types, equipment, laptop specifications, asset tags |
| 4 | Deliveries (handovers), custody (assignments), delivery inspection, photos, private file storage |
| 5 | Returns, return inspection, damage, maintenance |
| 6 | Responsibility Term generation: PDF, versions, snapshots |
| 7 | Term delivery by email, signed-copy upload, execution status; a later hardening pass of the email path |
| 8 | Excel import (preview, then confirm) and export |
| 9 | Frontend: design system, theming, every screen; a refinement pass (dashboard aggregates, list search, combobox) |
| 10 | Production hardening: forwarded headers, security headers, fail-closed authorisation, health checks, code splitting |

## Iterations

After the first complete version, the product went through review
iterations. "Iteration n" in a comment means the behaviour was added or
changed in that iteration.

| Iteration | Changes |
|---|---|
| 1 | Purchase metadata on equipment; derived operational status of a delivery; equipment history timeline; dashboard filter by equipment type |
| 2 | Draft deliveries with explicit dispatch and finalisation steps |
| 3 | Explicit delivery method (in person or carrier); delivery eligibility requires an active allocation; exactly one active allocation per consultant |
| 4 | Ending an engagement starts returns for everything in custody; return groups; manual multi-item returns |

## Decisions

### Foundation (Phase 0)

- **ADR-001: Modular monolith.** One deployable ASP.NET Core application,
  organised in Domain, Application, Infrastructure and API layers. Module
  boundaries live in code, not on the network; the system has no need for
  independent scaling or deployment.
- **ADR-002: PostgreSQL through EF Core.** Npgsql provider, versioned
  migrations only, no manual schema edits.
- **ADR-003: Cookie authentication.** ASP.NET Core Identity with `HttpOnly`,
  `Secure`, `SameSite=Strict` cookies. No JWTs and no tokens in browser
  storage, which removes token theft through XSS.
- **ADR-004: CSRF double-submit token.** `GET /api/antiforgery/token` issues a
  token pair; the frontend sends the token as `X-CSRF-TOKEN` on every
  state-changing request. Defence in depth on top of `SameSite=Strict` and a
  narrow CORS policy.
- **ADR-005: Permission policies, not role checks.** A permission vocabulary
  declared as constants, one policy per permission, and controllers that
  require permissions rather than roles.
- **ADR-006: No sign-in endpoints in the bootstrap phase.** Identity services
  and roles only; no seeded user accounts, because committed credentials are a
  secret-hygiene risk even for development.
- **ADR-007: Built-in structured logging.** `ILogger` with JSON console output
  outside development and a correlation-ID scope per request. No third-party
  logging library.
- **ADR-008: Migrations auto-apply in Development only.** Every other
  environment migrates explicitly, so a deployment can never change a database
  as a side effect of a restart.
- **ADR-009: Visual design deferred.** The first frontend proved only the
  build pipeline; a typed theme interface reserved the shape of branding
  without inventing a palette.
- **ADR-010: `.slnx` solution format.** The current, diff-friendly format
  supported by the .NET 10 tooling.

### Users, permissions and audit (Phase 1)

- **ADR-011: Role-to-permission map as code.** One static map, reconciled into
  Identity role claims on every startup.
- **ADR-012: Append-only audit log.** Safe-field snapshots in `jsonb`; a single
  write path and no update or delete surface. Entities are never serialised
  into the log, only named safe fields.
- **ADR-013: Administrator bootstrap through configuration.** A one-time,
  opt-in bootstrap that never touches an existing account. No seeded
  credentials.
- **ADR-014: Temporary passwords.** Administration issues a random temporary
  password, shown once; the user must change it before anything else works.
- **ADR-015: Per-request session validation.** A cookie validator reloads the
  user on every request and rejects inactive users or a changed security
  stamp.
- **ADR-016: Last-Administration safeguard.** A pure rule evaluated inside a
  Serializable transaction, so the last active administrator can never be
  removed, even by concurrent requests.
- **ADR-017: Internal users and consultants are separate models.** Consultants
  never sign in and share no identity type, table or interface with internal
  users.

### Consultants, companies and allocations (Phase 2)

- **ADR-018: Engagement type belongs to the allocation.** It can change over a
  consultant's career, so it is attributed to a period, not to the person.
- **ADR-019: Company deduplication by normalised name.** Trim, collapse
  whitespace, upper-case; a unique index enforces it. Deterministic, no fuzzy
  matching.
- **ADR-020: Allocation overlap rule** *(superseded by ADR-067).* Interval
  overlap checked inside a Serializable transaction.
- **ADR-021: Allocation history is immutable.** After creation only the role
  can change, and an ended allocation cannot change at all. Ending is the only
  transition.
- **ADR-022: Consultant PII in audit.** Tax ID, email, address and phone
  changes are recorded as field names only, never values. List responses omit
  these fields.
- **ADR-023: Structural tax-ID validation.** Digits only, 5 to 20 of them. No
  country-specific checksum, so foreign consultants are not rejected by an
  unconfirmed rule.
- **ADR-024: Company permissions.** HR creates and updates companies; only
  Administration archives them.
- **ADR-025: No financial fields.** The allocation is the seam a future
  finance module would attach to; no unused rate columns are added.

### Equipment (Phase 3)

- **ADR-026: Asset tags from one database sequence.** A PostgreSQL sequence
  plus the type's prefix, written once at creation and never recomputed.
  Concurrency-safe and unaffected by later prefix changes.
- **ADR-027: Type versus asset.** An equipment type's kind (generic, laptop,
  later mobile phone) is fixed at creation, because it decides which
  specification an asset requires. Manufacturer, model and serial number live
  on the asset. Kind-specific specifications are 1:0..1 extensions.
- **ADR-028: One lifecycle field.** `Status` is the only lifecycle field;
  there is no separate active flag. Transitions are explicit operations.
- **ADR-029: Equipment permissions.** HR creates and edits types and
  equipment; archiving and retiring stay with Administration.

### Delivery and custody (Phase 4)

- **ADR-030: Snapshots at finalisation.** The consultant, allocation and
  equipment data a delivery records are copied once, when it is finalised, so
  history never changes with later edits.
- **ADR-031: The assignment is the custody record.** Custody is answered only
  by the active assignment, backed by a partial unique index (one active
  assignment per equipment).
- **ADR-032: Photo minimum per kind at finalisation** *(superseded by
  ADR-079).*
- **ADR-033: Private file storage.** A narrow storage interface, server-
  generated keys, content validated by magic bytes, files never served
  statically. A failed database write removes the file just stored, so no
  orphan is left.
- **ADR-034: HR runs the whole delivery workflow,** including finalisation.
- **ADR-035: Dates validated as UTC calendar dates.** The only effect is a
  short window after midnight in time zones ahead of UTC; documented as a
  known limitation.
- **ADR-036: Conflict classification by SQLSTATE.** One classifier walks the
  whole exception chain for the PostgreSQL error code and constraint, instead
  of relying on a wrapper exception type.

### Returns, damage and maintenance (Phase 5)

- **ADR-037: Returns before the Responsibility Term.** A sequencing change
  only; no requirement changed.
- **ADR-038: A return covers one assignment,** not a whole delivery.
- **ADR-039: Physical receipt ends custody.** Starting a return does not end
  custody, and the inspection result does not either; receiving the item does.
- **ADR-040: Damage is created when the inspection is finalised,** from the
  inspection's draft fields, never as its own mutable draft.
- **ADR-041: Inactive consultants can still return equipment.** Deactivation
  blocks new custody only, never ending existing custody.
- **ADR-042: Load inside the transaction.** Rows a lifecycle operation will
  change are read after opening the Serializable transaction, so concurrent
  changes surface as conflicts.
- **ADR-043: One return per assignment,** enforced by a full unique index.
- **ADR-044: Return cancellation.** A terminal `Cancelled` state, only from
  `Pending`, which puts the equipment back in custody.

### Responsibility Terms (Phases 6 and 7)

- **ADR-045: PDF library.** PDFsharp-MigraDoc (MIT) behind a narrow generator
  interface; libraries with revenue-gated or copyleft licences were rejected.
- **ADR-046: Embedded fonts.** Liberation Sans (SIL OFL 1.1) embedded as a
  resource, so output does not depend on the host's fonts.
- **ADR-047: The term belongs to a finalised delivery** and is built only from
  that delivery's snapshots, never from live records.
- **ADR-048: Document identity.** Sequence-based document numbers
  (`TR-yyyy-nnnnnn`), a per-delivery version, the template version and the
  SHA-256 of each PDF.
- **ADR-049: Regeneration is generation.** All rendering and storage happen
  first; a short Serializable transaction then supersedes the previous version
  atomically. At most one current version exists.
- **ADR-050: Minimal status model.** `Generated` and `Superseded` only. Issuer
  configuration fails safely instead of printing made-up data.
- **ADR-051: Delivery attempts and signed copies are separate records.** Sends
  and the uploaded signed copy are their own immutable rows; the execution
  status is derived on read.
- **ADR-052: Send, then record.** The email is sent first and the attempt
  recorded afterwards; a durable "pending" attempt is written before the send,
  so a crash can never hide that a send was tried. No outbox or queue.
- **ADR-053: Signed copies are never overwritten.** One signed copy per
  version; a superseded version may still receive its own late signed copy.
- **ADR-054: SMTP through MailKit (MIT),** replacing the legacy .NET
  `SmtpClient`, behind the same sender interface.

### Excel import and export (Phase 8)

- **ADR-055: Spreadsheet library.** ClosedXML (MIT) on Microsoft's OpenXML SDK.
- **ADR-056: Preview before confirm.** An import session stores typed preview
  rows; nothing is written to business tables until confirmation.
- **ADR-057: All-or-nothing confirmation.** One transaction per batch that
  re-validates every database-sensitive fact before writing.
- **ADR-058: No implicit upsert.** Duplicates are errors; unknown companies
  or types are never created automatically; asset tags are always generated
  by the server.
- **ADR-059: Formula safety.** Formula cells are rejected on import; exported
  text is written so it can never become a formula; uploaded workbooks are not
  kept.
- **ADR-060: Bounded preview retention.** Each session stores its expiry, and
  expired previews lose their row data.

### Production hardening (Phases 9 and 10)

- **ADR-061: Forwarded headers are opt-in** and require named trusted proxies,
  otherwise startup fails.
- **ADR-062: Baseline security headers and HSTS** on every API response,
  including a strict CSP for the JSON API.
- **ADR-063: Design-time database factory** reads the same connection-string
  variable as the application.
- **ADR-064: Empty responses are payload-less success** in the frontend HTTP
  client, regardless of status code.

### Later iterations

- **ADR-065: Delivery eligibility requires an active allocation;** the
  allocation is only an eligibility gate.
- **ADR-066: Explicit delivery method** (in person or carrier). Carrier
  deliveries require a dispatch before finalisation; in-person ones can never
  be dispatched.
- **ADR-067: Exactly one active allocation per consultant,** decided by status,
  not by dates.
- **ADR-068: Ending an engagement starts returns** for every item in custody,
  in the same transaction.
- **ADR-069: Return groups** record which returns were started together; they
  carry no workflow of their own.
- **ADR-070: Workbook localisation.** Columns keep a stable internal key, a
  Portuguese header and an explicit list of accepted legacy headers. Matching
  is exact, never by similarity.
- **ADR-071: Current-inventory import** registers existing custody as real
  assignments, marked with their origin.
- **ADR-072: IMEI is unique and structurally validated** (15 digits after
  removing separators).
- **ADR-073: Own profile.** `/api/profile` acts only on the signed-in user, so
  no other account can be named; the audit records field names only.
- **ADR-074: Integration tests use a temporary database** per test run,
  guarded so that no other database can be touched.
- **ADR-075: Integration tests use temporary file storage** per run; CI runs on
  pushes to the main branch.
- **ADR-076: Untrusted correlation IDs are replaced,** and every mutation
  commits in the same transaction as its audit row.
- **ADR-077: Expired import previews are cleared on a schedule** by a
  background worker.
- **ADR-078: Cookies carry a permission-map fingerprint,** so a change to the
  role-permission map invalidates older sessions.
- **ADR-079: Condition photos belong to the equipment** as versioned photo
  sets of three. A delivery links to the set it used, so each term version
  keeps the exact evidence it was generated with.
- **ADR-080: Retirement requests.** HR requests retirement with a reason;
  Administration approves or rejects it. Direct retirement stays with
  Administration.
- **ADR-081: Derived operational status and template v2.** A finalised
  delivery is "awaiting signed term" until its current term version has a
  signed copy; the template change is versioned.
- **ADR-082: Exports filter through Excel Tables** rather than worksheet
  AutoFilter.
- **ADR-083: Product core and build-time client profiles.** Everything that
  identifies a deploying organisation (name, brand, document wording, email
  copy) lives in `clients/<profile>/`, selected at build time. See
  [WHITE_LABEL.md](WHITE_LABEL.md).
