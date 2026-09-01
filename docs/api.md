# CTMS HTTP API reference

Generated from `src/CTMS.Api/Endpoints/*` and `src/CTMS.Api/Program.cs`. All
payloads are JSON; property names are camelCased on the wire
(`baseLanguageCode`, `translationKeyId`, …). C# DTO names are given so you can
cross-reference `src/CTMS.Application`.

- Local dev base URL: `http://localhost:5147` (Swagger UI at `/swagger` in
  `Development`). In the container / compose it is `http://localhost:8080`.
- The **project** in a route path is the project **code** (the `Project` slug,
  e.g. `nimbus`), not a GUID. The **language** is a BCP-47 code (e.g. `fr-FR`).
  Key ids are GUIDs (`{keyId:guid}` route constraint) — a non-GUID segment is a
  route miss (`404`), not a `400`.
- The API has two surfaces: the **Consumer API** (one route, anonymous by
  default) and the **Management API** (everything else, each route behind a
  named authorization policy). See
  [`authentication.md`](authentication.md) / [`authorisation.md`](authorisation.md).

---

## Error model — RFC 7807 ProblemDetails

Known application/domain exceptions are translated by
`ApplicationExceptionHandler` into `application/problem+json`:

| Exception | HTTP status | `title` |
|---|---|---|
| `ValidationException` | `400` | `Invalid request` |
| `NotFoundException` | `404` | `Resource not found` |
| `SlugAlreadyInUseException` | `409` | `Project code already in use` |
| `ConflictException` | `409` | `Conflict` |
| `InvalidReviewTransitionException` | `409` | `Invalid review transition` |

`detail` carries the exception message; `traceId` lines up with the logs.
Anything not in this table surfaces as a `500`. Endpoints that return
resource-or-`null` (most `GET {id}` / `PATCH`) answer a **bare `404`** with no
body. There is **no** concurrency / version-conflict path — string upsert is
last-write-wins, so there is no `ConcurrencyException`, no `expectedVersion`, no
`409` with `currentVersion`.

---

# Consumer API

The single route an external application, website, or SDK calls to fetch
translations. Anonymous while `Auth:PublicBundleReads=true` (the default);
`CanRead` otherwise. See [`external-consumption.md`](external-consumption.md).

## `GET /api/translations/{project}/{language}`

Assembled-on-demand published translations for one `(project, language)` pair.
DTO: `PublishedTranslationsResponse`.

**Response body (`200`)**

```json
{
  "project": "nimbus",
  "language": "fr-FR",
  "translations": {
    "common.cancel": "Quitter le cours",
    "common.save": "Enregistrer",
    "course.start": "Commencer le cours"
  }
}
```

- `translations` is a **flat `keyName → value` map, ordered by key (ordinal)**.
  The value set is: this project's `Published` strings, plus every `IsCommon`
  project's `Published` strings (**the project value wins** on a key-name
  collision), with any key still missing a value in `{language}` filled by
  walking that language's `FallbackCode` chain (cycle-guarded). A key with no
  published value anywhere is **omitted**. `Archived` strings are never included.
- **`ETag: "<hash>"`** — on every `200` and every `304`. `<hash>` is a raw
  lowercase-hex SHA-256 over the ordered entries
  (`TranslationContentHash.Compute`), a **strong** validator. **No version
  number anywhere.** See [`etag.md`](etag.md).
- **`Cache-Control: no-cache`** — a client / shared cache may store the response
  but must revalidate before reuse.
- **`If-None-Match`** — a request whose header carries a matching entity-tag gets
  **`304 Not Modified`**, no body, `ETag` still set. Matching accepts the quoted
  form, an optional `W/` weak prefix, a comma-separated list, the header repeated
  across values, and `*`.
- **`404`** (bare) — unknown or inactive project; unknown or inactive language;
  or the language is not in the project's `enabledLanguageCodes`.
- A **Redis** read-through cache (`translations:{project}:{language}`,
  lower-cased; TTL `Cache:TranslationsTtlMinutes`, default 60) fronts this route;
  a hit serves the `ETag` / `304` decision and body without touching MongoDB.
  Without `ConnectionStrings:Redis` an in-process cache is used and the route
  behaves identically. A publish invalidates the affected pair(s); a `common`
  publish fans out to every project. See [`caching.md`](caching.md).
