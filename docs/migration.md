# Migrating existing translation data into CTMS

Spec §44. How to move an existing solution's old translation data into CTMS
without breaking the old system. **Do not auto-delete production data** — identify
it, map it, import it, validate, and only then retire the old structures.

---

## 1. Identify the old data

Catalogue what you have before touching anything:

- **Old collections / tables / files** — a legacy `translations` Mongo
  collection, a SQL `Resource` table, `.resx` files per assembly, JSON i18n
  bundles per app, spreadsheet exports.
- **Old models** — how a translation row is shaped (key, culture, value, state?),
  and where the "which app" and "which language" live.
- **What depends on them** — which services read the old store directly, which
  build pipelines embed `.resx`, which apps ship JSON bundles.

Write this down (a short doc or a spreadsheet). It is the input to the mapping
step and the checklist for the retirement step.

## 2. Map the old shape onto the CTMS model

| Old concept | CTMS |
|---|---|
| An application / site / bounded context | a **`Project`** (`POST /api/projects`). Its `code` (slug) is what consumers pass on `GET /api/translations/{project}/{language}`. |
| Cross-app "shared" / "global" strings | one `Project` with `isCommon: true` (conventionally `common`). Its published strings merge into every project's delivered map; a project's own key of the same name wins. |
| A culture / locale (`en`, `en-GB`, `fr_FR`, `1033`) | a **`Language`** (`POST /api/languages` or `/api/languages/bulk`), keyed by a **BCP-47** `code` (`en-GB`, `fr-FR`). Normalise `_` → `-`; map LCIDs and bare `en` to a real tag. Set `fallbackCode` to build the chain (`fr-CA` → `fr-FR` → `en-GB`) and `isRtl` for Arabic / Hebrew / Persian. |
| A resource key (`Checkout_Submit`, `checkout.button.submit`, `Views/Home/Index.Title`) | a **`TranslationKey`** `keyName` — must match `[A-Za-z0-9_.-]+`. Prefer dotted paths; the segment before the first `.` becomes the auto-derived **category** (`checkout.button.submit` → `Checkout`) unless you pass `category` explicitly. |
| A translated value | a **`TranslationString`** for `(key, language)`. |
| A state column (`Draft`, `Reviewed`, `Live`) | map onto `ReviewState`. Import at `Draft` / `InReview` / `Approved`; **`Published` is not importable** — publish through the review workflow after validation. |
| A "last modified by" column | passed as the caller identity / `updatedBy`; recorded in the audit trail. |
| Numeric version columns | **dropped** — CTMS has no numeric translation versioning (spec §27). Change history is the audit trail. |

## 3. Import

### Path A — the bulk import endpoint (preferred)

`POST /api/projects/{project}/import` — one file per `(project, language)`:

```json
{
  "format": "json",
  "language": "fr-FR",
  "content": "{ \"checkout.button.submit\": \"Payer\", \"common.cancel\": \"Annuler\" }",
  "category": null,
  "status": "InReview",
  "dryRun": true
}
```

- **`format` is `json` or `flat` only** (`key=value` lines). CSV and RESX are
  **not** supported — pre-convert:
  - `.resx` → JSON: read `<data name>`/`<value>` pairs (skip entries with a
    `type=` / `mimetype=` attribute), emit `{ name: value }`.
  - spreadsheet / CSV → flat: one `key=value` per row.
- **Run with `dryRun: true` first** — it returns `createdKeys` / `createdStrings`
  / `updatedStrings` / `skipped`, a per-row `errors` list (bad key names), and up
  to 200 key names, and writes nothing.
- `language` must already exist **and** be enabled for the project.
- Body-size ceiling for this route is `Limits:MaxImportBodyBytes` (default 5 MB);
  split a larger file by language or by key prefix.
- See [`api.md` → Bulk import](api.md#bulk-import).

### Path B — a one-off script

For a store the import endpoint cannot express (per-key metadata, selective
migration, incremental sync), script it against the management API:

1. `POST /api/languages/bulk` — register the languages with fallback chains.
2. `POST /api/projects` — create each project (+ the `common` one with
   `isCommon: true`), enabling its languages.
3. For each `(key, language, value)`:
   `PUT /api/projects/{project}/keys/{keyId}/strings/{language}` (creates the key
   first with `POST .../keys` if new).
4. Optionally `POST /api/projects/{project}/review-bulk` to move a whole
   language / category to `Approved`.

The script is idempotent-friendly: an unchanged string upsert is a no-op, an
existing key returns `409` (catch and continue), `languages/bulk` skips existing
codes.

## 4. Validate

- `GET /api/dashboard?project=<code>` — per-language coverage %, `totalMissing`.
- `GET /api/translations/missing?project=<code>` — keys still lacking a value.
- `GET /api/translations?project=<code>&status=Approved` — eyeball the grid; the
  `source` tag shows which cells are inherited from `common`.
- `GET /api/translations/publish/preview?project=<code>&language=<lang>` — the
  `added` / `changed` diff a publish would make to the delivered map.
- Compare a sample of `GET /api/translations/{project}/{language}` responses
  against what the old system serves for the same keys.

## 5. Publish and cut over

1. `POST /api/translations/publish` (`{ project, language? }`) — promotes every
   `Approved` string to `Published` and invalidates the delivery cache.
2. Point one consumer at CTMS (`GET /api/translations/{project}/{language}` for
   an external app, or `ITranslationService` for an internal .NET service — see
   [`internal-consumption.md`](internal-consumption.md) /
   [`external-consumption.md`](external-consumption.md)). Run both old and new in
   parallel first if you can.
3. Migrate the remaining consumers.

## 6. Retire the old structures

Only after every consumer is on CTMS and has been stable for a sensible bake-in:

1. Re-check the dependency list from step 1 — nothing still reads the old store.
2. Snapshot / back up the old data.
3. Remove the old read paths from services and pipelines.
4. Drop the old collections / tables / bundled files.

Keep the backup until you are certain. CTMS's own audit trail is not a substitute
for a backup of the source system.
