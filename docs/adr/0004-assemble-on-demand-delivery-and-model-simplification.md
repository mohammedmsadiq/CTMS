# 4. Assemble-on-demand delivery and model simplification

Date: 2026-08-30

## Status

Accepted

Revises the data model and delivery mechanism described in
[ADR 0002](0002-mongodb-as-primary-store.md) (per-project locales, immutable
versioned bundles, the `TranslationString` concurrency token) and the
bundle-delivery path referenced in
[ADR 0003](0003-production-hardening.md). It supersedes neither formally; both
remain **Accepted** and the parts they describe that are not touched here still
stand.

## Context

After the MongoDB switch (ADR 0002) the model still carried shapes inherited from
the first relational scaffold, and the delivery mechanism it shipped in WS3/WS4
turned out to fit the product poorly:

- **Locales were per project.** Every application re-declared `en`, `fr`, `ar`,
  … as its own `Locale` rows, with per-project display names and RTL flags. In
  practice the language set is an organisation-wide catalogue, and the same
  language means the same thing in every application.
- **No cross-application sharing.** Common UI strings ("Save", "Cancel") were
  copied into every application. There was no way to maintain them once.
- **Bundles were immutable, versioned snapshots.** A `TranslationBundle` was cut
  per `(project, locale)` with a monotonic `Version`, stored as a denormalised
  document, and served from a Redis entry keyed by version. Publishing was a
  distinct, heavyweight step; clients had to reason about version numbers and a
  `/versions` history they rarely used; a two-stage "promote each string to
  `Published`, then cut a bundle" workflow was confusing.
- **Optimistic concurrency on `TranslationString`.** A `Version` token
  (PostgreSQL `xmin`, then an app-managed `long`) guarded string edits, surfacing
  as `expectedVersion` in, `version` out, and `409` + `currentVersion`. The admin
  UI is the only writer, edits are already funnelled through the review workflow,
  and the token was mostly friction.
- **Keys had no grouping.** The admin UI needed categories, per-language coverage
  numbers, and a "what is still missing" view; none of the data model supported
  them.
- **History showed transitions but not content.** An `Edited` audit entry
  recorded the from/to review state, not what the text changed from and to.

## Decision

### Global languages, per-application enablement

Replace the per-project `Locale` aggregate with a single global **`Language`**
collection (`languages`, unique index `{ code: 1 }`):

- `Code` (BCP-47, e.g. `en-GB`), `Name`, `FallbackCode?` (another language's
  code), `IsRtl`, `Active`.
- A `Language` may name a `FallbackCode`, forming a chain
  (`fr-CA` → `fr-FR` → `en-GB`). A language cannot fall back to itself.
- Each application declares which languages it uses in
  `Project.EnabledLanguageCodes`; enabling a language validates that it exists
  and is active.

### `Project` is an application

The aggregate keeps the type name `Project` but models a translatable
**application**:

- `Slug` is the application **code** used on the client delivery routes
  (`/api/translations/{application}/{language}`) and on every management route
  (`/api/applications/{code}/...`).
- `BaseLanguageCode` (was `BaseLocaleCode`).
- New: `IsShared` — a shared application (e.g. `common`) whose published
  translations merge into every other application's delivered map;
  `Active` — inactive applications are hidden from delivery;
  `EnabledLanguageCodes`.

### Categories on keys

`TranslationKey` gains `Category` (**required** — `Common`, `Navigation`,
`Course`, …), `Active` (inactive keys are excluded from delivery and coverage),
and `CreatedBy`. A non-unique index `{ projectId: 1, category: 1 }` backs
category filtering.

### Remove all version numbers

- **`TranslationBundle` is deleted** — the entity, the `translationBundles`
  collection and its unique index, `TranslationBundleService`, and the
  `POST/GET .../bundles/...` and `.../versions[...]` endpoints.
- **`TranslationString.Version` is deleted**, along with `expectedVersion` in the
  upsert request, `version` in the DTO, `ConcurrencyException`, the `409` +
  `extensions.currentVersion` response, and the EF-era
  `DbUpdateConcurrencyException` mapping. **String upsert is last-write-wins.**
- `TranslationString` is now keyed by `LanguageCode` (a string) instead of a
  `LocaleId` GUID; the unique index is `{ translationKeyId: 1, languageCode: 1 }`.