- Rate limiting: delivery GETs are counted in a separate, looser IP-keyed
  partition (`RateLimit:BundlePermitPerWindow`).

The in-process equivalent is
`ITranslationService.GetTranslationsAsync(project, language, ct)` →
`TranslationBundle` (same map, `ETag` as a field). See
[`internal-consumption.md`](internal-consumption.md).

---

# Management API

Every route below requires an Entra ID bearer token whose `roles` claim
satisfies the named policy. The role → policy matrix is in
[`authorisation.md`](authorisation.md). The two catalogue **list** reads
(`GET /api/projects`, `GET /api/languages`) are anonymous while
`Auth:PublicBundleReads=true`; every other route always needs a token.

## Projects

`Project` aggregate. `{code}` is the slug. DTOs: `ProjectDto`,
`CreateProjectRequest`, `UpdateProjectRequest`.

```
ProjectDto            { code, name, description?, isCommon, active, baseLanguageCode,
                        enabledLanguageCodes: string[], createdAt, updatedAt }
CreateProjectRequest  { name, baseLanguageCode, code?, description?,
                        isCommon? = false, enabledLanguageCodes?: string[] }
UpdateProjectRequest  { name?, description?, isCommon?, active?, baseLanguageCode?,
                        enabledLanguageCodes?: string[] }   // omitted members unchanged
```

| Method & route | Body / query | Success | Errors | Policy |
|---|---|---|---|---|
| `GET /api/projects?includeInactive=false` | — | `200` `ProjectDto[]` | — | anonymous by default, else `CanRead` |
| `GET /api/projects/{code}` | — | `200` `ProjectDto` | `404` unknown | `CanRead` |
| `POST /api/projects` | `CreateProjectRequest` | `201` `ProjectDto` + `Location` | `400` validation; `409` code already in use | `CanAdminProjects` |
| `PATCH /api/projects/{code}` | `UpdateProjectRequest` | `200` `ProjectDto` | `400` validation; `404` unknown | `CanManageContent` |
| `PUT /api/projects/{code}/languages/{language}` | — | `200` `ProjectDto` | `400` unknown/inactive language; `404` unknown project | `CanManageContent` |
| `DELETE /api/projects/{code}/languages/{language}` | — | `200` `ProjectDto` | `404` unknown project | `CanManageContent` |

- `code` is derived from `name` (lower-cased, hyphenated) when omitted; an empty
  derived code is `400`.
- `PUT/DELETE .../languages/{language}` add/remove a code in
  `enabledLanguageCodes` and return the updated project (not `204`). Enabling
  validates the language exists and is active (`400` otherwise); disabling an
  absent code is a no-op `200`.
- `isCommon: true` marks the project whose published strings merge into every
  other project's delivered map. There is no delete-project endpoint; set
  `active: false` via `PATCH`.

## Languages

Global `Language` catalogue, keyed by BCP-47 `code`. DTOs: `LanguageDto`,
`CreateLanguageRequest`, `UpdateLanguageRequest`, `BulkCreateLanguagesRequest`,
`BulkCreateLanguagesResult`.

```
LanguageDto                 { code, name, fallbackCode?, isRtl, active, createdAt, updatedAt }
CreateLanguageRequest       { code, name, fallbackCode?, isRtl? = false, active? = true }
UpdateLanguageRequest       { name?, fallbackCode?, isRtl?, active? }   // omitted members unchanged
BulkCreateLanguageItem      { code, name, fallbackCode?, isRtl? }
BulkCreateLanguagesRequest  { languages: BulkCreateLanguageItem[] }
BulkCreateLanguagesResult   { created: string[], skipped: string[] }
```

| Method & route | Body | Success | Errors | Policy |
|---|---|---|---|---|
| `GET /api/languages?includeInactive=false` | — | `200` `LanguageDto[]` | — | anonymous by default, else `CanRead` |
| `POST /api/languages/bulk` | `BulkCreateLanguagesRequest` | `200` `BulkCreateLanguagesResult` | `400` empty list, or an entry with a blank `code` / `name` | `CanManageContent` |
| `GET /api/languages/{code}` | — | `200` `LanguageDto` | `404` unknown | `CanRead` |
| `POST /api/languages` | `CreateLanguageRequest` | `201` `LanguageDto` + `Location` | `400` validation; `409` code already exists | `CanManageContent` |
| `PATCH /api/languages/{code}` | `UpdateLanguageRequest` | `200` `LanguageDto` | `400` validation; `404` unknown | `CanManageContent` |

