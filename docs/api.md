# CTMS HTTP API reference

Generated from `src/CTMS.Api/Endpoints/*` and the `CLAUDE.md` "API surface"
section. All payloads are JSON; property names are camelCased on the wire
(`baseLocaleCode`, `translationKeyId`, ...). The C# DTO names are given so you
can cross-reference `src/CTMS.Application`.

- Base URL in local dev: `http://localhost:5147` (Swagger UI at `/swagger` in
  the `Development` environment). In the container / compose it is
  `http://localhost:8080`.
- **No authentication yet.** Each `/api/*` group has a `// TODO: auth` marker
  where `RequireAuthorization()` will be added (expected scheme: JWT bearer).
- IDs in the path are GUIDs (`{id:guid}` route constraint) - a non-GUID segment
  is a route miss (`404`), not a `400`.

---

## Error model - RFC 7807 ProblemDetails

Known application/domain exceptions are translated by
`ApplicationExceptionHandler` into `application/problem+json`:

| Exception | HTTP status | `title` | Extra |
|-----------|-------------|---------|-------|
| `ValidationException` | `400` | `Invalid request` | - |
| `NotFoundException` | `404` | `Resource not found` | - |
| `SlugAlreadyInUseException` | `409` | `Slug already in use` | - |
| `ConflictException` | `409` | `Conflict` | - |
| `ConcurrencyException` | `409` | `Concurrency conflict` | `extensions.currentVersion` (`long`) |
| `InvalidReviewTransitionException` | `409` | `Invalid review transition` | - |

`detail` carries the exception message. Anything not in this table is unhandled
and surfaces as a normal `500`. (The EF-era `DbUpdateConcurrencyException` branch
has been removed with the MongoDB switch - the string repository now throws
`ConcurrencyException` directly, carrying the stored `Version`.)

---

## Health

### `GET /health`
Liveness. No checks. `200 OK` with a health-report body while the process runs.

### `GET /health/ready`
Readiness. Runs the checks tagged `ready` - `MongoHealthCheck`, which issues
`{ ping: 1 }` against the configured database. `200` when ready, `503` when not.

---

## Projects

DTOs: `ProjectDto`, `CreateProjectRequest` (`src/CTMS.Application/Projects`).

```
ProjectDto            { id, name, slug, description?, baseLocaleCode, createdAt, updatedAt }
CreateProjectRequest  { name, baseLocaleCode, slug?, description? }
```

| Method & route | Body | Success | Errors |
|----------------|------|---------|--------|
| `GET /api/projects` | - | `200` `ProjectDto[]` | - |
| `GET /api/projects/{id:guid}` | - | `200` `ProjectDto` | `404` if unknown |
| `POST /api/projects` | `CreateProjectRequest` | `201` `ProjectDto` + `Location: /api/projects/{id}` | `400` validation; `409` slug already in use |

`slug` is derived from `name` (lower-cased, hyphenated) when omitted. There is no
update or delete endpoint for projects.

---

## Locales

Nested under a project. DTOs: `LocaleDto`, `CreateLocaleRequest`,
`UpdateLocaleRequest` (`src/CTMS.Application/Locales`).

```
LocaleDto            { id, projectId, code, displayName, isRtl, createdAt, updatedAt }
CreateLocaleRequest  { code, displayName, isRtl? = false }
UpdateLocaleRequest  { displayName?, isRtl? }        // omitted members unchanged
```

| Method & route | Body | Success | Errors |
|----------------|------|---------|--------|
| `GET /api/projects/{projectId:guid}/locales` | - | `200` `LocaleDto[]` | - |
| `GET /api/projects/{projectId:guid}/locales/{localeId:guid}` | - | `200` `LocaleDto` | `404` |
| `POST /api/projects/{projectId:guid}/locales` | `CreateLocaleRequest` | `201` `LocaleDto` + `Location` | `400` validation; `404` unknown project; `409` `(projectId, code)` exists |
| `PATCH /api/projects/{projectId:guid}/locales/{localeId:guid}` | `UpdateLocaleRequest` | `200` `LocaleDto` | `400` validation; `404` |
| `DELETE /api/projects/{projectId:guid}/locales/{localeId:guid}` | - | `204` | `404` |

