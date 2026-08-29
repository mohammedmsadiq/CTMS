# CTMS HTTP API reference

Generated from `src/CTMS.Api/Endpoints/*` and the `CLAUDE.md` "API surface"
section. All payloads are JSON; property names are camelCased on the wire
(`baseLocaleCode`, `translationKeyId`, ...). The C# DTO names are given so you
can cross-reference `src/CTMS.Application`.

- Base URL in local dev: `http://localhost:5147` (Swagger UI at `/swagger` in
  the `Development` environment). In the container / compose it is
  `http://localhost:8080`.
- **Authentication is required** (Microsoft Entra ID, JWT bearer). Every `/api/*`
  endpoint carries an authorization policy; see
  [Authentication & authorization](#authentication--authorization). `/health`,
  `/health/ready` and Swagger are anonymous, and the bundle **delivery** GET
  routes are anonymous by default (`Auth:PublicBundleReads`).
- A local-dev / test escape hatch (`Auth:Enabled=false`) authenticates every
  request as a synthetic all-roles principal so `dotnet run` and the test suite
  work with no identity provider. It is refused under `Production`.
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

## Authentication & authorization

### Bearer requirement

The API authenticates **Microsoft Entra ID** access tokens as JWT bearer
(`Authorization: Bearer <token>`), wired with `Microsoft.Identity.Web`
(`AddMicrosoftIdentityWebApi`, config section `AzureAd`). A request with no / an
invalid token to a protected endpoint gets `401`. An authenticated caller whose
token carries **no recognised role** gets `403` on every `/api/*` endpoint
(there is no implicit read access).

Roles come from the token's `roles` claim (Entra **app roles**):

| Role | Intended for | Grants |
|------|--------------|--------|
| `ctms.admin` | Administrators | Everything, incl. create projects |
| `ctms.manager` | Project managers | Manage locales & keys, publish bundles, + all reviewer/translator rights |
| `ctms.reviewer` | Reviewers | Review transitions (approve/reject/reopen/publish action), edit strings, read |
| `ctms.translator` | Translators | Create/edit string values, submit for review, read |
| `ctms.reader` | Read-only clients | Every GET |

### Policies

Endpoints reference **named policies**, never raw roles. The mapping lives in one
place — `AuthorizationPolicies` (`src/CTMS.Api/Auth/AuthorizationPolicies.cs`);
the Admin UI keeps a byte-identical copy.

| Policy | Satisfied by roles |
|--------|--------------------|
| `CanRead` | admin, manager, reviewer, translator, reader |
| `CanEditStrings` | admin, manager, reviewer, translator |
| `CanReview` | admin, manager, reviewer |
| `CanManageContent` | admin, manager |
| `CanPublish` | admin, manager |
| `CanAdminProjects` | admin |

### Endpoint → policy matrix

| Endpoint | Policy |
|----------|--------|
| `GET /api/projects`, `GET /api/projects/{id}` | `CanRead` |
| `POST /api/projects` | `CanAdminProjects` |
| `GET .../locales`, `GET .../locales/{id}` | `CanRead` |
| `POST/PATCH/DELETE .../locales[...]` | `CanManageContent` |
| `GET .../keys`, `GET .../keys/{id}` | `CanRead` |
| `POST/PATCH/DELETE .../keys[...]` | `CanManageContent` |
| `GET .../keys/{keyId}/strings[...]`, `GET .../projects/{id}/strings` | `CanRead` |
| `PUT .../keys/{keyId}/strings/{localeId}` (upsert) | `CanEditStrings` |
| `POST .../strings/{localeId}/review` (submit/approve/reject/reopen/**publish** action) | `CanReview` |
| `POST /api/projects/{id}/bundles/{localeCode}` (publish a bundle) | `CanPublish` |
| `GET .../bundles/{localeCode}`, `.../versions`, `.../versions/{n}` | anonymous by default — see below |
| `GET .../history`, `GET .../keys/.../history` | `CanRead` |
| `GET /health`, `GET /health/ready`, `/swagger` | anonymous |

The review `publish` action (`Approved → Published` on a single string) is part
of the review workflow and needs `CanReview`; cutting a **bundle**
(`POST .../bundles/...`) is a separate step and needs `CanPublish`.

### `Auth:PublicBundleReads` (default `true`)

The three bundle **GET** routes are the SDK / CDN delivery path (client-devops
WS6), which must work for unauthenticated clients. While
`Auth:PublicBundleReads` is `true` they are `AllowAnonymous`. Set it to `false`
to require `CanRead` on them instead (e.g. a fully private deployment). Bundle
**publication** (`POST`) always requires `CanPublish` regardless of this flag.

### `Auth:Enabled` (default `true`) — local-dev / test escape hatch

With `Auth:Enabled=false` (set in `appsettings.Development.json`) the JWT scheme
is replaced by a permissive handler that authenticates **every** request as a
synthetic principal (`dev-bypass`) holding **all** roles, so `dotnet run` and the
84+ tests need no IdP. A loud warning is logged at startup. `Auth:Enabled=false`
is **refused at startup** when `ASPNETCORE_ENVIRONMENT=Production`.

### Actor fields are taken from the token

`updatedBy` (string upsert), `reviewedBy` (review), and `publishedBy` (bundle
publish) in the request body are **ignored when the caller presents a real
bearer token** — the actor recorded in the row and the audit trail is the token
identity (`name` claim, then `preferred_username`, then `oid`). The body field
still works when auth is disabled or the request is anonymous (bundle reads).

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
| `GET /api/projects/{projectId:guid}/strings?reviewState=&skip=0&take=50` | - | `200` `PagedResult<TranslationStringDto>` | `400` bad `reviewState`; `404` unknown project |

### Project-wide string list

`GET /api/projects/{projectId:guid}/strings` returns every string in the project
(across all keys and locales), newest-updated first, as
`PagedResult<TranslationStringDto>` (`{ items, total }`).

- `reviewState` (optional) filters by exact `ReviewState` name (`Draft`,
  `NeedsReview`, `Approved`, `Published`). An unknown name - or a numeric value -
  is `400`. Omitted means all states.
- `skip` is floored at 0; `take` defaults to 50 and is capped at 200.
- `404` when the project does not exist. A project with no matching strings is
  `200` with `{ items: [], total: 0 }`.
- Scope is the project: strings under other projects' keys are never returned.
  (The query resolves the project's key ids and matches
  `translationStrings.translationKeyId` against that set; `TranslationString`
  is **not** denormalised with a `projectId`.)

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

## Bundles

Immutable, versioned snapshots of a locale's `Published` strings. DTOs:
`TranslationBundleDto`, `BundleVersionDto`, `PublishBundleRequest`
(`src/CTMS.Application/Translations`).

```
TranslationBundleDto  { id, projectId, localeCode, version,
                        entries: { "<keyName>": "<value>" }, etag, createdBy, createdAt }
BundleVersionDto      { version, etag, createdAt, createdBy, entryCount }
PublishBundleRequest  { publishedBy? }        // omitted / blank -> "system"
```

`{localeCode}` is the BCP-47 locale **code** (e.g. `fr`, `fr-CA`), matched
against the project's locales - not a GUID. `etag` is the raw lowercase-hex
SHA-256 content hash (`TranslationBundle.ComputeETag`); wrap it in double quotes
to use it as an HTTP entity tag.

| Method & route | Body | Success | Errors |
|----------------|------|---------|--------|
| `POST /api/projects/{projectId:guid}/bundles/{localeCode}` | `PublishBundleRequest` (optional) | `201` `TranslationBundleDto` + `Location: .../bundles/{localeCode}/versions/{version}` | `400` blank locale code / nothing published; `404` unknown project or locale; `409` version race |
| `GET /api/projects/{projectId:guid}/bundles/{localeCode}` | `If-None-Match` (optional) | `200` `TranslationBundleDto` (latest version) + `ETag` + `Cache-Control: no-cache`; `304 Not Modified` (no body, `ETag` still set) when `If-None-Match` matches | `404` unknown project/locale, or nothing published yet |
| `GET /api/projects/{projectId:guid}/bundles/{localeCode}/versions` | - | `200` `BundleVersionDto[]` (ascending by `version`, no entries payload) | `404` unknown project/locale |
| `GET /api/projects/{projectId:guid}/bundles/{localeCode}/versions/{version:int}` | - | `200` `TranslationBundleDto` | `404` unknown project/locale/version |

Publish semantics:

- Gathers every `TranslationString` for the locale whose `reviewState` is
  `Published` (strings get there first, one at a time, via the review `publish`
  action), joins each to its `TranslationKey.keyName`, and freezes the
  `keyName -> value` map.
- **Publishing never changes any string's `reviewState`.** It only snapshots.
  A published string stays `Published` after the bundle is cut.
- `version` is monotonic per `(projectId, localeCode)`, starting at 1, computed
  as `latest.version + 1`. Older versions are retained forever.
- `etag` is derived purely from the entries: two publishes with identical
  content produce byte-identical `etag`s (only `version`/`id`/`createdAt`
  differ); any value change changes the `etag`.
- Publishing with zero `Published` strings is rejected `400` - no empty bundle
  is created.
- The `(projectId, localeCode, version)` unique index makes a concurrent publish
  that grabbed the same next version fail `409` (`ConflictException`).
- A `Published` `AuditEntry` is written with
  `entityType = "TranslationBundle"`, `entityId = <bundle id>`,
  `detail = "{localeCode} v{version}, {n} strings"`.

### Conditional GET on the latest bundle

`GET .../bundles/{localeCode}` is an HTTP conditional GET:

- **`ETag`** — every `200` (and every `304`) carries `ETag: "<etag>"`, the body's
  raw lowercase-hex `etag` wrapped in double quotes (a strong validator).
- **`Cache-Control: no-cache`** — clients (and shared caches) may store the
  response but must revalidate before reuse; a stored copy is still allowed to be
  sent back as an `If-None-Match` conditional request.
- **`If-None-Match`** — if the request header contains a matching entity-tag the
  response is `304 Not Modified` with no body and the `ETag` still set; otherwise
  it is `200` with the full body. Matching accepts the quoted form
  (`"<etag>"`), an optional weak prefix (`W/"<etag>"`), a comma-separated list,
  the header repeated across multiple values, and `*` (matches whenever a bundle
  exists).

A **Redis** cache fronts this route (`ctms:bundle:{projectId}:{localeCode}:latest`,
locale code lower-cased; TTL `Cache:BundleTtlMinutes`, default 60). A cache hit
serves the `ETag` / `304` decision and the body without touching MongoDB;
publishing a new version invalidates the key. When `ConnectionStrings:Redis` is
unset (e.g. a local `dotnet run`) an in-process distributed-memory cache is used
instead, so the route behaves identically without Redis.

The `versions` and by-version routes stay uncached and unconditioned (a
by-version bundle is immutable, but WS4 is scoped to the latest route).

---

## History / audit trail

Read-only projection of the append-only audit log. DTO: `AuditEntryDto`
(`src/CTMS.Application/Audit`).

```
AuditEntryDto  { id, projectId, entityType, entityId, action, actor,
                 timestamp, fromState?, toState?, detail? }
```

`action` is an `AuditAction` name (`Created`, `Edited`, `Submitted`, `Approved`,
`Rejected`, `Reopened`, `Published`); `fromState` / `toState` are `ReviewState`
names when the operation changed review state.

| Method & route | Body / query | Success | Errors |
|----------------|--------------|---------|--------|
| `GET /api/projects/{projectId:guid}/history?skip=0&take=50` | `skip` floored at 0; `take` default 50, capped at 200 | `200` `PagedResult<AuditEntryDto>`, newest first | `404` unknown project |
| `GET /api/projects/{projectId:guid}/keys/{keyId:guid}/strings/{localeId:guid}/history` | - | `200` `AuditEntryDto[]` for that one string, newest first | `404` if the string does not exist |

- The project feed spans every audited entity in the project (strings and
  bundles). The per-string feed is `entityType = "TranslationString"` filtered
  to the string's id.
- Entries are append-only - never edited or deleted.