- `code` is trimmed and internal whitespace collapsed; casing preserved.
- `fallbackCode` must not equal the language's own `code` (`400`). Set it to
  `""` via `PATCH` to clear it.
- No delete endpoint; set `active: false`.
- `POST /api/languages/bulk` is **idempotent** — an existing code
  (case-insensitive) is returned in `skipped`, not errored; a duplicate code
  within the request body is de-duplicated. Only a blank `code`/`name` or an
  empty `languages` array is `400`. There is **no** static "suggestions"
  catalogue endpoint — any BCP-47 code is accepted.

## Translation keys

Nested under a project. DTOs: `TranslationKeyDto`, `CreateTranslationKeyRequest`,
`UpdateTranslationKeyRequest`, `PagedResult<T>`.

```
TranslationKeyDto           { id, project, keyName, category, description?, active, createdBy, createdAt, updatedAt }
CreateTranslationKeyRequest { keyName, category?, description?, createdBy? }
UpdateTranslationKeyRequest { category?, description?, active? }   // omitted members unchanged
PagedResult<T>              { items: T[], total: int }
```

| Method & route | Body / query | Success | Errors | Policy |
|---|---|---|---|---|
| `GET /api/projects/{project}/keys?category=&skip=0&take=50` | `skip` floored at 0; `take` default 50, capped at 200 | `200` `PagedResult<TranslationKeyDto>` | `404` unknown project | `CanRead` |
| `GET /api/projects/{project}/keys/{keyId:guid}` | — | `200` `TranslationKeyDto` | `404` | `CanRead` |
| `POST /api/projects/{project}/keys` | `CreateTranslationKeyRequest` | `201` + `Location` | `400` validation; `404` unknown project; `409` `(project, keyName)` exists | `CanManageContent` |
| `PATCH /api/projects/{project}/keys/{keyId:guid}` | `UpdateTranslationKeyRequest` | `200` | `400` validation; `404` | `CanManageContent` |
| `DELETE /api/projects/{project}/keys/{keyId:guid}` | — | `204` | `404` | `CanManageContent` |

- `keyName` must match `[A-Za-z0-9_.-]+`.
- **`category` is optional on create.** When omitted / blank it is derived from
  the key name: the segment before the first `.`, title-cased (`course.start` →
  `Course`), else `General` (`CategorySuggestion.FromKeyName`). The stored
  category is always non-blank. `PATCH` sets `category` explicitly and rejects an
  explicitly-blank value with `400`.
- `category` filter on the list is an exact, case-insensitive match.
- `DELETE` cascades to the key's `TranslationString` rows.

## Translation strings

One value per `(key, language)`. DTOs: `TranslationStringDto`,
`UpsertTranslationStringRequest`.

```
TranslationStringDto           { id, translationKeyId, languageCode, value, status,
                                 updatedBy?, createdAt, updatedAt }
UpsertTranslationStringRequest { value, updatedBy? }
```

`status` is the `ReviewState` name — `"Draft"`, `"InReview"`, `"Approved"`,
`"Published"`, `"Archived"`. **No `version` field, no `expectedVersion`, no
`409` concurrency response.**

| Method & route | Body / query | Success | Errors | Policy |
|---|---|---|---|---|
| `GET /api/projects/{project}/strings?reviewState=&skip=0&take=50` | — | `200` `PagedResult<TranslationStringDto>` (newest-updated first) | `400` bad `reviewState`; `404` unknown project | `CanRead` |
| `GET /api/projects/{project}/keys/{keyId:guid}/strings` | — | `200` `TranslationStringDto[]` (one per language with a value) | `404` if the key is not in the project | `CanRead` |
| `GET /api/projects/{project}/keys/{keyId:guid}/strings/{language}` | — | `200` `TranslationStringDto` | `404` | `CanRead` |
| `PUT /api/projects/{project}/keys/{keyId:guid}/strings/{language}` | `UpsertTranslationStringRequest` | `201` + `Location` when created; `200` when updated | `400` blank value / blank language; `404` if the key is not in the project, the language is not registered, or the language is not enabled for the project | `CanEditStrings` |

