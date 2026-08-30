# CTMS HTTP API reference

Generated from `src/CTMS.Api/Endpoints/*` and the `CLAUDE.md` "API surface"
section. All payloads are JSON; property names are camelCased on the wire
(`baseLanguageCode`, `translationKeyId`, ...). The C# DTO names are given so you
can cross-reference `src/CTMS.Application`.

- Base URL in local dev: `http://localhost:5147` (Swagger UI at `/swagger` in
  the `Development` environment). In the container / compose it is
  `http://localhost:8080`.
- **Authentication is required** (Microsoft Entra ID JWT bearer, or an
  `X-Api-Key` header for read-only machine callers) on every `/api/*` endpoint
  except the **client delivery reads**, which are anonymous by default
  (`Auth:PublicBundleReads=true`) — see
  [Authentication & authorization](#authentication--authorization). `/health`,
  `/health/ready` and Swagger are always anonymous.
- A local-dev / test escape hatch (`Auth:Enabled=false`) authenticates every
  request as a synthetic all-roles principal so `dotnet run` and the test suite
  work with no identity provider. It is refused under `Production`.
- The **application** in a route path is the application code (the `Project`
  slug, e.g. `icoach`), not a GUID. The **language** in a route path is a BCP-47
  code (e.g. `fr-FR`). Key ids are GUIDs (`{keyId:guid}` route constraint) — a
  non-GUID segment is a route miss (`404`), not a `400`.

---

## Error model — RFC 7807 ProblemDetails

Known application/domain exceptions are translated by
`ApplicationExceptionHandler` into `application/problem+json`:

| Exception | HTTP status | `title` |
|-----------|-------------|---------|
| `ValidationException` | `400` | `Invalid request` |
| `NotFoundException` | `404` | `Resource not found` |
| `SlugAlreadyInUseException` | `409` | `Application code already in use` |
| `ConflictException` | `409` | `Conflict` |
| `InvalidReviewTransitionException` | `409` | `Invalid review transition` |

`detail` carries the exception message. Anything not in this table is unhandled
and surfaces as a normal `500`. There is **no** concurrency / version-conflict
path: string upsert is last-write-wins, so there is no `ConcurrencyException`,
no `409` with `extensions.currentVersion`, and no `DbUpdateConcurrencyException`
mapping.

Endpoints that return a resource-or-`null` (most `GET {id}` and `PATCH`) answer a
bare `404` with no ProblemDetails body when the resource is missing.

---

## Authentication & authorization

### Bearer requirement

The API authenticates **Microsoft Entra ID** access tokens as JWT bearer
(`Authorization: Bearer <token>`), wired with `Microsoft.Identity.Web`
(`AddMicrosoftIdentityWebApi`, config section `AzureAd`). A request with no / an
invalid token to a protected endpoint gets `401`. An authenticated caller whose
token carries **no recognised role** gets `403` on every protected endpoint.

Roles come from the token's `roles` claim (Entra **app roles**):

| Role | Intended for |
|------|--------------|
| `ctms.admin` | Administrators — everything, incl. create applications |
| `ctms.manager` | Project managers — manage languages, applications & keys, publish, + all reviewer/translator rights |
| `ctms.reviewer` | Reviewers — review transitions, edit strings, read |
| `ctms.translator` | Translators — create/edit string values, submit for review, read |
| `ctms.reader` | Read-only clients — every GET |

### Policies

Endpoints reference **named policies**, never raw roles
(`src/CTMS.Api/Auth/AuthorizationPolicies.cs`; the Admin UI keeps a
byte-identical copy).

| Policy | Satisfied by roles |
|--------|--------------------|
| `CanRead` | admin, manager, reviewer, translator, reader |
| `CanEditStrings` | admin, manager, reviewer, translator |
| `CanReview` | admin, manager, reviewer |
| `CanManageContent` | admin, manager |
| `CanPublish` | admin, manager |
| `CanAdminProjects` | admin |

### `Auth:PublicBundleReads` (default `true`)

The **client delivery reads** — `GET /api/translations/{application}/{language}`,
`GET /api/languages`, `GET /api/applications` — are the SDK / CDN delivery path,
which must work for unauthenticated clients. While `Auth:PublicBundleReads` is
`true` they are `AllowAnonymous`; set it to `false` to require `CanRead` on them
instead (a fully private deployment). The config key keeps its historical name
even though versioned bundles are gone. Every **other** `/api/*` route — the
catalogue `GET {code}` reads, the management grid / dashboard / missing /
categories reads, and all writes — always requires a token regardless of this
flag.

### `Auth:Enabled` (default `true`) — local-dev / test escape hatch

With `Auth:Enabled=false` (set in `appsettings.Development.json`) the JWT scheme
is replaced by a permissive handler that authenticates **every** request as a
synthetic principal (`dev-bypass`) holding **all** roles. A loud warning is
logged at startup. `Auth:Enabled=false` is **refused at startup** when
`ASPNETCORE_ENVIRONMENT=Production`.

### Actor fields are taken from the token

`updatedBy` (string upsert), `reviewedBy` (review), `createdBy` (key create) and
the bulk-publish actor in the request body are **ignored when the caller presents
a real bearer token** — the actor recorded in the row and the audit trail is the
token identity (`name`, then `preferred_username`, then `oid`). The body field
still applies when auth is disabled or the request is anonymous. See
`src/CTMS.Api/Auth/TokenActor.cs`.

### API keys — read-only machine access

A machine caller (a CI job, an SSR site, a CDN origin) that only needs to *read*
translations can authenticate with a long-lived key instead of acquiring an Entra
token per request:

- The key travels in an **`X-Api-Key`** request header.
- It authenticates a read-only principal holding the single role `ctms.reader`,
  so it satisfies `CanRead` and nothing else — every write policy still returns
  `403`.
- The `X-Api-Key` scheme is **composed with** the JWT bearer scheme: a request
  may present either. Bearer is tried first; a request with no bearer token but a
  valid `X-Api-Key` is authenticated as the reader principal.
- Keys are stored **hashed** (`apiKeys` collection); the raw key is shown **once**
  at creation and never again. Each key tracks `LastUsedAt`.
- The scheme is active whenever `Auth:Enabled=true`. With `Auth:Enabled=false`
  (local dev / tests) the all-roles bypass covers everything and `X-Api-Key` is
  not consulted.

| Method & route | Body | Success | Errors | Policy |
|----------------|------|---------|--------|--------|
| `GET /api/api-keys` | — | `200` — key metadata (`id`, `name`, `createdAt`, `lastUsedAt?`); **never** the raw key or its hash | — | `CanAdminProjects` |
| `POST /api/api-keys` | `{ name }` | `201` — the created key **including the raw secret, once** | `400` blank name | `CanAdminProjects` |
| `DELETE /api/api-keys/{id}` | — | `204` | `404` unknown id | `CanAdminProjects` |

Deleting a key revokes it immediately.

---

## Client delivery

The routes a client application / SDK / CDN calls to fetch translations. All
three are anonymous while `Auth:PublicBundleReads=true` (the default).

### `GET /api/translations/{application}/{language}`

Assembled-on-demand published translations for one `(application, language)`
pair. DTO: `PublishedTranslationsResponse`
(`src/CTMS.Application/Translations/PublishedTranslationsDtos.cs`).

```
PublishedTranslationsResponse { application, language, translations: { "<keyName>": "<value>" } }
```

- `translations` is a **flat** `keyName → value` map, ordered by key. The value
  set is: this application's published strings, plus every `IsShared`
  application's published strings (the app-specific value wins on a key-name
  collision), with any key still missing a value in `{language}` filled by
  walking that language's `FallbackCode` chain (cycle-guarded). A key with no
  published value anywhere is omitted.
