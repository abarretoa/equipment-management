# White-label architecture: Product Core and client profiles

Equipment Management is one product deployed separately for each
organisation that uses it. The code is split into:

- the **Product Core**: every product rule, workflow, security control and
  the document engine, shared by all deployments;
- **client profiles** (`clients/<profile>/`): the identity and content that
  differ between organisations.

One build embeds exactly one profile, and one deployment serves exactly one
organisation.

## Not multi-tenancy

A deployment is one organisation, end to end. There is deliberately **no**
`TenantId`, company switcher, cross-organisation administrator, shared
database, shared file storage or shared user base. A second organisation is a
second, fully separate deployment with its own:

- PostgreSQL database and backups;
- private file storage;
- Data Protection key ring (session and CSRF keys);
- SMTP account and sender identity;
- users and secrets;
- host name and TLS certificate.

The profile is chosen when the images are built, never at runtime, and it is
never stored in the database. No database table or migration exists for it.

## The rule

Core code never asks which client it is running for: no `if (client ==
"…")`, no `switch` on a client name, no per-client copies of a service. A
difference between organisations is expressed as:

| Kind of difference | Mechanism |
|---|---|
| A value (name, colour, amount, label) | A typed field of the profile |
| A piece of content (legal clauses, logo) | Profile data, rendered by core code |
| Behaviour that genuinely differs | A policy/strategy interface in core, chosen by a typed profile setting (none exists yet) |

`ClientIdentity.Id` exists for diagnostics only (startup log, build check). No
rule may read it.

## What belongs where

| Category | Examples | Lives in |
|---|---|---|
| Core | Lifecycle, custody, carrier dispatch and cancellation, returns, damage, maintenance, retirement, permissions, audit, term versioning and per-version snapshots, photo sets, concurrency, security, startup validation, email infrastructure (MIME, encoding, signature renderer, failure logging), PDF layout, signature block, numbering, pt-PT product wording | `src/`, `tests/` |
| Branding | Product name, short name, logo variants, favicon, brand colour tokens | `clients/<p>/profile.json`, `assets/`, `frontend/` |
| Client configuration | Organisation name used in copy, Data Protection application name, mobile operator named on documents | `clients/<p>/profile.json` |
| Client document content | Term logo, accent colour, device-form legal clauses, demonstration notice | `clients/<p>/responsibility-term.json`, `document/` |
| Client business rule | Minimum indemnity per device type (`{indemnity}` in the clauses) | `clients/<p>/responsibility-term.json` |
| Client email identity | Sender mailbox, display name, Reply-To, signature lines | Deployment configuration (`.env`), never source |
| Client deployment | Compose project name, database name, profile selection, domain, secrets | `clients/<p>/deploy/.env.example` (template) and the server's `.env` |

The document **issuer** (legal name, address, tax ID) and the email
identity are configuration of a deployment, validated at Production startup,
not profile files: they are operational facts that belong with the other
secrets and site values.

## Profile layout

```
clients/
  <profile>/
    profile.json               identity (shared by backend and frontend)
    responsibility-term.json   term content and business values
    document/                  files the API embeds (e.g. the term logo)
    assets/                    frontend-only files (logo variants, favicon.svg)
    frontend/brand.ts          BrandConfig for the UI
    frontend/brand.css         CSS token overrides (unlayered :root rules)
    deploy/.env.example        optional deployment template with fixed values
```

### `profile.json`

```json
{
  "id": "default",
  "productName": "Equipment Management",
  "shortName": "Acme",
  "productWord": "Equipment",
  "organizationName": "Acme Consulting",
  "dataProtectionApplicationName": "Equipment Management",
  "mobileCarrierName": null,
  "isDemonstration": true
}
```

- `dataProtectionApplicationName` isolates the deployment's key ring. Set it
  once; changing it signs every user out.