### Upsert behaviour

- First write for a `(key, language)` creates the row in state `Draft`, returns
  `201`, writes a `Created` audit entry (`newValue` = the value).
- A subsequent write with an **unchanged** `value` is a no-op — `200`, nothing
  persisted or audited.
- A subsequent write with a **changed** `value` updates the row, returns `200`,
  writes an `Edited` audit entry with `oldValue` / `newValue`. **Last write
  wins** — a concurrent edit by another actor is overwritten silently.
- Editing resets `status` to `InReview` **unless it is currently `Draft`** (a
  draft stays a draft; an `Archived` string stays archived). When a `Published`
  string is edited, the delivery cache for that `(project, language)` is
  invalidated.
- `reviewState` on the project-wide list filters by exact `ReviewState` name; an
  unknown or numeric value is `400`.

## Review workflow

DTO: `ReviewRequest { action, reviewedBy }`.

### `POST /api/projects/{project}/keys/{keyId:guid}/strings/{language}/review`

Policy: `CanReview`. Transitions live on `TranslationString.ChangeReviewState`:

| `action` | from → to | audit action |
|---|---|---|
| `submit` | `Draft` → `InReview` | `Submitted` |
| `approve` | `InReview` → `Approved` | `Approved` |
| `reject` | `InReview` → `Draft` | `Rejected` |
| `reopen` | `Approved` → `InReview`, or `Published` → `InReview` | `Reopened` |
| `publish` | `Approved` → `Published` | `Published` |
| `archive` | `Draft` / `InReview` / `Approved` / `Published` → `Archived` | `Archived` |
| `unarchive` | `Archived` → `Draft` | `Unarchived` |

| Outcome | Response |
|---|---|
| Transition applied | `200` `TranslationStringDto`; `updatedBy` = the token identity (or `reviewedBy` when anonymous / auth disabled); an `AuditEntry` is written; the delivery cache is invalidated when the string entered or left `Published` |
| Project / key / string not found | `404` (bare) |
| `action` not a known verb, or `reviewedBy` blank | `400` (`ValidationException`) |
| Verb valid but illegal from the current state | `409` (`InvalidReviewTransitionException`) |

The single-string `publish` action needs `CanReview`. The bulk
`POST /api/translations/publish` is a separate step and needs `CanPublish`.

### `POST /api/projects/{project}/review-bulk` — bulk review

Policy: `CanReview`. DTOs: `ReviewBulkRequest`, `ReviewBulkResult`.

```
ReviewBulkRequest { action, language?, category?, keyIds?: guid[], reviewedBy? }
ReviewBulkResult  { transitioned, skipped }
```

| Body | Success | Errors |
|---|---|---|
| `ReviewBulkRequest` | `200` `ReviewBulkResult` | `400` unknown `action`, **or no filter supplied**; `404` unknown project, or `language` not registered |

- `action` is one of the seven verbs above.
- **At least one of `language` / `category` / `keyIds` is required** — an
  unfiltered mass transition is refused with `400`. Filters combine (AND).
- The action is applied to every matching string that is in a state the
  transition is legal from; **illegal ones are skipped**, not errored, and
  counted in `skipped`.
- One audit entry per transitioned string. The delivery cache is invalidated
  once at the end for the languages of strings that entered or left `Published`
  (`common` fan-out applies).

## Bulk export

### `GET /api/projects/{project}/export`

Policy: `CanRead`. Streams a translator work file (CSV or XLSX) — one row per key
the project **owns**, one column per language. Query DTO:
`TranslationExportQuery`. The writers live in
`CTMS.Application/Translations/Export` (`TranslationExporter` → `ExportedFile`);
the endpoint only streams the bytes.

| Query param | Required | Meaning |
|---|:--:|---|
| `format` | yes | `csv` or `xlsx`; any other value (or omitted) → `400` |
| `language` | no | one BCP-47 code — emit just that language column; omitted ⇒ one column per code in the project's `enabledLanguageCodes` |
| `category` | no | only keys in this category (exact, case-insensitive) |
| `status` | no | only keys with **at least one** string in this `ReviewState` (`Draft` / `InReview` / `Approved` / `Published` / `Archived`), same semantics as the grid `status` filter; any other value → `400` |
| `includeInactiveKeys` | no | default `false`; `true` also emits inactive keys |