- **`ETag`** — every `200` and every `304` carries `ETag: "<hash>"`, where
  `<hash>` is a raw lowercase-hex SHA-256 over the ordered entries
  (`TranslationContentHash.Compute`). It is a strong validator. **No version
  number is involved.**
- **`Cache-Control: no-cache`** — a client / shared cache may store the response
  but must revalidate before reuse.
- **`If-None-Match`** — a request whose header contains a matching entity-tag
  gets `304 Not Modified` with no body and the `ETag` still set. Matching accepts
  the quoted form, an optional `W/` weak prefix, a comma-separated list, the
  header repeated across values, and `*` (matches whenever a map exists).
- **`404`** — unknown or inactive application; unknown or inactive language; or
  the language is not in the application's `enabledLanguageCodes`. Bare `404`, no
  ProblemDetails body.
- A **Redis** read-through cache (`translations:{app}:{language}`, both
  lower-cased; TTL `Cache:TranslationsTtlMinutes`, default 60) fronts this route;
  a hit serves the `ETag` / `304` decision and body without touching MongoDB. In
  the absence of `ConnectionStrings:Redis` an in-process distributed-memory cache
  is used, so the route behaves identically without Redis. A publish invalidates
  the affected keys (a shared-application publish fans out to every application).
- Rate limiting: `GET /api/translations/...` requests are counted in a separate,
  looser IP-keyed partition (partition prefix `delivery:`, limit
  `RateLimit:BundlePermitPerWindow`) so a busy CDN edge cannot exhaust an
  authenticated user's budget.

