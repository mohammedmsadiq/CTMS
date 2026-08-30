# 5. First-run experience and the machine-integration surface

Date: 2026-08-30

## Status

Accepted

Builds on [ADR 0004](0004-assemble-on-demand-delivery-and-model-simplification.md)
(global languages, assemble-on-demand delivery, categories on keys). It does not
supersede any earlier ADR; the auth model in
[ADR 0003](0003-production-hardening.md) / the security section of the
architecture doc is extended, not replaced.

## Context

After ADR 0004 the data model fit the product, but two rough edges remained.

**Standing up a new application was tedious and error-prone.**

- Every application had to have its languages hand-entered one `POST /api/languages`
  at a time before anything else could happen, even though the useful set is a
  dozen well-known BCP-47 locales.
- Every `TranslationKey` required an explicit `category`. Callers either
  invented one per key or dumped everything into a single bucket; the category
  was meant to *help* browsing, not to be a required ceremony on every create.
- Teams migrating an existing app already had their strings — in `.resx`, in a
  JSON i18n bundle, in a spreadsheet export — and no way to get them in except a
  script hitting the string upsert key by key.
- Bulk review (approve a whole category, submit a language) and "what will this
  publish actually change?" had no endpoint; reviewers worked string by string
  and published blind.

**Machine consumers were second-class.**

- The only way to authenticate was an Entra token. A CI job or a server-rendered
  site that just needs to *read* published translations had to run the full
  confidential-client OAuth flow, or the deployment had to flip
  `Auth:PublicBundleReads` and expose the delivery reads to everyone.
- The only way to notice a publish was to poll `GET /api/translations/{app}/{lang}`
  with `If-None-Match`. Fine for a CDN edge; wasteful for a consumer that would
  rather be told.

## Decision

### Lower the barrier to adding content

- **Optional category with prefix derivation.** `category` becomes optional on
  `POST /api/applications/{application}/keys`. When it is null/blank the service
  derives one from the key name: the segment before the first `.`, title-cased
  (`course.start` → `Course`), or `General` when there is no usable prefix
  (`CategorySuggestion.FromKeyName`). The domain still always stores a non-blank
  category; `PATCH` still requires an explicit value.
- **A first-run wizard backed by a shipped catalogue.** A static ~38-entry
  BCP-47 table (`LanguageCatalogue`, code-only, never persisted) is served at
  `GET /api/languages/suggestions`. `POST /api/languages/bulk` registers a list
  of `{ code, name, fallbackCode?, isRtl? }` in one idempotent call (existing
  codes are skipped, not errored). The Admin UI new-application wizard uses the
  two together so a new app goes from nothing to "languages enabled" in one step,
  instead of N hand-typed `POST /api/languages` calls.
- **Bulk file import as the migration path.** `POST /api/applications/{application}/import`
  takes a whole file — `flat` (`key=value`), RFC-4180 `csv`, flat-or-nested
  `json`, or `.resx` — plus a target `language` and `status`. It creates any
  missing key (category = the request value, else derived), upserts a string per
  entry, reports a per-row error list, and supports `dryRun`. The parsers are
  HTTP-free and independently testable.
- **Bulk review + a publish diff-preview.**
  `POST /api/applications/{application}/review-bulk` applies one review action
  across a filtered set (language / category / keyIds — at least one required),
  skipping illegal transitions rather than failing.
  `GET /api/translations/publish/preview` assembles the current delivered map and
  the hypothetical post-publish map and returns the `added` / `changed` diff, so
  a publish is a reviewed action.

### Give machines a first-class way in and out

- **API keys for authenticated machine reads.** An `X-Api-Key` header
  authenticates a read-only principal (role `ctms.reader`, `CanRead` only),
  through a scheme composed with the JWT bearer scheme so either credential
  works. Keys are stored hashed (`ApiKey` aggregate, collection `apiKeys`), shown
  raw exactly once, and track `LastUsedAt`. Managed at
  `POST` / `GET` / `DELETE /api/api-keys` (`CanAdminProjects`). Active whenever
  `Auth:Enabled=true`. This replaces "make every CI job do OAuth" and "open the
  delivery reads to the world" with a revocable, auditable, read-only credential.
- **Publish webhooks for push notification.** A `Webhook` aggregate (collection
  `webhooks`) stores a URL and a signing secret (shown once). On every publish an
  async, retrying `BackgroundService` `POST`s
  `{ event: "published", application, language, etag, publishedAt }` with
  `X-CTMS-Signature: sha256=<HMAC-SHA256(secret, raw body)>`. Delivery is
  best-effort: `Webhooks:MaxAttempts` (default 3) tries at
  `Webhooks:TimeoutSeconds` (default 5) each, then the event is dropped. Dispatch
  is fire-and-forget — a webhook failure never affects the publish.
  `Webhooks:Enabled` (default true) is the off switch.

## Consequences

### Positive

- A new application is minutes of clicking, not a scripted sequence of API calls;
  a migrating team imports its existing files directly.
- Keys can be added with just a name; the category still comes out useful because
  key names are already dotted paths.
- Reviewers act in bulk and see a publish's effect before committing it.
- CI jobs and SSR sites authenticate with a single revocable header value and
  can be told about changes instead of polling.
- API keys are read-only by construction, so leaking one exposes only published
  translations that a `Auth:PublicBundleReads=true` deployment would serve
  anonymously anyway.

### Negative / risks

- **API keys are a second credential type.** They must be rotated, audited and
  revoked separately from Entra identities, and a hashed secret in the database
  is one more thing to protect. Mitigation: read-only scope, `LastUsedAt` for
  staleness detection, immediate revoke on `DELETE`.
- **Webhooks are best-effort.** After `MaxAttempts` the event is gone; a consumer
  that treats the webhook as the source of truth will miss updates during an
  outage. The contract is explicitly "a nudge to revalidate" — consumers must
  still fall back to a conditional `GET`. Signature + a bounded `publishedAt`
  blunt spoofing and replay but the consumer has to implement both checks.
- **The importer trusts the caller's `category` and `status`.** It will happily
  mark a whole file `Approved` or file every key under one category; there is no
  server-side sanity check beyond "not `Published`". Dry-run is the guard rail,
  and the audit trail records every created/edited string.
- **`review-bulk` and `bulk` language register can touch a lot of rows in one
  call.** Both are single-request, non-transactional (per ADR 0002 / 0004): a
  mid-batch failure leaves partial work. They are idempotent enough to re-run,
  and the mandatory filter on `review-bulk` keeps the blast radius deliberate.
- **The language catalogue is a hard-coded list.** Adding a locale to the wizard
  picklist is a code change and a release. Judged acceptable — it is a
  convenience list, and `POST /api/languages` / `/bulk` accept any BCP-47 code
  regardless of what the catalogue offers.