| Outcome | Response |
|---|---|
| File | `200`, body = the file bytes, `Content-Disposition: attachment; filename="{project}-translations.<ext>"` |
| Unknown / inactive project | `404` (bare) |
| Bad / missing `format`, or bad `status` | `400` (ProblemDetails) |

- **Rows** — one per `TranslationKey` the project owns, ordered by key name
  (ordinal). A `common` project's keys are **not** merged in (export `common`
  itself to edit shared strings).
- **Columns** — `key`, `category`, `description`, then one per language code.
  Each language cell is that key's **current** value in that language **in any
  review state** (a translator work file, not the published map); blank when no
  string exists.
- **CSV** — `text/csv; charset=utf-8`, RFC 4180 quoting, `\r\n` line endings, a
  leading UTF-8 BOM; `filename="{project}-translations.csv"`.
- **XLSX** — `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`;
  one worksheet `Translations`, bold + frozen header row, frozen first column,
  auto-sized columns; `filename="{project}-translations.xlsx"`. Written with
  ClosedXML.
- An exported CSV/XLSX re-imports as a **wide** file. See
  [`import-export.md`](import-export.md).

## Bulk import

### `POST /api/projects/{project}/import`

Policy: `CanManageContent`. DTOs: `ImportTranslationsRequest`,
`ImportTranslationsResult`, `ImportError`.

```
ImportTranslationsRequest { format, language?, content?, contentBase64?,
                            category?, status?, dryRun = false }
ImportError               { line?, key?, message }
ImportTranslationsResult  { createdKeys, createdStrings, updatedStrings, skipped,
                            errors: ImportError[], keys: string[] }   // keys <= 200 names
```

| Body | Success | Errors |
|---|---|---|
| `ImportTranslationsRequest` | `200` `ImportTranslationsResult` | `400` bad `format`, unparseable body (the `detail` names the line/row), invalid `status`, or a narrow file with no `language`; `404` unknown project, or (narrow) `language` not enabled for it |

- **`format`** is **`json`**, **`flat`**, **`csv`** or **`xlsx`**
  (`TranslationFileParser.SupportedFormats`):
  | `format` | Parser |
  |---|---|
  | `flat` | `key=value` lines; `#` comment lines and blank lines ignored; the value is trimmed. **Narrow.** |
  | `json` | a flat `{ "key": "value" }` object, or a nested object flattened with `.` between segments; numbers / booleans stringified, `null` → `""`, arrays rejected. **Narrow.** |
  | `csv` | RFC 4180; body in `content`. Shape from the header row (below). |
  | `xlsx` | first worksheet of an OpenXML workbook; bytes **base64-encoded in `contentBase64`** (not `content`); legacy `.xls` rejected. Shape from the header row (below). |
- **Narrow vs wide** (`csv` / `xlsx` only) — decided by the header row:
  - **narrow** — a `key` column plus a `value` column: each row is `(key, value)`
    for the request's **`language`** (required, as for `json` / `flat`). A narrow
    file with no `language` → `400 "language is required for this format"`.
  - **wide** — a `key` column plus one or more columns whose header is a
    **registered** language code (case-insensitive): each such column imports
    that language and the request's `language` is **ignored**. Optional
    `category` / `description` columns seed a newly-created key; other columns are
    ignored. **A blank cell is a skip, never a delete.**
  A header with neither a `value` column nor a language-code column → `400`.
- **Body content** — `content` carries the text formats (`json` / `flat` /
  `csv`); `contentBase64` carries the `xlsx` bytes. Both `language` and `content`
  are optional on the DTO now (a wide `csv`/`xlsx` needs neither `language` nor,
  for `xlsx`, `content`).
- **Request-body ceiling.** This endpoint opts into
  **`Limits:MaxImportBodyBytes`** (default 5 MB) instead of the 256 KB global
  `Limits:MaxRequestBodyBytes`; an over-cap body is `413` before binding.
  Base64 inflates the `xlsx` payload ~33 %.