- `mobileCarrierName`: when set (e.g. `"Exemplo"`), the mobile number is
  labelled "Cartão Exemplo" in the UI, the term ("DESIGNAÇÃO DO TELEMÓVEL –
  CARTÃO EXEMPLO") and the import template header ("N.º de telemóvel / Cartão
  Exemplo"). When null, the generic "N.º de telemóvel" is used.
- `isDemonstration`: the profile's wording is illustrative. It must then
  print a `demonstrationNotice` on its terms, and a Production start logs a
  warning.

### `responsibility-term.json`

```json
{
  "logo": { "file": "document/logo.png", "widthCm": 3.6 },
  "accentColor": "#3D5A80",
  "demonstrationNotice": null,
  "laptop":      { "indemnity": { "amount": 1200, "amountInWords": "mil e duzentos euros" }, "clauses": ["…", "… {indemnity} …"] },
  "mobilePhone": { "indemnity": { "amount": 180,  "amountInWords": "cento e oitenta euros" }, "clauses": ["…"] }
}
```

`{indemnity}` is the only placeholder and renders as `1.200,00€ (mil e
duzentos euros)`. `logo` may be null. Unknown settings, unknown placeholders,
an indemnity without its placeholder (or the reverse), a non-image logo or an
invalid colour all fail the profile load, listing every problem.

## Selecting a profile

First match wins, identically for the backend (`Directory.Build.props`) and
the frontend (`src/frontend/clientProfile.ts`):

1. `dotnet build -p:ClientProfile=<p>` (backend only);
2. the `CLIENT_PROFILE` environment variable (also the Docker build argument);
3. the one-line file `clients/active-profile.txt`, when it exists;
4. `default`.

```bash
# Backend with the default profile
dotnet build EquipmentManagement.slnx -p:ClientProfile=default
dotnet test  EquipmentManagement.slnx -p:ClientProfile=default

# Frontend with the default profile
cd src/frontend
CLIENT_PROFILE=default npm run build
CLIENT_PROFILE=default npm test -- --run

# Docker package: CLIENT_PROFILE in deploy/production/.env
```

A build fails if the selected profile directory does not exist.

## What happens at runtime

- The API embeds `profile.json`, `responsibility-term.json` and `document/`
  of the selected profile into the Infrastructure assembly, with the profile
  name as assembly metadata. Nothing else from `clients/` reaches the API
  image.
- At startup the profile is loaded and validated; an invalid profile stops
  the API. With `ClientProfile__Expected` set (the Docker package sets it from
  `CLIENT_PROFILE`), a build of any other profile also stops it.
- The startup log names the profile. A demonstration profile in Production
  logs a warning; it is not blocked, and no security control is relaxed.
- The frontend build resolves `@client-profile` to the profile directory:
  `brand.ts` and `brand.css` are bundled, the page title comes from
  `profile.json` and `/favicon.svg` from `assets/favicon.svg`.

Changing a profile's document content affects only terms generated
afterwards. Generated PDFs are immutable bytes and keep their SHA-256, and
each version keeps its own snapshot. `TemplateVersion` identifies the core
layout; profile wording is versioned with the deployment's release.

## Creating a private client profile

This section describes how the copyright owner, or a developer working
with the copyright owner's written permission, prepares a deployment for
an organisation. It is documentation of the architecture, **not a licence
grant**: this repository's [LICENSE](../LICENSE) does not permit
third parties to deploy, modify or commercialise the product.

1. Create `clients/<new-id>/` (lower-case letters, digits, hyphens) with the
   same files as `clients/default/`. Keep it in the organisation's
   **private** repository, never in this one.
2. Edit `profile.json`: names, `dataProtectionApplicationName`,
   `mobileCarrierName`, `"isDemonstration": false`.
3. Put the organisation's **own, legally reviewed** clauses and indemnities in
   `responsibility-term.json`. Set `demonstrationNotice` to null only when the
   wording has been approved. Add `document/logo.png` (PNG or JPEG) if the
   term shows a logo.
4. Add `assets/` logos and `favicon.svg`, and adapt `frontend/brand.ts` and
   `frontend/brand.css`.
5. Optionally add `deploy/.env.example` with the organisation's fixed
   deployment values (`CLIENT_PROFILE`, `COMPOSE_PROJECT_NAME`, database
   name). Never commit secrets.
6. Add regression tests that pin the profile's content, kept with the
   private profile.
7. Build and test with `-p:ClientProfile=<new-id>` / `CLIENT_PROFILE=<new-id>`,
   then deploy a separate stack for that organisation
   ([DEPLOYMENT.md](DEPLOYMENT.md)).

If an organisation needs different *behaviour*, the change belongs in the
Product Core as a policy with a typed setting that selects it, available to
every profile, never as a branch on a client name.

> Demo Responsibility Term wording is provided only to demonstrate the
> document-generation feature. Organisations must obtain their own legal
> review and provide their own legally appropriate wording before production
> use.

## What this repository contains

This repository contains the **Product Core** and the neutral `default`
profile only. Private client profiles have the following properties:

- They are kept in separate private repositories.
- Their logos, legal wording, configuration and tests never appear here.
- They do not need any change to the Product Core.

Generic product improvements are made in the Product Core; anything specific
to one organisation stays in that organisation's profile.