### `GET /api/languages`

DTO: `LanguageDto` (`src/CTMS.Application/Languages/LanguageDtos.cs`).

```
LanguageDto { code, name, fallbackCode?, isRtl, active, createdAt, updatedAt }
```

| Query | Success | Notes |
|-------|---------|-------|
| `?includeInactive=false` (default) | `200` `LanguageDto[]` | Active languages only unless `includeInactive=true`. |

### `GET /api/applications`

DTO: `ApplicationDto` (`src/CTMS.Application/Projects/ProjectDtos.cs`).

```
ApplicationDto { code, name, description?, isShared, active, baseLanguageCode,
                 enabledLanguageCodes: string[], createdAt, updatedAt }
```

| Query | Success | Notes |
|-------|---------|-------|
| `?includeInactive=false` (default) | `200` `ApplicationDto[]` | Active applications only unless `includeInactive=true`. |

---

## Applications

`Project` aggregate, surfaced as an *application*. `{code}` is the slug. DTOs:
`ApplicationDto`, `CreateApplicationRequest`, `UpdateApplicationRequest`.

```
CreateApplicationRequest { name, baseLanguageCode, code?, description?,
                           isShared? = false, enabledLanguageCodes?: string[] }
UpdateApplicationRequest { name?, description?, isShared?, active?,
                           baseLanguageCode?, enabledLanguageCodes?: string[] }   // omitted members unchanged
```

