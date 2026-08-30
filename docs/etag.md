# ETag / conditional requests

The consumer delivery response carries a **content-hash ETag**. There is **no
numeric version anywhere** in CTMS (spec §27–§28) — change detection is the
ETag, `UpdatedAt`, and the audit trail.

---

## The hash

`TranslationContentHash.Compute(map)`
(`CTMS.Application/Translations/TranslationContentHash.cs`):

1. Order the entries by key, **ordinal**.
2. For each entry append `key`, `"\n"`, `value`, `"\n"` to a buffer.
3. Hash = **lowercase-hex SHA-256** of that buffer's UTF-8 bytes.

Two assemblies with identical content produce byte-identical hashes; any change
to any included value — or an added / removed key — changes the hash.

It is computed over the **fully resolved** map: this project's `Published`
strings, merged with every `common` project's `Published` strings (project value
wins a collision), with gaps filled from the language `FallbackCode` chain. So
the ETag changes when:

- a common translation in the bundle changes;
- a project translation in the bundle changes;
- a fallback translation that is currently filling a gap changes;
- a published translation is added to the bundle;
- a published translation is removed (unpublished / archived / key deactivated).

It does **not** change for edits to `Draft` / `InReview` / `Approved` /
`Archived` strings that are not currently in the delivered map, because those are
never part of the assembled result.

## HTTP flow — `GET /api/translations/{project}/{language}`

`TranslationEndpoints`:

- Every `200` **and** every `304` sets:
  - `ETag: "<hash>"` — the raw lowercase-hex hash wrapped in double quotes, a
    **strong** validator;
  - `Cache-Control: no-cache` — a client / shared cache may store the body but
    must revalidate before reuse.
- On a request with `If-None-Match`, `ConditionalRequest.IsNotModified` compares
  the header against the raw hash and the endpoint answers **`304 Not Modified`
  with no body** on a match, or a normal `200` with the new `ETag` and body on a
  mismatch.

### `If-None-Match` matching

`ConditionalRequest.IsNotModified` accepts:

- the quoted form `"abc123"`;
- an optional weak prefix `W/"abc123"` (the `W/` is stripped, then compared);
- a comma-separated list `"a", "b", "c"` (safe to split on `,` — a SHA-256 hex
  string never contains one);
- the header repeated across multiple values;
- `*` — matches whenever a map exists.

```
GET /api/translations/icoach/fr-FR
→ 200 OK
  ETag: "9f2b…c1"
  Cache-Control: no-cache
  { "project": "icoach", "language": "fr-FR", "translations": { … } }

GET /api/translations/icoach/fr-FR
If-None-Match: "9f2b…c1"
→ 304 Not Modified
  ETag: "9f2b…c1"
  (no body)

# after a publish changes a value:
GET /api/translations/icoach/fr-FR
If-None-Match: "9f2b…c1"
→ 200 OK
  ETag: "4d7a…e0"
  { … updated map … }
```

## In-process

`ITranslationService.GetTranslationsAsync` returns the same hash as
`TranslationBundle.ETag`. An internal consumer can compare it against a
previously stored value to decide whether to re-render / re-cache — the same
change-detection signal, no HTTP.

## Interaction with the cache

The Redis (or in-process) cache stores `{ translations, hash }` together
(`CachedTranslations`), so a cache hit answers the `304` / `200` decision
without re-assembling or hashing. A publish invalidates the cache entry; the
next request re-assembles and computes a fresh hash. See
[`caching.md`](caching.md).

## Related

- [`external-consumption.md`](external-consumption.md) — client-side ETag
  round-trip examples.
- [`maui-client.md`](maui-client.md) — the `CTMS.Client` revalidation state
  machine.