`code` is trimmed and internal whitespace collapsed; casing preserved. `DELETE`
cascades to the locale's `TranslationString` rows (application-level cleanup).

---

## Translation keys

Nested under a project. DTOs: `TranslationKeyDto`, `CreateTranslationKeyRequest`,
`UpdateTranslationKeyRequest`, `PagedResult<T>`
(`src/CTMS.Application/Translations`, `src/CTMS.Application/Common`).

```
TranslationKeyDto            { id, projectId, keyName, description?, createdAt, updatedAt }
CreateTranslationKeyRequest  { keyName, description? }
UpdateTranslationKeyRequest  { description? }
PagedResult<T>               { items: T[], total: int }
```

| Method & route | Body / query | Success | Errors |
|----------------|--------------|---------|--------|
| `GET /api/projects/{projectId:guid}/keys?skip=0&take=50` | `skip` floored at 0; `take` default 50, capped at 200 | `200` `PagedResult<TranslationKeyDto>` | - |
| `GET /api/projects/{projectId:guid}/keys/{keyId:guid}` | - | `200` `TranslationKeyDto` | `404` |
| `POST /api/projects/{projectId:guid}/keys` | `CreateTranslationKeyRequest` | `201` `TranslationKeyDto` + `Location` | `400` validation; `404` unknown project; `409` `(projectId, keyName)` exists |
| `PATCH /api/projects/{projectId:guid}/keys/{keyId:guid}` | `UpdateTranslationKeyRequest` | `200` `TranslationKeyDto` | `404` |
| `DELETE /api/projects/{projectId:guid}/keys/{keyId:guid}` | - | `204` | `404` |

`keyName` must match `[A-Za-z0-9_.-]+` (dotted path, e.g. `checkout.button.submit`).
`DELETE` cascades to the key's `TranslationString` rows.

---

## Translation strings

One value per `(key, locale)`. DTOs: `TranslationStringDto`,
`UpsertTranslationStringRequest` (`src/CTMS.Application/Translations`).

```
TranslationStringDto            { id, translationKeyId, localeId, localeCode, value,
                                  reviewState, updatedBy?, version, createdAt, updatedAt }
UpsertTranslationStringRequest  { value, updatedBy?, expectedVersion? }
```

`reviewState` is one of `"Draft"`, `"NeedsReview"`, `"Approved"`, `"Published"`
(the `ReviewState` enum, serialized as its name). `version` is the
optimistic-concurrency token (`long`); `expectedVersion` is `long?`.

| Method & route | Body | Success | Errors |
|----------------|------|---------|--------|
| `GET /api/projects/{projectId:guid}/keys/{keyId:guid}/strings` | - | `200` `TranslationStringDto[]` (one per locale that has a value) | `404` if the key is not in the project |
| `GET /api/projects/{projectId:guid}/keys/{keyId:guid}/strings/{localeId:guid}` | - | `200` `TranslationStringDto` | `404` |
| `PUT /api/projects/{projectId:guid}/keys/{keyId:guid}/strings/{localeId:guid}` | `UpsertTranslationStringRequest` | `201` `TranslationStringDto` + `Location` when created; `200` when updated | `400` validation; `404` if key or locale not in the project; `409` version mismatch |

Behaviour:

- Upsert. First write for a `(key, locale)` creates the row in state `Draft` and
  returns `201` (audit `Created`). A subsequent write updates it, returns `200`
  (audit `Edited`).
- Editing an existing string resets `reviewState` to `NeedsReview` **unless it
  is currently `Draft`** (a draft stays a draft). This includes editing an
  `Approved` or `Published` string.
