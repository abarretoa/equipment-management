# Domain model

The rules below are enforced on the server, mostly in the framework-free
rule classes of `EquipmentManagement.Domain`, and re-checked inside the
transaction of every operation. Where this document and the code disagree,
the code and its tests are authoritative.

## Vocabulary

| Term (UI label, pt-PT) | Meaning |
|---|---|
| **User** (Utilizador) | An internal operator with a login. Two roles: **Administration** and **HR**. |
| **Consultant** (Consultor) | A person who receives equipment. A domain record, **never** an application user: no login, no credentials. |
| **Company** (Empresa / Cliente) | The client company of an external engagement. |
| **Allocation** (Alocação) | A Consultant's engagement period. It has a workforce type (`Internal` / `External`; external requires a Company, internal forbids one), an engagement type, a role, and start/end dates. |
| **Equipment type** | A kind of equipment with an asset-tag prefix. Its kind is `Generic`, `Laptop` or `MobilePhone`. |
| **Equipment** | One physical asset with a unique asset tag. Serial numbers and IMEIs are normalised and unique. |
| **Handover** (Entrega) | A delivery of one or more items to one Consultant, in person or by carrier. |
| **Dispatch** (Registar envio) | The shipping event of a carrier Handover. |
| **Assignment** (Atribuição) | An `EquipmentAssignment`, **the only record of custody**. |
| **Responsibility Term** (Termo de Responsabilidade) | A versioned PDF for a Handover, sent to the Consultant and returned signed. |
| **Photo set** | The condition photos of one piece of equipment at one point in time. |
| **Return** (Devolução) / **Return group** | Equipment coming back; a group bundles the returns started together. |
| **Damage**, **Maintenance**, **Retirement request** (Solicitação de abate) | The after-return workflows. |

## Equipment lifecycle

Statuses: `Available`, `InTransit`, `Assigned`, `ReturnPending`,
`UnderInspection`, `Damaged`, `UnderMaintenance`, `Retired`.

```
Available ──(confirm in-person delivery)──────────────────────▶ Assigned
Available ──(dispatch)──▶ InTransit ──(confirm delivery)──────▶ Assigned
                          InTransit ──(cancel dispatch)───────▶ Available

Assigned ──(return initiated)──▶ ReturnPending ──(return cancelled)──▶ Assigned
ReturnPending ──(physical receipt)──▶ UnderInspection
UnderInspection ──(inspection finalised)──▶ Available | Damaged
Damaged ──(maintenance started)──▶ UnderMaintenance
UnderMaintenance ──(maintenance completed)──▶ Available (repaired) | Damaged (not repairable)
Available | Damaged ──(retirement, Administration only)──▶ Retired
```

| Transition | Trigger |
|---|---|
| (new) → Available | Registration. Laptops and mobile phones need exactly 3 condition photos. |
| Available → InTransit | Dispatch of a carrier Handover. |
| InTransit → Assigned | Delivery confirmation of that carrier Handover. |
| InTransit → Available | Cancellation of that dispatch, before delivery is confirmed. |
| Available → Assigned | Confirmation of an in-person Handover, or the current-inventory import. |
| Assigned → ReturnPending | A return is initiated (manually, or by ending an engagement). |
| ReturnPending → Assigned | The return is cancelled (only while pending). |
| ReturnPending → UnderInspection | The return is physically received. **Custody ends here.** |
| UnderInspection → Available / Damaged | The return inspection is finalised; damage evidence leads to Damaged. |
| Damaged → UnderMaintenance | Maintenance starts. |
| UnderMaintenance → Available / Damaged | Maintenance completes as Repaired / NotRepairable. |
| Available / Damaged → Retired | Retirement by Administration, directly or by approving a request. |

Rules:

- Only `Available` equipment can be added to a Handover.
- **Adding equipment to a draft Handover does not change its status.**
  Several drafts may contain the same item. The first draft that commits it
  wins (by dispatch, or by confirming an in-person delivery); the others are
  rejected with a 409 and show the item as no longer available.
