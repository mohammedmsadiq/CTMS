# Database

**MongoDB** is the single source of truth for translations (spec §4, §31–§32).
Only CTMS accesses these collections — internal microservices go through
`ITranslationService`, external apps through the REST API.

- Driver: `MongoDB.Driver`.
- Connection string: `ConnectionStrings:CtmsDatabase`
  (`ConnectionStrings__CtmsDatabase`). Required — startup throws without it.
- Database name: `Mongo:Database` (`Mongo__Database`), default **`ctms`**.
- Context: `CtmsMongoContext` (`IMongoContext`), registered as a singleton by
  `AddInfrastructure`.

---

## Collections

Names are constants on `CtmsMongoContext`. Five collections — **no**
`translationBundles`, `apiKeys`, or `webhooks`.

### `projects` — `Project`

| Field | Type | Notes |
|---|---|---|
| `_id` | UUID | `Id` (standard UUID BSON subtype) |
| `name` | string | non-blank, trimmed |
| `slug` | string | **unique**, lower-cased, trimmed — the project **code** on every route |
| `description` | string? | null when blank |
| `baseLanguageCode` | string | BCP-47; the language source strings are authored in |
| `isCommon` | bool | a `common` project's published strings merge into every project's bundle |
| `active` | bool | inactive projects are hidden from delivery |
| `enabledLanguageCodes` | string[] | BCP-47 codes; ordinal, de-duplicated |
| `createdAt` / `updatedAt` | date | stamped by the repository on write |

### `languages` — `Language` (global)

| Field | Type | Notes |
|---|---|---|
| `_id` | UUID | `Id` |
| `code` | string | **unique** across CTMS; BCP-47; trimmed, casing preserved |
| `name` | string | non-blank |
| `fallbackCode` | string? | another language's `code`; must not equal this one's `code` |
| `isRtl` | bool | right-to-left script (e.g. `ar-AE`) |
| `active` | bool | inactive languages are hidden from delivery and rejected by the assembler |
| `createdAt` / `updatedAt` | date | |

### `translationKeys` — `TranslationKey`

| Field | Type | Notes |
|---|---|---|
| `_id` | UUID | `Id` |
| `projectId` | UUID | owning project |
| `keyName` | string | dotted path, matches `[A-Za-z0-9_.-]+`; unique within a project |
| `category` | string | non-blank; derived from the key-name prefix when the caller omits it |
| `description` | string? | |
| `active` | bool | inactive keys are excluded from delivery and coverage |
| `createdBy` | string | actor who created the key |
| `createdAt` / `updatedAt` | date | |

### `translationStrings` — `TranslationString`

| Field | Type | Notes |
|---|---|---|
| `_id` | UUID | `Id` |
| `translationKeyId` | UUID | owning key |
| `languageCode` | string | BCP-47; a bare string — not tied to a `languages` row at the DB level |
| `value` | string | |
| `reviewState` | string | enum name: `Draft` / `InReview` / `Approved` / `Published` / `Archived` |
| `updatedBy` | string | last actor (token identity in a deployed environment) |
| `createdAt` / `updatedAt` | date | |

**No `version` / concurrency field** — last write wins (spec §27).

### `auditEntries` — `AuditEntry` (append-only)

| Field | Type | Notes |
|---|---|---|
| `_id` | UUID | `Id` |
| `projectId` | UUID | owning project |
| `entityType` | string | e.g. `"TranslationString"` |
| `entityId` | UUID | the audited entity |
| `action` | string | `Created` / `Edited` / `Submitted` / `Approved` / `Rejected` / `Reopened` / `Published` / `Archived` / `Unarchived` |
| `actor` | string | |
| `timestamp` | date (UTC) | the only time field — no `createdAt` / `updatedAt` |
| `fromState` / `toState` | string? | `ReviewState` names on a review transition |
| `detail` | string? | free-form context |
| `oldValue` / `newValue` | string? | value diff — `newValue` on `Created`, both on `Edited`, both null on a review transition |

Never updated or deleted.

## Indexes

All created by `MongoIndexInitializer` (an `IHostedService`) on **every startup**
via `EnsureIndexesAsync`. `createIndexes` is idempotent — an already-present
index with the same key and options is a no-op.

| Collection | Index | Unique | Backs |
|---|---|:--:|---|
| `languages` | `{ code: 1 }` | ✓ | language lookup; the global catalogue |
| `projects` | `{ slug: 1 }` | ✓ | project-by-code lookup |
| `translationKeys` | `{ projectId: 1, keyName: 1 }` | ✓ | uniqueness of a key within a project |
| `translationKeys` | `{ projectId: 1, category: 1 }` | | category filtering |
| `translationStrings` | `{ translationKeyId: 1, languageCode: 1 }` | ✓ | one value per `(key, language)` |
| `translationStrings` | `{ translationKeyId: 1, reviewState: 1, updatedAt: -1 }` | | the project-wide review-state listing, newest-updated first |
| `auditEntries` | `{ projectId: 1, timestamp: 1 }` | | a project's audit feed |
| `auditEntries` | `{ entityType: 1, entityId: 1, timestamp: 1 }` | | one entity's history |

The unique indexes carry the constraints a relational schema's foreign keys and
unique indexes used to enforce. **Referential integrity the database does not
guarantee** — "a key's project exists", cascade-delete of a key's strings, "a
`languageCode` names a real, enabled language" — is enforced in the application
services and by explicit multi-collection cleanup in the repositories.

## BSON mapping

`MongoMappingRegistration.Register()` (idempotent, called during wiring):

- GUIDs stored as the standard UUID BSON subtype **everywhere, including `_id`**.
- Conventions applied to every `CTMS.*` type: **camelCase** element names,
  **`IgnoreExtraElements`** (tolerate unknown fields — additive schema
  evolution), **enums as strings** (`ReviewState`, `AuditAction`).
- `AuditEntry` auto-maps despite not deriving from `Entity`.

## Seeder

`DataSeeder` (an `IHostedService`) runs **only** when
`ASPNETCORE_ENVIRONMENT=Development` **and** `Seed:Enabled=true`, and is
idempotent (it does nothing if the `common` project already exists). It seeds:

- **Languages**: `en-GB`, `fr-FR` (→ `en-GB`), `fr-CA` (→ `fr-FR`), `de-DE`,
  `es-ES`, `ar-AE` (RTL), `it-IT` — with the reference fallback chain
  `fr-CA → fr-FR → en-GB`.
- **`common`** project (`isCommon: true`) with keys `common.save`,
  `common.cancel`, `common.delete` (mostly `Published`), and `common.legacy`
  (`Archived`).
- **`icoach`** sample project with `course.*` / `nav.*` keys across
  `Draft` / `Approved` / `Published` states, a project-level `common.cancel`
  override (`"Exit course"`, demonstrating spec §22), and `course.retired`
  (`Archived`).

## No migration tool

There is **no** EF Core, no `dotnet ef`, no migration files. Schema shape is
managed by:

- additive, unknown-field-tolerant BSON mapping (`IgnoreExtraElements`);
- idempotent index creation on startup;
- one-off backfill commands (mongosh / a script) when a rewrite is unavoidable.

`NoOpUnitOfWork.SaveChangesAsync` does nothing — every repository call is a
single-document atomic write, already durable when it returns. A publish that
updates many strings and writes many audit entries spans documents
non-transactionally and relies on operation ordering and idempotency.

`MongoWriteExceptions.IsDuplicateKey` recognises E11000; repositories translate
it into `ConflictException` / `SlugAlreadyInUseException`.