- `ReviewState` is exposed in DTOs under the name `status`.

### Assemble-on-demand delivery

`GET /api/translations/{application}/{language}` returns a flat
`{ application, language, translations: { key: value } }`. The value set is
assembled per request by `PublishedTranslationsService.GetPublishedAsync`:

1. resolve the application (404 unknown/inactive) and language (404
   unknown/inactive, or not in the application's `EnabledLanguageCodes`);
2. gather `TranslationString`s with `ReviewState == Published` for this
   application's active keys **plus every `IsShared` application's** active keys —
   on a key-name collision the **application-specific value wins**;
3. for a key with no published value in the requested language, walk the
   `Language.FallbackCode` chain (cycle-guarded) and take the first published
   value found; a key with no published value anywhere is **omitted**;
4. return the map ordered by key.

The response carries `ETag: "<hash>"` where the hash is a lowercase-hex SHA-256
over the ordered entries (`TranslationContentHash.Compute`, the same algorithm
the old bundle ETag used) — **there is no version number anywhere**. The route
sets `Cache-Control: no-cache` and answers `304 Not Modified` to a matching
`If-None-Match`.

A Redis read-through cache holds the serialized map plus its hash under
`translations:{applicationCode}:{languageCode}` (both lower-cased), TTL
`Cache:TranslationsTtlMinutes` (default 60). When `ConnectionStrings:Redis` is
unset an in-process distributed-memory cache is used instead. The cache is
invalidated whenever a string enters or leaves `Published` (a per-string review
transition, or an edit that knocks a `Published` string back to `NeedsReview`)
and on bulk publish. **Invalidating a shared application fans out** to every
application's cache entry for the affected languages.

Publishing is now a single action: `POST /api/translations/publish`
(`{ application, language? }`) promotes every `Approved` string for the
application (optionally one language) to `Published`, writes audit entries, and
invalidates the cache.

### Value-diff history

`AuditEntry` gains `OldValue?` / `NewValue?`: `NewValue` is set on `Created`,
both are set on `Edited`, and both are null on review transitions. `AuditEntryDto`
carries them through so history shows what the text changed from and to.

### New management surface

`GET /api/translations` (grid rows), `GET /api/categories`, `GET /api/dashboard`
(per-language coverage %), `GET /api/translations/missing`, and
`POST /api/translations/publish`. Key / string / review / history routes are
rebased under `/api/applications/{code}/...` and keyed by `{language}`.
`GET /api/languages` and `GET /api/applications` expose the catalogues.

Coverage / "missing" define a key as **translated** in a language when a
`TranslationString` exists in **any non-`Draft` state**.

## Consequences

### Positive

- One language catalogue, maintained once; one place for a shared string set.
- No version bookkeeping — no `expectedVersion` handshakes, no `/versions`
  history, no two-stage promote-then-cut publish.
- The client picks a single language and the server resolves the fallback chain;
  the SDK no longer maintains a locale fallback walk of its own for server data.
- The delivered map is always current the moment a publish invalidates the cache
  — there is no stale "latest bundle" pointer to advance.
- Categories, a coverage dashboard, and a missing-translations view fall out of
  the model directly.
- History shows content diffs, not just state changes.

### Negative / risks

- **Last-write-wins can silently overwrite a concurrent edit.** Two editors
  saving the same `(key, language)` — the later write wins with no conflict.
  Mitigations: the admin UI is the only writer; editing any non-`Draft` string
  drops it back to `NeedsReview`, so an overwrite of reviewed text is caught at
  re-review; and the audit trail records `OldValue`/`NewValue` for every edit, so
  an overwrite is recoverable.
- **A shared-application publish invalidates every application's cache** for the
  affected languages. With many applications this is a burst of invalidations and
  a wave of cache-miss re-assembly on the next request per `(app, language)`.
- **On-demand assembly does more work per cache miss** than serving a stored
  bundle blob: several collection reads (this app's keys, shared apps' keys,
  their published strings), the per-key fallback walk, and the content hash.
  Mitigated by the read-through cache (a hit needs no assembly and no database
  round-trip) and by the ETag/`304` path for revalidating clients.
- **Referential integrity stays the application's job** (as in ADR 0002): a
  `TranslationString.LanguageCode` is a bare string; nothing at the database
  level ties it to a `Language` row or to the application's enabled set. The
  services validate on write.