- Retired equipment is never assigned again. Damaged or under-maintenance
  equipment cannot be handed over. Retirement is never automatic.

## Custody

**Current custody derives only from an active `EquipmentAssignment`.**

- An assignment is created only when a Handover's delivery is confirmed (or
  by the current-inventory import, which records existing custody without
  inventing a Handover or a term).
- It is ended only at the physical receipt of a return.
- At most one active assignment exists per equipment (partial unique index).

## In transit is not custody

`InTransit` is an **operational, logistics state**: the equipment was shipped
on a carrier Handover and its receipt is not yet confirmed.

- No assignment exists while equipment is in transit. The Consultant does
  not hold it yet.
- In-transit equipment is also not available. It cannot be:
  - added to another Handover or dispatched again;
  - delivered in person;
  - retired or requested for retirement;
  - sent to maintenance;
  - photo-corrected.
- It leaves `InTransit` only through the Handover that shipped it: delivery
  confirmation (→ `Assigned`) or dispatch cancellation (→ `Available`).

## Handover lifecycle

The persisted status is `Draft` → `Finalized` (one-way). The status shown to
users is derived and never stored:

- `Draft`;
- `AwaitingSignedTerm`: finalised, but the current term is missing or
  unsigned;
- `Completed`: finalised, and the current term has a signed copy.

| Step | In person | Carrier |
|---|---|---|
| Draft (items, per-item inspections) | equipment Available | equipment Available |
| Dispatch | — | equipment **InTransit**, no assignment, composition locked |
| Confirm delivery | equipment Assigned, **assignment created** | equipment Assigned, **assignment created** |

Draft and finalisation rules:

- A draft may be deleted only while it has no dispatch history, no
  assignment and no Responsibility Term.
- Confirming delivery needs a delivery date that is not in the future, an
  inspection per item, an active Consultant with exactly one active
  allocation, and every item in the expected status.
- Confirming delivery freezes the Handover's **delivery record**: the
  Consultant, allocation and equipment snapshots, and the condition-photo
  set of each laptop or phone.
- A finalised Handover is immutable.

**Cancel dispatch** ("Anular envio") is available to HR and Administration
through a dedicated permission. A reason is mandatory (up to 500
characters).

- **Allowed only** while all of these hold: the Handover is still a draft,
  every item is still in transit, no assignment exists and no term has been
  signed.
- **Effect, in one Serializable transaction:**
  - every item returns to `Available`;
  - the dispatch row is kept and marked cancelled (who, when, why);
  - a term prepared for that dispatch is superseded.
- The draft may then be corrected and dispatched again, which creates a new
  dispatch row.
- Once delivery is confirmed, the only way back is a return.

## Responsibility Terms

- **Versions.** A Handover has at most one current (`Generated`) term
  version. Regenerating supersedes the previous one. Superseded versions
  are terminal and kept forever.
- **When a term can be generated:**
  - for a finalised Handover;
  - for a carrier draft with an active dispatch, so it can travel with the
    equipment. Such a term cannot be sent or signed until delivery is
    confirmed.
- **Per-version immutable snapshot.** Each version stores, in the same
  transaction that creates it, the exact data its PDF was rendered from:
  - a Consultant snapshot and an allocation snapshot;
  - one row per item with an equipment snapshot and the exact photo set
    rendered.

  The PDF is rendered from that snapshot. A later version gets its own
  snapshot and never modifies an earlier one. Supersession, dispatch
  cancellation, delivery confirmation and signing do not change it, and
  restrictive foreign keys protect the referenced photo sets.
- **Evidence.** PDF bytes and their SHA-256 never change.
- **Sending and signing.**
  - Sending reads the recipient and equipment lines from the version's own
    snapshot.
  - A durable attempt row is committed before the email is sent.
  - The signed copy (PDF only) is uploaded once per version.
  - **Only a signed current version completes the Handover.**
