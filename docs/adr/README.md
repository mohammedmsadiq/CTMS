# Architecture Decision Records

Nygard format — **Context**, **Decision**, **Consequences**, a **Status**, and a
date. An ADR is immutable once Accepted; to change a decision, add a new ADR and
point the old one's Status at it. The current architecture is described in
[`../architecture.md`](../architecture.md); the product spec is
[`../../CLAUDE.md`](../../CLAUDE.md).

| ADR | Title | Status |
|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | **Accepted** — stands |
| [0002](0002-mongodb-as-primary-store.md) | MongoDB as the primary datastore | **Accepted** — stands (the `TranslationString.Version` concurrency token it introduced was later removed by 0004) |
| [0003](0003-production-hardening.md) | Production hardening: CORS, rate limiting, request-size cap, persistent Data Protection, structured logging | **Accepted** — stands. The bundle-delivery route it references is now the assemble-on-demand route (0004); the rate-limit partition prefix is `delivery:` and the config key `RateLimit:BundlePermitPerWindow`. |
| [0004](0004-assemble-on-demand-delivery-and-model-simplification.md) | Assemble-on-demand delivery and model simplification | **Accepted, partly superseded** — see below |
| [0005](0005-first-run-experience-and-machine-integration.md) | First-run experience and the machine-integration surface | **Accepted, partly reverted** — see below |

The ADR files are kept for the historical record; nothing here is deleted.

## What 0004 and 0005 got right, and what has changed since

The current spec ([`../../CLAUDE.md`](../../CLAUDE.md)) and the alignment commit
`c34515a` moved past parts of 0004 and 0005. The authoritative present-day
picture is [`../architecture.md`](../architecture.md); read the ADRs for
rationale, with these corrections:

### 0004 — still stands

- **Assemble-on-demand delivery** — no stored bundles, no version numbers, a
  content-hash ETag. Unchanged. ([`../etag.md`](../etag.md))
- **Model simplification** — one global `Language` catalogue with per-project
  enablement; `Project` is a translatable application; categories on keys;
  last-write-wins string upsert; value-diff history. Unchanged.
- **The `Approved → Published` state and single-action publish.** Unchanged.

### 0004 — renamed / extended since

- **Vocabulary:** the wire term is **`project`**, not `application`
  (`/api/projects/*`, `ProjectDto.Code`, route param `project`); the shared-scope
  flag is **`IsCommon`**, not `IsShared` (the seeded project is `common`); the
  `AuditEntryDto` field is `projectId`. The `Locale` → `Language` and versioned-
  bundle removals from 0004 stand.
- **Review states** are now **`Draft / InReview / Approved / Published /
  Archived`** — `NeedsReview` was renamed `InReview` and `Archived` was added,
  with `archive` / `unarchive` review actions. ([`../translation-workflow.md`](../translation-workflow.md))
- **Roles** are the spec's `TranslationAdministrator / TranslationManager /
  TranslationReviewer / Translator / TranslationReadOnly`, not the `ctms.*`
  short names in 0004's diagrams. ([`../authorisation.md`](../authorisation.md))
- **Health** is `/health`, `/health/live`, `/health/ready`.
- **In-process consumption** is now formalised as `ITranslationService` +
  `AddTranslationServices(configuration)`. ([`../internal-consumption.md`](../internal-consumption.md))

### 0005 — the machine-integration half is REVERTED

- **API-key authentication** (`X-Api-Key`, `ApiKey` aggregate, `apiKeys`
  collection, `/api/api-keys`) is **removed**. The consumption paths are the
  in-process `ITranslationService` and the anonymous-by-default REST read;
  authenticated management access is Entra ID only.
- **Publish webhooks** (`Webhook` aggregate, `webhooks` collection,
  `/api/webhooks`, `X-CTMS-Signature`, the dispatch `BackgroundService`) are
  **removed**. Change detection is ETag + `If-None-Match` + `304`.
- **The static language catalogue** (`LanguageCatalogue`,
  `GET /api/languages/suggestions`) is **removed**. `POST /api/languages` /
  `/api/languages/bulk` accept any BCP-47 code; a picklist is the Admin UI's
  concern.

### 0005 — the first-run half stands

- **Optional `category` with prefix derivation** on key create
  (`CategorySuggestion.FromKeyName`). Unchanged.
- **`POST /api/languages/bulk`** — idempotent bulk language register. Unchanged.
- **`POST /api/projects/{project}/import`** — bulk file import. The `resx` parser
  from 0005 stays removed; `csv` was reinstated and `xlsx` added in `25f911c`
  alongside `GET /api/projects/{project}/export` and the translator work-file
  round-trip (spec §34). Formats are now `json` / `flat` / `csv` / `xlsx`, the
  table formats narrow *or* wide-multi-language.
  ([`../api.md` → Bulk import](../api.md#bulk-import),
  [`../import-export.md`](../import-export.md))
- **`POST /api/projects/{project}/review-bulk`** and
  **`GET /api/translations/publish/preview`**. Unchanged.

A future ADR (`0006`) formalising the vocabulary alignment and the 0005 reversal
would be the clean way to record this; until then, this page is the reconciliation.