| Method & route | Body | Success | Errors | Policy |
|----------------|------|---------|--------|--------|
| `GET /api/applications` | — | `200` `ApplicationDto[]` | — | anonymous by default (see [Client delivery](#client-delivery)) |
| `GET /api/applications/{code}` | — | `200` `ApplicationDto` | `404` unknown | `CanRead` |
| `POST /api/applications` | `CreateApplicationRequest` | `201` `ApplicationDto` + `Location` | `400` validation; `409` code already in use | `CanAdminProjects` |
| `PATCH /api/applications/{code}` | `UpdateApplicationRequest` | `200` `ApplicationDto` | `400` validation; `404` unknown | `CanManageContent` |
| `PUT /api/applications/{code}/languages/{language}` | — | `200` `ApplicationDto` | `400` unknown/inactive language; `404` unknown application | `CanManageContent` |
| `DELETE /api/applications/{code}/languages/{language}` | — | `200` `ApplicationDto` | `404` unknown application | `CanManageContent` |

- `code` is derived from `name` (lower-cased, hyphenated) when omitted; an empty
  derived code is `400`.
- `PUT/DELETE .../languages/{language}` add/remove a code in
  `enabledLanguageCodes` and return the updated application (not `204`).
  Enabling validates the language exists and is active (`400` otherwise);
  disabling an absent code is a no-op `200`.
- `enabledLanguageCodes` on create/update is validated the same way. There is no
  delete-application endpoint; set `active=false` via `PATCH`.

---

## Languages

Global `Language` catalogue, keyed by BCP-47 `code`. DTOs: `LanguageDto`,
`CreateLanguageRequest`, `UpdateLanguageRequest`, `LanguageSuggestionDto`,
`BulkCreateLanguagesRequest`, `BulkCreateLanguagesResult`.

```
CreateLanguageRequest        { code, name, fallbackCode?, isRtl? = false, active? = true }
UpdateLanguageRequest        { name?, fallbackCode?, isRtl?, active? }   // omitted members unchanged
LanguageSuggestionDto        { code, name, isRtl }
BulkCreateLanguageItem       { code, name, fallbackCode?, isRtl? }
BulkCreateLanguagesRequest   { languages: BulkCreateLanguageItem[] }
BulkCreateLanguagesResult    { created: string[], skipped: string[] }
```

| Method & route | Body | Success | Errors | Policy |
|----------------|------|---------|--------|--------|
| `GET /api/languages` | — | `200` `LanguageDto[]` | — | anonymous by default |
| `GET /api/languages/suggestions` | — | `200` `LanguageSuggestionDto[]` | — | anonymous while `Auth:PublicBundleReads=true`, else `CanRead` |
| `POST /api/languages/bulk` | `BulkCreateLanguagesRequest` | `200` `BulkCreateLanguagesResult` | `400` empty list, or an entry with a blank `code` / `name` | `CanManageContent` |
| `GET /api/languages/{code}` | — | `200` `LanguageDto` | `404` unknown | `CanRead` |
| `POST /api/languages` | `CreateLanguageRequest` | `201` `LanguageDto` + `Location` | `400` validation; `409` code already exists | `CanManageContent` |
| `PATCH /api/languages/{code}` | `UpdateLanguageRequest` | `200` `LanguageDto` | `400` validation; `404` unknown | `CanManageContent` |

- `code` is trimmed and internal whitespace collapsed; casing preserved.
- `fallbackCode` must not equal the language's own `code` (`400`). Set it to `""`
  via `PATCH` to clear it.
- There is no delete endpoint; set `active=false`.
- **`GET /api/languages/suggestions`** returns a **static** ~38-entry BCP-47
  catalogue (`LanguageCatalogue`, `src/CTMS.Application/Languages`) — it is a
  constant in code, never persisted and never queried. It is what the Admin UI
  new-application wizard offers as a picklist. RTL entries are the Arabic
  locales, Hebrew and Persian.
- **`POST /api/languages/bulk`** registers every entry that does not already
  exist and is **idempotent**: an existing code (case-insensitive) is returned in
  `skipped`, not errored; a duplicate code within the same request body is
  de-duplicated. Only a blank `code` or `name` in an entry, or an empty
  `languages` array, is `400`. A single `SaveChanges` covers the whole batch.

---

## Translation keys

Nested under an application. DTOs: `TranslationKeyDto`,
`CreateTranslationKeyRequest`, `UpdateTranslationKeyRequest`, `PagedResult<T>`.

```
TranslationKeyDto           { id, application, keyName, category, description?, active, createdBy, createdAt, updatedAt }
CreateTranslationKeyRequest { keyName, category?, description?, createdBy? }
UpdateTranslationKeyRequest { category?, description?, active? }   // omitted members unchanged
PagedResult<T>              { items: T[], total: int }
```

| Method & route | Body / query | Success | Errors | Policy |
|----------------|--------------|---------|--------|--------|
| `GET /api/applications/{application}/keys?category=&skip=0&take=50` | `skip` floored at 0; `take` default 50, capped at 200 | `200` `PagedResult<TranslationKeyDto>` | `404` unknown application | `CanRead` |
| `GET /api/applications/{application}/keys/{keyId:guid}` | — | `200` `TranslationKeyDto` | `404` | `CanRead` |
| `POST /api/applications/{application}/keys` | `CreateTranslationKeyRequest` | `201` `TranslationKeyDto` + `Location` | `400` validation; `404` unknown application; `409` `(application, keyName)` exists | `CanManageContent` |
| `PATCH /api/applications/{application}/keys/{keyId:guid}` | `UpdateTranslationKeyRequest` | `200` `TranslationKeyDto` | `400` validation; `404` | `CanManageContent` |
| `DELETE /api/applications/{application}/keys/{keyId:guid}` | — | `204` | `404` | `CanManageContent` |

- `keyName` must match `[A-Za-z0-9_.-]+` (dotted path, e.g.
  `checkout.button.submit`).
- **`category` is optional on create.** When it is omitted, `null` or blank the
  service derives one from the key name: the segment before the first `.`,
  title-cased (`course.start` → `Course`, `nav.home.link` → `Nav`), or `General`
  when the key has no `.` or nothing usable before it
  (`CategorySuggestion.FromKeyName`). The stored `Category` is therefore always
  non-blank. `PATCH` still sets `category` **explicitly** and rejects an
  explicitly-blank value with `400`.
- `category` filter on the list is an exact, case-insensitive match.
- `DELETE` cascades to the key's `TranslationString` rows (repository-level
  multi-collection cleanup).

---

## Translation strings

One value per `(key, language)`. DTOs: `TranslationStringDto`,
`UpsertTranslationStringRequest`.

```
TranslationStringDto           { id, translationKeyId, languageCode, value, status,
                                 updatedBy?, createdAt, updatedAt }
UpsertTranslationStringRequest { value, updatedBy? }
```

`status` is the review state serialized as its name — `"Draft"`,
`"NeedsReview"`, `"Approved"`, `"Published"`. **There is no `version` field, no
`expectedVersion` request member, and no `409` concurrency response** — the
upsert is last-write-wins.

| Method & route | Body / query | Success | Errors | Policy |
|----------------|--------------|---------|--------|--------|
| `GET /api/applications/{application}/keys/{keyId:guid}/strings` | — | `200` `TranslationStringDto[]` (one per language that has a value) | `404` if the key is not in the application | `CanRead` |
| `GET /api/applications/{application}/keys/{keyId:guid}/strings/{language}` | — | `200` `TranslationStringDto` | `404` | `CanRead` |
| `PUT /api/applications/{application}/keys/{keyId:guid}/strings/{language}` | `UpsertTranslationStringRequest` | `201` `TranslationStringDto` + `Location` when created; `200` when updated | `400` blank value / blank language; `404` if the key is not in the application, the language is not registered, or the language is not enabled for the application | `CanEditStrings` |
| `GET /api/applications/{application}/strings?reviewState=&skip=0&take=50` | — | `200` `PagedResult<TranslationStringDto>` | `400` bad `reviewState`; `404` unknown application | `CanRead` |

### Upsert behaviour

- First write for a `(key, language)` creates the row in state `Draft`, returns
  `201`, and writes a `Created` audit entry (`newValue` = the value).
- A subsequent write with an unchanged `value` is a no-op — `200`, nothing
  persisted or audited.
- A subsequent write with a changed `value` updates the row, returns `200`, and
  writes an `Edited` audit entry carrying `oldValue` / `newValue`. **Last write
  wins** — a concurrent edit by another actor is overwritten silently.
- Editing an existing string resets `status` to `NeedsReview` **unless it is
  currently `Draft`** (a draft stays a draft). This includes editing an
  `Approved` or `Published` string; when a `Published` string is edited the
  delivery cache for that `(application, language)` is invalidated.

### Application-wide string list

`GET /api/applications/{application}/strings` returns every string under the
application's keys, newest-updated first, as `PagedResult<TranslationStringDto>`.

- `reviewState` (optional) filters by exact `ReviewState` name (`Draft`,
  `NeedsReview`, `Approved`, `Published`). An unknown name — or a numeric value —
  is `400`. Omitted means all states.
- `skip` floored at 0; `take` default 50, capped at 200.
- `404` when the application does not exist. An application with no matching
  strings is `200` with `{ items: [], total: 0 }`.

---

## Review workflow

DTO: `ReviewRequest`.

```
ReviewRequest { action, reviewedBy }
```

### `POST /api/applications/{application}/keys/{keyId:guid}/strings/{language}/review`

Policy: `CanReview`. Transitions live on
`TranslationString.ChangeReviewState`:

| `action` | from → to | audit action |
|----------|-----------|--------------|
| `submit` | `Draft` → `NeedsReview` | `Submitted` |
| `approve` | `NeedsReview` → `Approved` | `Approved` |
| `reject` | `NeedsReview` → `Draft` | `Rejected` |
| `reopen` | `Approved` → `NeedsReview`, or `Published` → `NeedsReview` | `Reopened` |
| `publish` | `Approved` → `Published` | `Published` |

| Outcome | Response |
|---------|----------|
| Transition applied | `200` `TranslationStringDto` (`updatedBy` = the token identity, or `reviewedBy` when anonymous / auth disabled); an `AuditEntry` is written; the delivery cache is invalidated when the string entered or left `Published` |
| Application / key / string not found | `404` (bare) |
| `action` not one of the five verbs, or `reviewedBy` blank | `400` (`ValidationException`) |
| Verb valid but illegal for the current state (e.g. `approve` on a `Draft`) | `409` (`InvalidReviewTransitionException`) |

The single-string `publish` action needs `CanReview`. The bulk
`POST /api/translations/publish` (below) is a separate step and needs
`CanPublish`.

---

## Management

The routes the admin UI drives. This section covers **Setup** (get an application
ready) and **Screens** (the working views); [Bulk operations](#bulk-operations)
and [Integration](#integration) follow as their own sections. Every *Screen*
route is `CanRead`.

### Setup

The first-run path — create an application, give it languages, load its keys —
uses routes documented in full above:

| Step | Route(s) | Section |
|------|----------|---------|
| Register languages | `GET /api/languages`, `GET /api/languages/suggestions`, `POST /api/languages`, `POST /api/languages/bulk`, `GET/PATCH /api/languages/{code}` | [Languages](#languages) |
| Create the application, enable its languages | `POST /api/applications`, `PATCH /api/applications/{code}`, `PUT/DELETE /api/applications/{code}/languages/{language}` | [Applications](#applications) |
| Load keys and strings | `POST /api/applications/{application}/keys`, `PUT .../strings/{language}`, or **bulk import** (below) | [Translation keys](#translation-keys), [Bulk operations](#bulk-operations) |

`GET /api/languages/suggestions` + `POST /api/languages/bulk` are what the Admin
UI new-application wizard uses to turn a picklist of common locales into
`Language` rows in one call. `POST /api/applications/{application}/import` is the
normal way to populate keys — see [Bulk operations](#bulk-operations).

### Screens

Every screen route takes an optional `?application=<code>` query that scopes it to
one application; omitted, it spans every active application (the union of their
enabled languages as columns).

### `GET /api/translations` — the grid

DTO: `TranslationRowDto`, `TranslationValueDto`, `PagedResult<T>`.

```
TranslationValueDto { value, status, source }
TranslationRowDto   { keyId, key, category, description?,
                      values: { "<languageCode>": { value, status, source }, ... } }
```

| Query | Success | Errors |
|-------|---------|--------|
| `?application=&category=&language=&search=&status=&skip=0&take=50` | `200` `PagedResult<TranslationRowDto>` | `400` invalid `status`; `404` when `application` is given but unknown |

- One row per active key, a cell per column language; a language with no string
  for that key is **absent** from `values`.
- `language` narrows the columns to that one code; otherwise the columns are the
  scoped application's `enabledLanguageCodes` (or the union across all
  applications).
- `category` is an exact case-insensitive filter. `search` matches the key name
  **or** any of the key's string values (case-insensitive substring).
- **`status`** (optional) is one of the four `ReviewState` names — `Draft`,
  `NeedsReview`, `Approved`, `Published`; any other value is `400`. It keeps only
  rows that have **at least one cell** in that state, but each kept row still
  carries **all** of its cells, so the grid stays coherent.
- **`source`** on each cell is provenance: `"app"` when the value is the
  application's own string, or `"shared:<code>"` when it is merged in from a
  shared application (an app-owned key wins a name collision). `source` is
  **grid-only** — the client delivery payload does not carry it.
- `skip` floored at 0; `take` default 50, capped at 200.

### `GET /api/categories`

| Query | Success | Errors |
|-------|---------|--------|
| `?application=` | `200` `string[]` — distinct non-empty categories, ordinal-sorted | `404` when `application` is given but unknown |

### `GET /api/dashboard`

DTO: `DashboardResponse`, `LanguageCoverageDto`.

```
LanguageCoverageDto { languageCode, languageName, translatedCount, totalKeys, percent, missingCount }
DashboardResponse   { applicationCount, languageCount, keyCount,
                      coverage: LanguageCoverageDto[], totalMissing }
```

| Query | Success | Errors |
|-------|---------|--------|
| `?application=` | `200` `DashboardResponse` | `404` when `application` is given but unknown |

- A key counts as **translated** in a language when a `TranslationString` exists
  in **any non-`Draft` state** (`NeedsReview`, `Approved` or `Published`).
- `percent` is `translatedCount * 100 / keyCount` rounded to 1 decimal (`0` when
  `keyCount` is 0). `coverage` is ordered by `languageCode`. `totalMissing` is
  the sum of `missingCount` across the coverage rows.

### `GET /api/translations/missing`

DTO: `MissingTranslationDto`, `PagedResult<T>`.

```
MissingTranslationDto { keyId, key, category, missingLanguages: string[] }
```

| Query | Success | Errors |
|-------|---------|--------|
| `?application=&language=&skip=0&take=50` | `200` `PagedResult<MissingTranslationDto>` | `404` when `application` is given but unknown |

- Only keys with at least one target language that has **no non-`Draft`** value
  are returned. `language` narrows the target set to one code.
- `skip` floored at 0; `take` default 50, capped at 200.

## Bulk operations

Three ways to move many strings at once: load them from a file, run one review
action across a filtered set, and publish.

### `POST /api/applications/{application}/import` — bulk file import

Policy: `CanManageContent`. DTOs: `ImportTranslationsRequest`,
`ImportTranslationsResult`, `ImportError`
(`src/CTMS.Application/Translations/Import`).

```
ImportTranslationsRequest { format, language, content, category?, status?, dryRun = false }
ImportError               { line?, key?, message }
ImportTranslationsResult  { createdKeys, createdStrings, updatedStrings, skipped,
                            errors: ImportError[], keys: string[] }   // keys ≤ 200 names
```

| Body | Success | Errors |
|------|---------|--------|
| `ImportTranslationsRequest` | `200` `ImportTranslationsResult` | `400` bad `format`, malformed `content` (the `detail` names the line), invalid `status`; `404` unknown application, or `language` not enabled for it |

- **Request-body ceiling.** This endpoint opts (via endpoint metadata) into the
  larger limit **`Limits:MaxImportBodyBytes`** (default 5&nbsp;MB) instead of the
  256&nbsp;KB global `Limits:MaxRequestBodyBytes`; an over-cap body is `413`
  before binding.
- **`format`** — one of:
  | `format` | Parser |
  |----------|--------|
  | `flat` | `key=value` lines; `#` comment lines and blank lines ignored; the value is trimmed |
  | `csv` | RFC-4180; a header row must name a `key` column and a `value` column; quoted fields, `""` escapes, embedded newlines |
  | `json` | a flat `{ "key": "value" }` object, or a nested object flattened with `.` between segments; numbers/booleans stringified, `null` → `""`, arrays rejected |
  | `resx` | `<data name="…"><value>…</value></data>` elements; comments, `<resheader>`, `xml:space` and typed/file-backed resources (`type=` / `mimetype=`) ignored |
  A later duplicate key overrides an earlier one; the first occurrence fixes
  ordering. A body that does not parse for its declared `format` is `400` whose
  `detail` is `Line <n>: <reason>` where a line number is known.
- **`language`** must be a registered language that is **enabled** for the
  application (`404` otherwise).
- Per parsed `(key, value)`: the `TranslationKey` is **created if missing** — its
  category is the request `category` if given, else derived from the key name
  (see [Translation keys](#translation-keys)); `createdBy` is the caller identity.
  The `TranslationString` for `(key, language)` is then upserted at **`status`**.
- **`status`** ∈ `Draft` (default) / `NeedsReview` / `Approved`. `Published` is
  rejected with `400` — publish through the review workflow. The importer walks
  the string through the review state machine to reach the target state.
- A key name outside `[A-Za-z0-9_.-]+` is recorded in `errors` (with the raw
  `key`) and skipped; the rest of the import proceeds.
- **`dryRun: true`** computes the full plan — counts, `errors`, `keys` — and
  writes nothing.
- `skipped` counts entries whose value **and** state already matched. If any
  imported string enters or leaves `Published`, the delivery cache for
  `(application, language)` is invalidated once at the end.

### `POST /api/applications/{application}/review-bulk` — bulk review

Policy: `CanReview`. DTOs: `ReviewBulkRequest`, `ReviewBulkResult`.

```
ReviewBulkRequest { action, language?, category?, keyIds?: guid[], reviewedBy? }
ReviewBulkResult  { transitioned, skipped }
```

| Body | Success | Errors |
|------|---------|--------|
| `ReviewBulkRequest` | `200` `ReviewBulkResult` | `400` unknown `action`, **or no filter supplied**; `404` unknown application, or `language` not registered |

- `action` ∈ `submit` / `approve` / `reject` / `reopen` / `publish` — the same
  verbs as the single-string review (`publish` needs `CanReview` here, unlike the
  standalone `POST /api/translations/publish` which needs `CanPublish`).
- **At least one of `language` / `category` / `keyIds` is required** — an
  unfiltered mass transition is refused with `400` so it can never be one click.
  Filters combine (AND).
- The action is applied to every matching string that is in a state the
  transition is **legal** from; **illegal ones are skipped**, not errored, and
  counted in `skipped`.
- One audit entry per transitioned string. The delivery cache is invalidated
  **once at the end** for the languages of strings that entered or left
  `Published` (shared-application fan-out applies).

### `POST /api/translations/publish` — bulk publish

DTO: `PublishTranslationsRequest`, `PublishTranslationsResult`. Policy:
`CanPublish`.

```
PublishTranslationsRequest { application, language? }
PublishTranslationsResult  { published: int }
```

| Body | Success | Errors |
|------|---------|--------|
| `PublishTranslationsRequest` | `200` `PublishTranslationsResult` | `404` (ProblemDetails) unknown application or unknown `language` |

- Promotes **every `Approved` string** for the application (and language, when
  given) to `Published` via the normal `Approved → Published` transition, writes
  a `Published` audit entry per string, and invalidates the delivery cache for
  the affected languages.
- Publishing a **shared** application fans the invalidation out to every
  application's cache entry for those languages.
- `published` is the number of strings promoted (`0` when there was nothing
  `Approved` — not an error).

### `GET /api/translations/publish/preview` — publish diff

Policy: `CanRead`. DTO: `PublishPreviewResponse`, `PublishPreviewChange`.

```
PublishPreviewChange  { key, currentValue?, newValue, kind }   // kind: "added" | "changed"
PublishPreviewResponse { application, language, changes: PublishPreviewChange[],
                         addedCount, changedCount }
```

| Query | Success | Errors |
|-------|---------|--------|
| `?application=&language=` | `200` `PublishPreviewResponse` | `400` `application` or `language` missing; `404` unknown/inactive application or language, or language not enabled for the application |

- Shows what a `POST /api/translations/publish` for the **same** `(application,
  language)` would change in the delivered map: it assembles the current
  published map and a hypothetical one (the application's `Approved` strings
  treated as published) and diffs them.
- `kind` is `"added"` (the key is not delivered today) or `"changed"` (a
  delivered value would differ — reached today only through the fallback chain).
- **`language` is required** (`400` otherwise) — unlike bulk publish, there is no
  all-languages preview.

---

## Integration

Machine-facing surface for CI jobs, SSR sites and downstream caches.

### API keys

Read-only key auth (`X-Api-Key`) and the `POST/GET/DELETE /api/api-keys`
management routes are documented under
[Authentication & authorization → API keys](#api-keys--read-only-machine-access).

### Publish webhooks

A `Webhook` (collection `webhooks`) registers an HTTP endpoint that CTMS calls
after a publish, so a consumer learns a bundle changed without polling `ETag`.

| Method & route | Body | Success | Errors | Policy |
|----------------|------|---------|--------|--------|
| `GET /api/webhooks` | — | `200` — webhook metadata (`id`, `url`, `createdAt`); **never** the secret | — | `CanAdminProjects` |
| `POST /api/webhooks` | `{ url }` | `201` — the created webhook **including the signing secret, once** | `400` bad URL | `CanAdminProjects` |
| `DELETE /api/webhooks/{id}` | — | `204` | `404` unknown id | `CanAdminProjects` |

**Delivery.** On a successful publish (single-string `publish`, bulk review
`publish`, or `POST /api/translations/publish`) an async, retrying
`BackgroundService` `POST`s each registered webhook:

```
POST <webhook url>
Content-Type: application/json
X-CTMS-Signature: sha256=<hex HMAC-SHA256(secret, raw request body)>

{ "event": "published", "application": "<code>", "language": "<code>",
  "etag": "<content hash>", "publishedAt": "<ISO-8601 UTC>" }
```

- One request per `(application, language)` that actually changed. `etag` is the
  same content hash the delivery route returns in its `ETag` header (unquoted
  here).
- **Retry.** Up to `Webhooks:MaxAttempts` (default 3) attempts with a per-request
  timeout of `Webhooks:TimeoutSeconds` (default 5); after the last failure the
  event is **dropped** (logged, not queued forever). A webhook failure **never**
  affects the publish or the API response — dispatch is fire-and-forget off a
  background channel.
- **Disable** the whole feature with `Webhooks:Enabled=false` (default `true`).

**Verifying the signature** (consumer side):

```
received = header["X-CTMS-Signature"]              # "sha256=abcd…"
expected = "sha256=" + hex(hmac_sha256(secret, raw_request_body_bytes))
if not constant_time_equals(received, expected):
    reject 401
# also: bound `publishedAt` to a few minutes to blunt replay
```

Compute the HMAC over the **raw** body bytes, before any JSON re-serialisation,
and compare in constant time.

**Config** (see also [architecture.md §8](architecture.md#8-configuration-and-secrets)):

| Key | Env override | Default |
|-----|--------------|---------|
| `Webhooks:Enabled` | `Webhooks__Enabled` | `true` |
| `Webhooks:TimeoutSeconds` | `Webhooks__TimeoutSeconds` | `5` |
| `Webhooks:MaxAttempts` | `Webhooks__MaxAttempts` | `3` |

---

## History / audit trail

Read-only projection of the append-only audit log. Policy: `CanRead`. DTO:
`AuditEntryDto`.

```
AuditEntryDto { id, applicationId, entityType, entityId, action, actor, timestamp,
                fromState?, toState?, detail?, oldValue?, newValue? }
```

- `action` is an `AuditAction` name (`Created`, `Edited`, `Submitted`,
  `Approved`, `Rejected`, `Reopened`, `Published`).
- `fromState` / `toState` are `ReviewState` names when the operation changed
  review state.
- `oldValue` / `newValue` carry the string value diff: `newValue` on `Created`,
  both on `Edited`, both null on review transitions.
- **`applicationId`** is the owning application's id. The outward DTO field was
  renamed from `projectId`; the internal `Project` / `ProjectId` domain names are
  unchanged.

| Method & route | Query | Success | Errors |
|----------------|-------|---------|--------|
| `GET /api/applications/{application}/history?skip=0&take=50` | `skip` floored at 0; `take` default 50, capped at 200 | `200` `PagedResult<AuditEntryDto>`, newest first | `404` unknown application |
| `GET /api/applications/{application}/keys/{keyId:guid}/strings/{language}/history` | — | `200` `AuditEntryDto[]` for that one string, newest first | `404` if the string does not exist |

Entries are append-only — never edited or deleted.

---

## Health

### `GET /health`
Liveness. No checks. `200 OK` with a health-report body while the process runs.
Opts out of rate limiting.

### `GET /health/ready`
Readiness. Runs the checks tagged `ready` — `MongoHealthCheck` (name `database`),
which issues `{ ping: 1 }` against the configured database. `200` when ready,
`503` when not. There is no Redis readiness check: the delivery cache degrades to
on-demand assembly if Redis is down, so it is not a readiness dependency.