- **Layout.**
  - The term has one final Consultant signature block and one blank
    reception-date field, whatever its length or composition.
  - The device-specific clauses, indemnity amounts, logo and accent colour
    come from the client profile ([WHITE_LABEL.md](WHITE_LABEL.md)).

> Demo Responsibility Term wording is provided only to demonstrate the
> document-generation feature. Organisations must obtain their own legal
> review and provide their own legally appropriate wording before production
> use.

## Condition-photo sets

- Photo sets belong to the equipment, not to a Handover.
- Each piece of equipment has at most one `Current` set; `Historical` sets
  never change again.
- Laptops and mobile phones always have **exactly three** current photos.
  Generic equipment needs none.
- A new current set is created:
  - at registration;
  - at the physical receipt of a return;
  - by an administrative correction (Administration only). A correction is
    allowed only when the equipment is not in custody, not in transit or
    awaiting delivery, and not retired. It copies the photos it does not
    replace.
- Handover delivery records and every term version stay linked to the exact
  set they rendered.
- Delivery-inspection, return-inspection and damage photos are separate
  evidence attached to their workflows.

## Returns, damage and maintenance

- Return status: `Pending` → `Received` → `Completed`, or `Pending` →
  `Cancelled`.
- A return can be started only for assigned equipment, with at most one
  open return per assignment.
- **Ending an engagement.** It requires an explicit choice of whether the
  engagement has ended. When it has:
  - every item the Consultant holds gets a return, in one atomic operation
    with the allocation change;
  - the returns are grouped;
  - one consolidated collection email is sent after the commit.
- **Physical receipt** ends the assignment, moves the equipment to
  `UnderInspection` and records the new photo set.
- **Inspection.** It records the condition. Damage requires a description
  and at least one damage photo. Finalising creates an open `Damage` record
  and sets `Damaged`, or sets `Available`.
- **Maintenance** starts only for damaged equipment, with at most one active
  record per equipment. It ends as `Repaired` (→ Available) or
  `NotRepairable` (→ Damaged).

## Retirement

- Only Administration retires equipment, and only `Available` or `Damaged`
  equipment.
- HR may **request** retirement, with at most one pending request per
  equipment.
- Administration approves or rejects the request. Approval retires the
  equipment in the same transaction.
- New requests notify the active Administration users by email.

## Roles and permissions

- Permissions are claims mapped from roles in one place
  (`RolePermissions`). Every endpoint declares its permission, and a
  fail-closed fallback policy requires authentication everywhere else.
- **Administration only:**
  - user management and audit reading;
  - archiving companies and equipment types;
  - retirement and retirement-request decisions;
  - administrative photo correction.
- **HR and Administration:** the operational workflow: Consultants and
  allocations, import and export, equipment registration and updates,
  Handovers (including dispatch, cancel dispatch and delivery
  confirmation), Responsibility Terms, returns and maintenance.
- The last active Administration user cannot be deactivated or demoted.

## Concurrency

- Lifecycle operations re-read their state inside a **Serializable**
  transaction.
- Database constraints are the final safeguard: partial unique indexes,
  check constraints and `RESTRICT` foreign keys.
- Races resolve to exactly one winner. The loser receives **409 Conflict**
  with nothing changed. This applies, for example, to:
  - two dispatches of the same item;
  - a dispatch against an in-person confirmation;
  - a cancellation against a confirmation.

## Invariants

1. Current custody derives exclusively from an active `EquipmentAssignment`.
2. An assignment is created only at delivery confirmation or by the
   current-inventory import.
3. A draft never changes equipment status; `InTransit` is never custody.
4. Finalised Handovers, superseded terms, historical photo sets, PDFs and
   signed copies are immutable evidence.
5. Only a signed **current** term completes a Handover.
6. A term prepared after dispatch is never sent or signed before delivery
   is confirmed.
7. Custody ends at the physical receipt of a return.
8. Laptops and phones always have exactly three current condition photos.
9. Email is communication: its failure never rolls back the business
   transaction.
10. Consultants are never users.
11. Cancelling a dispatch never touches custody.