- If `expectedVersion` is supplied and does not equal the stored `version`, the
  response is `409` with `extensions.currentVersion`. The store's
  version-guarded `UpdateAsync` maps a lost race to the same `409`.

---

## Review workflow

DTO: `ReviewRequest` (`src/CTMS.Application/Translations`).

```
ReviewRequest  { action, reviewedBy }
```

### `POST /api/projects/{projectId:guid}/keys/{keyId:guid}/strings/{localeId:guid}/review`

| `action` | from -> to | audit action |
|----------|-----------|--------------|
| `submit` | `Draft` -> `NeedsReview` | `Submitted` |
| `approve` | `NeedsReview` -> `Approved` | `Approved` |
| `reject` | `NeedsReview` -> `Draft` | `Rejected` |
| `reopen` | `Approved` -> `NeedsReview`, or `Published` -> `NeedsReview` | `Reopened` |
| `publish` | `Approved` -> `Published` | `Published` |

| Outcome | Response |
|---------|----------|
| Transition applied | `200` `TranslationStringDto` (`updatedBy` = `reviewedBy`; `version` advanced); an `AuditEntry` is written |
| String / key / locale not found | `404` |
| `action` not one of the five verbs, or `reviewedBy` blank | `400` (`ValidationException` - message lists `submit`, `approve`, `reject`, `reopen`, `publish`) |
| Verb valid but illegal for the current state (e.g. `approve` on a `Draft`) | `409` (`InvalidReviewTransitionException`) |

---

## Planned endpoints

Not implemented on the current branch. The domain types, repositories and
(for audit) the read service exist; the HTTP surface and the bundle-assembly
service do not. See
[architecture.md](architecture.md#4-publishing-and-immutable-bundles).

### Published bundle delivery

`GET /api/projects/{projectId:guid}/bundles/{locale}`

- Path `{locale}` is the BCP-47 locale **code** (e.g. `fr-FR`), not a GUID -
  `TranslationBundle` stores `localeCode`, not a locale id.
- Returns the latest immutable `TranslationBundle` for `(project, locale)`.
  Shape (`TranslationBundleDto`):
  `{ id, projectId, localeCode, version, entries: { "<keyName>": "<value>" }, etag, createdBy, createdAt }`.
- Sets an `ETag` header = `"` + `dto.etag` + `"` (the DTO carries the raw
  lowercase-hex SHA-256; see `TranslationBundle.ComputeETag`).
- Conditional request: a client sending `If-None-Match: "<etag>"` that matches
  gets `304 Not Modified` with an empty body; otherwise the full `200` body plus
  the current `ETag`.
- `404` if the project/locale is unknown or nothing has been published yet.
- Optional `?version=<n>` -> `GetByVersionAsync` for a historical bundle.
- A companion publish action (project-level, or
  `POST .../bundles/{locale}`) snapshots the locale's `Published` strings into a
  new `TranslationBundle` version and writes a `Published` audit entry.
  `InsertAsync` yields `409` (`ConflictException`) if that
  `(projectId, localeCode, version)` already exists.

### History / audit trail

`GET /api/projects/{projectId:guid}/history` and/or
`GET /api/projects/{projectId:guid}/keys/{keyId}/strings/{localeId}/history`

- Backed by `AuditService`:
  - `ListByProjectAsync(projectId, skip, take)` -> `PagedResult<AuditEntryDto>`,
    newest first (`skip` floored at 0; `take` default 50, capped at 200).
  - `ListByEntityAsync(entityType, entityId)` -> `AuditEntryDto[]`, newest
    first.
- `AuditEntryDto`:
  `{ id, projectId, entityType, entityId, action, actor, timestamp, fromState?, toState?, detail? }`
  where `action` is an `AuditAction` name (`Created`, `Edited`, `Submitted`,
  `Approved`, `Rejected`, `Reopened`, `Published`) and `fromState` / `toState`
  are `ReviewState` names.
- Append-only; entries are never edited or deleted.