- **`language`** (narrow only) must be a registered language that is **enabled**
  for the project (`404` otherwise). Wide language columns are matched against
  the global catalogue only — enabled-for-project is **not** re-checked per
  column.
- Per parsed entry: the `TranslationKey` is **created if missing** — its category
  is a `category` column, else the request `category`, else derived from the key
  name; `createdBy` is the caller identity. The `TranslationString` for
  `(key, language)` is then upserted and walked to **`status`**.
- **`status`** ∈ `Draft` (default) / `InReview` / `Approved`. `Published` and
  `Archived` are rejected with `400`. An existing string is walked to `status`
  even when only its value changed — re-importing with the default knocks a
  non-`Draft` string back to `Draft`.
- A key name outside `[A-Za-z0-9_.-]+` is recorded in `errors` (with the raw
  `key`) and skipped; the rest of the import proceeds. A **parse** failure
  (bad header, unterminated quoted field, not a valid `.xlsx`, bad base64) fails
  the whole request with `400` before anything is written. `errors` line numbers
  are 1-based; for `xlsx` they are the worksheet row (first data row = `2`).
- **`dryRun: true`** computes the plan — counts, `errors`, `keys` — and writes
  nothing.
- `createdStrings` / `updatedStrings` count across **every** language column.
  `skipped` counts entries whose value **and** state already matched. If any
  imported string enters or leaves `Published`, the delivery cache for the
  affected `(project, language)` pairs is invalidated once at the end.
- Full how-to with worked files: [`import-export.md`](import-export.md).

## Management screens

Every screen route is `CanRead` and takes an optional `?project=<code>` query
that scopes it to one project; omitted, it spans every active project (the union
of their enabled languages as columns).

### `GET /api/translations` — the grid

DTOs: `TranslationRowDto`, `TranslationValueDto`, `PagedResult<T>`.

```
TranslationValueDto { value, status, source }
TranslationRowDto   { keyId, key, category, description?,
                      values: { "<languageCode>": { value, status, source }, ... } }
```

| Query | Success | Errors |
|---|---|---|
| `?project=&category=&language=&search=&status=&skip=0&take=50` | `200` `PagedResult<TranslationRowDto>` | `400` invalid `status`; `404` when `project` is given but unknown |

- One row per active key; a cell per column language; a language with no string
  for that key is **absent** from `values`.
- `language` narrows the columns to that one code; otherwise the columns are the
  scoped project's `enabledLanguageCodes` (or the union across all projects).
- `category` is an exact case-insensitive filter. `search` matches the key name
  **or** any of the key's string values (case-insensitive substring).
- **`status`** (optional) is one of the five `ReviewState` names —
  `Draft`, `InReview`, `Approved`, `Published`, `Archived`; any other value is
  `400`. It keeps only rows with **at least one cell** in that state, but each
  kept row still carries **all** its cells. **`Archived` cells are hidden**
  unless `status=Archived` is explicitly requested.
- **`source`** on each cell is provenance: `"app"` when the value is the
  project's own string, or `"shared:<code>"` when it is merged in from a `common`
  project (a project-owned key still wins a name collision). `source` is
  **grid-only** — the consumer delivery payload never carries it.
- `skip` floored at 0; `take` default 50, capped at 200.

### `GET /api/categories`

| Query | Success | Errors |
|---|---|---|
| `?project=` | `200` `string[]` — distinct non-empty categories, ordinal-sorted | `404` when `project` is given but unknown |

### `GET /api/dashboard`

DTOs: `DashboardResponse`, `LanguageCoverageDto`.

```
LanguageCoverageDto { languageCode, languageName, translatedCount, totalKeys, percent, missingCount }
DashboardResponse   { projectCount, languageCount, keyCount,
                      coverage: LanguageCoverageDto[], totalMissing }
```

| Query | Success | Errors |
|---|---|---|
| `?project=` | `200` `DashboardResponse` | `404` when `project` is given but unknown |

- A key counts as **translated** in a language when a `TranslationString` exists
  in **any state other than `Draft` or `Archived`** (`InReview`, `Approved` or
  `Published`).
