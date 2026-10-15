# Product Specification (summary)

This is the functional and technical baseline the application was built
against, summarised in English. Code comments cite it as
`docs/PROJECT_SPEC.md, section N`, and the section numbers below match those
citations. "Invariant #N" in a comment refers to the numbered list in
[section 21](#21-critical-invariants).

The baseline was refined during development. Where a later decision changed a
rule, the section says so and names the decision in
[ARCHITECTURE_DECISIONS.md](ARCHITECTURE_DECISIONS.md). The current behaviour
is described in [DOMAIN_MODEL.md](DOMAIN_MODEL.md).

## 1. Purpose of the document

A single source of truth for the first version, so that frontend, backend and
database stay consistent and scope does not grow by accident. A relevant
business-rule change updates this baseline (or the decision log) together with
the implementation.

## 2. Product vision

An internal platform that tracks the lifecycle of the equipment an
organisation lends to its consultants. At any moment it must answer: what
equipment exists, what state it is in, who holds it, and what its full
custody history is. It also keeps delivery and return evidence, manages
damage, generates Responsibility Terms and keeps an administrative audit log.

## 3. Scope

In scope:

- authentication and access control;
- internal users;
- consultants, client companies and allocations (current and historical);
- equipment types and physical equipment;
- delivery of one or more items to a consultant, with inspection and photos;
- Responsibility Term generation and sending;
- returns, return inspection and photos;
- damage, maintenance and retirement;
- equipment and consultant history;
- Excel import with validation and preview, and Excel export;
- audit of critical actions.

Out of scope: finance, revenue, timesheets, invoicing, advanced digital
signatures, ERP/HRIS integrations, a native mobile app, microservices,
multi-tenancy and full maintenance management.

## 4. Access profiles and permissions

Two kinds of users sign in: **Administration** and **HR**. Consultants never
sign in; they are records managed by internal users.

Authorisation is enforced in the backend by permission policies, not by
hiding buttons. The UI reflects permissions, but the API is always the final
authority.

## 5. Profile data

- **Administration:** first name, last name, email, phone.
- **HR:** the same, plus an address.
- **Consultant:** full name, tax ID (NIF), email, address, phone, record status
  and allocation history. Engagement type, team, role and company belong to
  the **allocation**, not to the consultant, so history is preserved when they
  change.

## 6. Consultant allocation

An allocation is the consultant's professional relationship for a period:
engagement type (service provider or employment contract), team (internal or
external), role, client company when external, start date, optional end date
and status.

- An external allocation requires a company; an internal one does not.
- Only one allocation is active at a time.
  *Later refined:* exactly one Active allocation per consultant, decided by
  status rather than dates (ADR-067).
- Ended allocations are never deleted or rewritten to change history.

## 7. Equipment management

The system distinguishes an **equipment type** (laptop, monitor, mouse, …,
plus types created by users) from a **physical asset**.

Each asset has a unique, readable internal identifier (asset tag, for
example `LT-0023`), a type, manufacturer, optional model, a serial number when
applicable (unique when given), status, notes and extra specifications.
Laptops require processor, memory and storage.

## 8. Equipment states

`Available`, `Assigned`, `ReturnPending`, `UnderInspection`, `Damaged`,
`UnderMaintenance`, `Retired`. Transitions are controlled by the domain; the
frontend can never set an arbitrary state.
*Later added:* `InTransit`, for equipment shipped by carrier until delivery is
confirmed (see [DOMAIN_MODEL.md](DOMAIN_MODEL.md)); carrier delivery itself is
ADR-066.

## 9. Equipment lifecycle

Register → Available → delivery → delivery inspection → Responsibility Term →
Assigned → return started → ReturnPending → receipt → return inspection →
Available (no damage) or Damaged → UnderMaintenance → Available → eventually
Retired.

Central principle: **custody history is never lost.** Every delivery creates a
new custody record; a change of consultant never overwrites an earlier one.

## 10. Assignment and delivery

A delivery (handover) contains one or more items, with a header and one line
per item. It records the consultant, date, the user who registered it, the
items, each item's condition, notes and the initial inspection.

Laptops require three photos at delivery.
*Later refined:* condition photos belong to the equipment as versioned photo
sets, and a handover links to the set it used (ADR-079).

## 11. Automatic Responsibility Term

The Responsibility Term is tied to the delivery, not just to the consultant.
HR can generate it, or generate and send it. It is filled from a **snapshot**
of the consultant and equipment data of that delivery.

A generated PDF never changes when the consultant or equipment is edited
later, so each version stores its snapshot and its template version. Sending
requires a valid consultant email. The legal wording, logo, sender and
signature policy are deployment decisions (see
[WHITE_LABEL.md](WHITE_LABEL.md)).

## 12. Return and final inspection

A return can be started for end of contract, replacement, role change,
maintenance or another reason. It records the expected and actual dates, the
consultant, the equipment, the user who registered it and the name of the
person who physically received the equipment; **those two can be different**.

Laptops require three inspection photos. The final condition is recorded. If
there is damage, a description and at least one damage photo are required.

## 13. Damage and maintenance

Damage has the equipment, the originating inspection, a description, date,
author, photos and status, with an optional severity (minor, moderate,
severe). Supplier, cost and work-order management is out of scope, but
equipment can go through `UnderMaintenance` and back to `Available`.

## 14. Excel import and export

Bulk import from an official `.xlsx` template, through a mandatory flow:
upload → parse → validate → preview → error report → confirm → persist.
Nothing is written before validation. Duplicate tax IDs, missing required
fields, invalid dates or emails, invalid team and an external consultant
without a company must be reported. Tax IDs are normalised before comparison.

The final report counts created, updated, skipped and rejected rows, and the
operation is audited. Filtered consultant and equipment lists can be exported.

## 15. Audit

Visible to Administration only. Append-only from the application's point of
view. Each critical event stores the user, action, entity, entity ID, UTC
timestamp, old and new values when applicable, IP address when available and
the correlation ID.

Audited actions include consultant changes, deliveries and returns, equipment
status changes, term generation and sending, damage, imports, and changes to
users and permissions. Audit records cannot be edited or removed through the
application.

## 16. Deletion and history

Entities with historical relevance are never physically deleted in normal
operation. Consultants, equipment, companies and users are archived or
deactivated. Transactional records (assignments, inspections, terms, damage,
audit) are history and are kept.

## 17. Architecture

Modular monolith: React + TypeScript frontend, ASP.NET Core Web API on .NET 10,
EF Core, PostgreSQL, private file storage behind an abstraction, ASP.NET Core
Identity with secure cookies, permission policies, server-side PDF generation,
REST with OpenAPI, Docker for local development. No microservices.

## 18. Backend organisation

Domain, Application, Infrastructure and API layers, with unit and integration
test projects. The domain never depends on infrastructure, and business rules
live in the domain and services, not in controllers.

## 19. Main domain entities

Internal user, Consultant, Company, Consultant allocation, Equipment type,
Equipment, Handover and handover item, Inspection, Damage, file metadata,
Responsibility Term, Audit log and import tracking.

## 20. Essential relationships

- A consultant and a company each have many allocations.
- An equipment type has many equipment.
- A consultant has many handovers; a handover has many items.
- An equipment appears in many handover items over time, but has at most one
  active custody record.
- Inspections have photos and may produce damage; damage has photos.
- A handover has many Responsibility Term versions.
- Critical entities are referenced by audit events.

## 21. Critical invariants

1. Equipment can never be assigned to two consultants at the same time.
2. Retired equipment cannot be assigned.
3. Equipment under maintenance or damaged cannot be assigned.
4. An external consultant's allocation requires a company.
5. An inactive or archived consultant cannot receive a new assignment.
6. A laptop requires processor, memory and storage.
7. A laptop requires three photos before a delivery is completed.
8. A laptop requires three photos before a return is completed.
9. Damage requires a description and a photo.
10. Completing a delivery sets every included item to `Assigned` in the same
    transaction.
11. Completing a return sets the equipment to `Available` without damage, or to
    `Damaged` with damage.
12. A historical term keeps its snapshot; later edits never change generated
    PDFs.
13. Audit cannot be edited by the application.
14. A normalised tax ID is unique across consultants.
15. States and transitions are validated in the backend.

## 22. Security and privacy

The application handles personal data, so security is an architectural
requirement: HTTPS in production, Identity-managed passwords, `HttpOnly` /
`Secure` / `SameSite` cookies, CSRF protection, backend authorisation by
policy, rate limiting, lockout, secrets outside the repository, upload
validation with size and type limits, private files, authorised file access,
logs without tax IDs, passwords, tokens or other sensitive content, backups,
least privilege and audit logs. Authentication tokens are never stored in
`localStorage`. See [SECURITY.md](SECURITY.md) for what is implemented.

## 23. File storage

Photos and PDFs are never stored as binaries in PostgreSQL. The database keeps
only metadata: owning entity, storage key, size, content type, checksum,
author and timestamp. Storage keys are generated by the server and never
derived from the original file name. Files are private by default.

## 24. Database and performance

PostgreSQL with versioned EF Core migrations. Indexes on foreign keys and
frequently queried fields (normalised tax ID, asset tag, serial number,
status, consultant, company, dates, audit timestamp). Lists use server-side
pagination and read through projections rather than whole entity graphs.
Critical operations use transactions.

## 25. Frontend and user experience

React + TypeScript with accessible components and strict typing. An
operational, task-oriented UI covering sign-in, dashboard, consultants,
companies, equipment, deliveries, returns and inspections, imports, users and
audit. Destructive or irreversible actions ask for confirmation, and
validation errors appear next to the fields. The frontend mirrors validation
for usability; the backend stays the authority.

## 26. Testing and quality

Automated tests from the start: unit tests for rules, states and invariants;
integration tests (API + PostgreSQL) for critical flows, authentication and
authorisation, delivery, return, audit and import. Each relevant feature
covers success, denied authorisation and failed validation. CI runs build,
tests and lint.

## 27. Observability and errors

Structured logging, a correlation ID and global error handling. API errors use
a consistent format (ProblemDetails) and predictable error responses. No stack
traces in production. Unexpected errors are logged with technical context but
without personal data. Health checks cover the API and the database.

## 28. Environments and DevOps

Local development and production at minimum. Docker Compose for local
services. Git with reviewed changes, versioned migrations and CI. No real
credential is ever committed. The supported production model is described in
[DEPLOYMENT.md](DEPLOYMENT.md).

## 29. Future finance module (not implemented)

Allocation and company are kept as the foundation for a possible future
finance module (rates belong to an allocation, not to a consultant). No
financial table or UI is created.

## 30. Implementation sequence

The baseline proposed a phased sequence. The order actually delivered is
listed in
[ARCHITECTURE_DECISIONS.md, "Development phases"](ARCHITECTURE_DECISIONS.md#development-phases);
returns were built before the Responsibility Term (ADR-037).

## 31. Acceptance criteria

Administration and HR sign in with the correct permissions; only
Administration sees the audit log; HR manages and imports consultants;
companies and allocations are recorded correctly; types and equipment are
registered; available equipment can be delivered with inspection and photos;
the term is generated with correct data and can be sent; equipment is never
with two consultants at once; returns record who received the item, its
condition and photos; damage requires evidence and changes status correctly;
history stays available after new assignments; critical actions are audited;
files are private; automated tests of the critical flows pass in CI.

## 32. Deployment decisions

Left to each deploying organisation: application name and visual identity,
the final legal wording of the Responsibility Term, the email sender,
signature policy, retention of photos and documents, maximum upload sizes,
rules for foreign tax IDs, multi-factor authentication, the initial list of
equipment types and the maintenance and retirement policy.
