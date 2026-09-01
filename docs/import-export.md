# Import / export — translator work files

CTMS can hand a project's translations out as a spreadsheet and take the edited
sheet back. This is the offline path behind the translation grid (spec §34): a
manager exports, a translator fills cells in Excel or a CSV editor, the manager
re-imports, and the values flow into the normal
`Draft → InReview → Approved → Published` workflow
([`translation-workflow.md`](translation-workflow.md)).

- Export: [`GET /api/projects/{project}/export`](api.md#bulk-export) — policy `CanRead`.
- Import: [`POST /api/projects/{project}/import`](api.md#bulk-import) — policy `CanManageContent`.

Both live in `CTMS.Application/Translations` (`Export/` and `Import/`); the HTTP
endpoints only move bytes.

---

## File shapes

A CSV or XLSX file is read in one of two shapes, decided **by the header row**:

| Shape | Header row | What each data row means | Request `language` |
|---|---|---|---|
| **wide** | a `key` column **plus one or more columns whose header is a registered language code** | upsert the key's value for *every* language column that has a non-blank cell | ignored — the column headers carry the language |
| **narrow** | a `key` column **plus a `value` column** (no language-code columns) | upsert `(key, value)` for the one language named in the request | **required** (as for `json` / `flat`) |

`json` and `flat` are always narrow. If a table header has neither a `value`
column nor any language-code column the import is rejected `400`.

### Wide CSV — worked example

```csv
key,category,description,en-GB,fr-FR
common.save,Common,Primary save action,Save,Enregistrer
common.cancel,Common,,Cancel,Annuler
course.start,Course,,Start course,
```

- Row 1 upserts `common.save` for both `en-GB` and `fr-FR`.
- Row 3 (`course.start`) has a value for `en-GB` only; the empty `fr-FR` cell is
  **skipped** — it is never read as "delete the French value".
- `category` / `description` are used **only when the import creates the key**;
  for a key that already exists they are ignored.

Request body:

```json
{ "format": "csv", "content": "key,category,description,en-GB,fr-FR\ncommon.save,Common,...", "status": "InReview", "dryRun": true }
```

No `language` — the `en-GB` / `fr-FR` headers are the languages.

### Narrow CSV — worked example

```csv
key,value
common.save,Enregistrer
common.cancel,Annuler
```

```json
{ "format": "csv", "language": "fr-FR", "content": "key,value\ncommon.save,Enregistrer\n...", "status": "InReview" }
```

`language` is mandatory here; omit it and the response is
`400 "language is required for this format"`.

### XLSX

Same column layout as CSV, read from the **first worksheet**, header on the
first used row. Supply the file **bytes base64-encoded in `contentBase64`**
(not `content`):

```json
{ "format": "xlsx", "contentBase64": "UEsDBBQABgAI...", "status": "InReview", "dryRun": true }
```

Only `.xlsx` (OpenXML) is accepted — legacy `.xls` is not. An exported XLSX
re-imports as a **wide** file (its header row carries the language codes).

## Column meanings

| Column | On export | On import |
|---|---|---|
| `key` | the `TranslationKey.KeyName` | required; must match `[A-Za-z0-9_.-]+`, else the row is reported in `errors` and skipped |
| `category` | the key's stored category | seeds a **newly-created** key's category; ignored for an existing key (a `category` in the file wins over the request `category`) |
| `description` | the key's description (blank if none) | seeds a newly-created key's description; ignored for an existing key |
| *language code* (e.g. `fr-FR`) | the key's current value in that language, **any review state**, blank when absent | wide only: upsert that language; a **blank cell is a skip, never a delete** |
| `value` | *(not exported)* | narrow only: the value for the request's `language` |
| anything else | — | ignored |

Import cannot delete a string or set `Published` / `Archived`. To remove or
retire a value use the string endpoint and the review workflow.

## Export query parameters

`GET /api/projects/{project}/export`

| Param | Required | Meaning |
|---|:--:|---|
| `format` | yes | `csv` or `xlsx`; any other value (or omitted) → `400` |
| `language` | no | one BCP-47 code — emit just that language column; omitted ⇒ one column per code in the project's `enabledLanguageCodes` |
| `category` | no | only keys in this category (exact, case-insensitive) |
| `status` | no | only keys with **at least one** string in this `ReviewState` (`Draft` / `InReview` / `Approved` / `Published` / `Archived`); any other value → `400` |
| `includeInactiveKeys` | no | default `false`; `true` also emits inactive keys |

Rows are the keys the project **owns**, ordered by key name. A `common` project's
keys are *not* merged into another project's export — export `common` itself to
edit shared strings. Each language cell is the **current** value in that language
regardless of review state, so the file is a translator work file, not the
published bundle. Unknown / inactive project → `404`.

- **CSV** — `text/csv; charset=utf-8`, RFC 4180 quoting, `\r\n`, a leading UTF-8
  BOM, `Content-Disposition: attachment; filename="{project}-translations.csv"`.
- **XLSX** — `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`,
  one worksheet `Translations`, bold + frozen header row, frozen first column,
  auto-sized columns, `filename="{project}-translations.xlsx"`.

## Send to a translator, get it back

1. **Export.** `GET /api/projects/nimbus/export?format=xlsx&language=fr-FR`
   (drop `language` to send every column at once). Mail the translator the file.
2. **Translate.** They fill the `fr-FR` column in Excel and send it back. Cells
   they leave blank stay untouched on import.
3. **Dry-run.** Base64-encode the `.xlsx` and
   `POST /api/projects/nimbus/import` with
   `{ "format": "xlsx", "contentBase64": "…", "status": "InReview", "dryRun": true }`.
   The response is the plan — `createdKeys`, `createdStrings`, `updatedStrings`,
   `skipped`, an `errors` list, and up to 200 affected key names. Nothing is
   written.
4. **Import.** Repeat without `dryRun`. Wide file ⇒ no `language` needed.
5. **Review.** The upserted strings are at the `status` you chose (`Draft` by
   default). Approve and publish through the normal workflow, or use
   `POST /api/projects/{project}/review-bulk`.

Round-trip note: a changed cell is an edit — an existing string is walked to the
import `status`. With the default `status` (`Draft`) a previously `InReview` /
`Approved` / `Published` cell is walked **back to `Draft`** even if only one
other cell in the row changed. Pass `status: "InReview"` (or `Approved`) when you
want the edited strings to land ready for a reviewer.

## Size limit

The import request body is capped at `Limits:MaxImportBodyBytes` (default 5 MB;
an over-cap body is `413` before binding). Base64 inflates bytes by ~33 %, so the
practical XLSX ceiling is ~3.7 MB of workbook. Split a bigger job by language or
by key prefix. Export has no size cap.

## Admin UI

The Admin UI drives both ends of this round-trip: an **Export** control on the
project's translation grid, and the project **Import** screen
(`/projects/{code}/import`) which takes a pasted body or an uploaded file, offers
format / language / initial-status pickers, and always previews (dry-run) before
it writes.

## Gotchas

- A wide column header only counts as a language if that code is **registered**
  (`POST /api/languages` first). An unrecognised header is treated as a normal
  column and ignored — which can silently turn a file you meant as wide into a
  narrow one (then `400 "language is required for this format"`).
- Enabled-for-project is enforced for a **narrow** import (`404` if the language
  is not in `enabledLanguageCodes`) but **not** per-column for a wide import —
  enable the languages on the project (`PUT /api/projects/{code}/languages/{lang}`)
  or the imported strings will never reach the delivered bundle.
- `errors` row numbers are 1-based; for XLSX they are the worksheet row, so the
  first data row is `2`.
- A parse failure (bad header, unterminated quoted field, not a valid `.xlsx`,
  `contentBase64` not valid base64) fails the whole request `400` before
  anything is written. A bad **key name** only drops that row.

## See also

- [`api.md` → Bulk export](api.md#bulk-export) / [Bulk import](api.md#bulk-import)
- [`translation-workflow.md`](translation-workflow.md) — what happens to the
  strings after import
- [`migration.md`](migration.md) — first-time bulk load from a legacy store