- `percent` is `translatedCount * 100 / keyCount` rounded to 1 dp (`0` when
  `keyCount` is 0). `coverage` is ordered by `languageCode`. `totalMissing` is
  the sum of `missingCount`.

### `GET /api/translations/missing`

DTO: `MissingTranslationDto`, `PagedResult<T>`.

```
MissingTranslationDto { keyId, key, category, missingLanguages: string[] }
```

| Query | Success | Errors |
|---|---|---|
| `?project=&language=&skip=0&take=50` | `200` `PagedResult<MissingTranslationDto>` | `404` when `project` is given but unknown |

- Only keys with at least one target language that has **no non-`Draft`,
  non-`Archived`** value are returned. `language` narrows the target set to one
  code. `skip` floored at 0; `take` default 50, capped at 200.

### `GET /api/translations/publish/preview` — publish diff

Policy: `CanRead`. DTOs: `PublishPreviewResponse`, `PublishPreviewChange`.

```
PublishPreviewChange   { key, currentValue?, newValue, kind }   // kind: "added" | "changed"
PublishPreviewResponse { project, language, changes: PublishPreviewChange[], addedCount, changedCount }
```

| Query | Success | Errors |
|---|---|---|
| `?project=&language=` | `200` `PublishPreviewResponse` | `400` `project` or `language` missing; `404` unknown / inactive project or language, or language not enabled for the project |

- Shows what a `POST /api/translations/publish` for the **same**
  `(project, language)` would change in the delivered map: it assembles the
  current published map and a hypothetical one (the project's `Approved` strings
  treated as `Published`) and diffs them.
- `kind` is `"added"` (the key is not delivered today) or `"changed"` (a
  delivered value would differ — reached today only through the fallback chain).
- **`language` is required** — there is no all-languages preview.

### `POST /api/translations/publish` — bulk publish

Policy: `CanPublish`. DTOs: `PublishTranslationsRequest`,
`PublishTranslationsResult`.

```
PublishTranslationsRequest { project, language? }
PublishTranslationsResult  { published: int }
```

| Body | Success | Errors |
|---|---|---|
| `PublishTranslationsRequest` | `200` `PublishTranslationsResult` | `404` (ProblemDetails) unknown project or unknown `language` |

- Promotes **every `Approved` string** for the project (and language, when
  given) to `Published` via the normal `Approved → Published` transition, writes
  a `Published` audit entry per string, and invalidates the delivery cache for
  the affected languages.
- Publishing a **`common`** project fans the invalidation out to every project's
  cache entry for those languages.
- `published` is the number of strings promoted (`0` when nothing was `Approved`
  — not an error).

## History / audit trail

Read-only projection of the append-only audit log. Policy: `CanRead`. DTO:
`AuditEntryDto`. **Not exposed to consumers.**

```
AuditEntryDto { id, projectId, entityType, entityId, action, actor, timestamp,
                fromState?, toState?, detail?, oldValue?, newValue? }
```

- `action` is an `AuditAction` name (`Created`, `Edited`, `Submitted`,
  `Approved`, `Rejected`, `Reopened`, `Published`, `Archived`, `Unarchived`).
- `fromState` / `toState` are `ReviewState` names when the operation changed
  review state.
- `oldValue` / `newValue` carry the string value diff: `newValue` on `Created`,
  both on `Edited`, both null on review transitions.

| Method & route | Query | Success | Errors |
|---|---|---|---|
| `GET /api/projects/{project}/history?skip=0&take=50` | `skip` floored at 0; `take` default 50, capped at 200 | `200` `PagedResult<AuditEntryDto>`, newest first | `404` unknown project |
| `GET /api/projects/{project}/keys/{keyId:guid}/strings/{language}/history` | — | `200` `AuditEntryDto[]` for that one string, newest first | `404` if the string does not exist |

---

## Health

| Route | Purpose | Checks |
|---|---|---|
| `GET /health` | Liveness | none — `200` with a health-report body while the process runs. Opts out of rate limiting. |
| `GET /health/live` | Liveness | none — same as `/health`. |
| `GET /health/ready` | Readiness | `MongoHealthCheck` (name `database`, tag `ready`) — `{ ping: 1 }` against the configured database. `200` ready / `503` not. **No Redis check** — the delivery cache degrades to on-demand assembly if Redis is down. |
