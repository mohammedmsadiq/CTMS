# Troubleshooting

Common issues and what to check. Config keys are in
[`architecture.md` §10](architecture.md#10-configuration-and-secrets).

---

## `GET /health/ready` returns `503`

Readiness runs `MongoHealthCheck` — `{ ping: 1 }` against the configured
database. A `503` means MongoDB is unreachable or misconfigured.

- Is MongoDB up? `docker compose ps`; `mongosh mongodb://localhost:27017 --eval 'db.adminCommand("ping")'`.
- Is `ConnectionStrings:CtmsDatabase` / `ConnectionStrings__CtmsDatabase`
  correct? Inside compose the host is `mongo`, not `localhost`.
- Cosmos DB for MongoDB (RU) needs `retrywrites=false` in the connection string
  and the right `authMechanism`.
- Redis being down does **not** cause a `503` — there is no Redis readiness
  check (delivery degrades to on-demand assembly).
- `GET /health` and `/health/live` stay `200` while the process runs regardless
  of MongoDB — only `/health/ready` gates on it.

## `401` on management routes

- `Auth:Enabled=true` (the default outside `Development` / the dev compose
  stack) requires a valid Entra ID bearer token. `401` = no token or an invalid
  one; `403` = a valid token whose `roles` claim satisfies no policy.
- Check `AzureAd:TenantId` / `:ClientId` / `:Audience` match the token issuer and
  audience.
- Locally, run with `Auth__Enabled=false` (synthetic all-roles principal). This
  is **refused at startup** under `ASPNETCORE_ENVIRONMENT=Production` — see the
  next item.
- The consumer read `GET /api/translations/{project}/{language}` (and the two
  catalogue list reads) is anonymous while `Auth:PublicBundleReads=true`; set it
  to `false` and those need `CanRead` too.
- Role/policy matrix: [`authorisation.md`](authorisation.md). A `Translator` *can*
  `submit` via `POST .../review` (not approve/reject/etc.); one documented §46
  widening remains — a `Reviewer` can also edit string values.

## Startup throws "Auth:Enabled=false is not permitted … Production"

`Auth:Enabled=false` + `ASPNETCORE_ENVIRONMENT=Production` is refused by
`AddCtmsAuth` (and by the Admin UI). Either remove the override or configure the
`AzureAd` section and set `Auth:Enabled=true`. Covered by
`ProductionStartupTests`.

## `GET /api/translations/{project}/{language}` returns an empty `translations` map

The map only contains **`Published`** strings (plus `common` and fallback). An
empty map means nothing is published for that project + language + fallback
chain.

- `GET /api/translations/publish/preview?project=<p>&language=<l>` — shows what a
  publish would add.
- `GET /api/translations?project=<p>&status=Approved` — strings sitting at
  `Approved`, waiting to be published.
- `POST /api/translations/publish` (`{ project, language? }`) to promote them.
- Check the language is in the project's `enabledLanguageCodes` (otherwise the
  route is `404`, not an empty `200`).
- Check keys are `active` — inactive keys are excluded.

## `GET /api/translations/{project}/{language}` returns `404`

Bare `404` (no body) means: unknown or inactive **project**; unknown or inactive
**language**; or the language is **not enabled** for the project. Check
`GET /api/projects/{code}` and its `enabledLanguageCodes`, and
`GET /api/languages/{code}` `active`.

## A publish did not change what consumers see (stale bundle)

- The delivery result is cached under `translations:{project}:{language}`
  (lower-cased). A publish / review transition that enters or leaves `Published`
  invalidates it; verify the operation actually changed review state (a no-op
  edit does not).
- With Redis: check the key is gone —
  `redis-cli --scan --pattern 'translations:*'` then `GET`. If Redis is
  unreachable the invalidation is logged as a warning and skipped, but reads also
  bypass a dead cache, so this only bites a *flaky* Redis.
- A **`common`** publish must fan out to every project × affected languages —
  confirm the `common` project has `isCommon: true`.
- TTL is `Cache:TranslationsTtlMinutes` (default 60) — a stale entry self-heals
  within that window even if an invalidation was missed.
- Compare `ETag` before/after: same hash ⇒ the assembled content genuinely did
  not change.

## `409 Invalid review transition`

The `action` verb is valid but illegal from the string's current state (e.g.
`approve` on a `Draft`, `publish` on an `InReview`). The legal table is in
[`translation-workflow.md`](translation-workflow.md). Bulk review
(`review-bulk`) **skips** illegal transitions instead of failing — check
`skipped` in the response.

## Excel / CSV import does nothing, or returns `400`

`POST /api/projects/{project}/import` — see [`import-export.md`](import-export.md).

- **`400` "language is required for this format"** — the file was read as
  **narrow** (no column header matched a registered language code), so it needs a
  request `language`. Either add real language-code columns (`en-GB`, `fr-FR`, …)
  to make it **wide**, or set `language` in the request.
- **A wide file behaves like a narrow one** — a language column only counts if
  that code is **registered** (`GET /api/languages`); create the languages first
  (`POST /api/languages/bulk`). An unrecognised header is ignored.
- **Excel `400` "not a valid .xlsx (OpenXML) workbook"** — only modern `.xlsx`
  is accepted, not legacy `.xls`. Re-save as `.xlsx`.
- **Excel sends nothing / "no xlsx content"** — XLSX bytes go **base64-encoded
  in `contentBase64`**, not `content`. `content` is for the text formats
  (`json` / `flat` / `csv`).
- **`400` "'contentBase64' is not valid base64"** — encode the raw file bytes;
  don't wrap in a `data:` URI or add newlines the decoder rejects.
- **Rows imported but not delivered** — a wide import does not check that a
  language column is *enabled* for the project. Enable it
  (`PUT /api/projects/{code}/languages/{lang}`), then publish.
- **Nothing changed** — blank cells are skipped by design; check `skipped` and
  `errors` in the response, and run with `dryRun: true` to see the plan.
- **Approved/Published strings dropped back to Draft** — an edited string is
  walked to the import `status` (default `Draft`). Pass `status: "InReview"` or
  `"Approved"`.

## `dotnet ef` / migrations — not applicable

There is **no EF Core** and no migration tool. Indexes are created by
`MongoIndexInitializer` on every startup (`createIndexes` is idempotent). Skip
`dotnet tool restore` — `.config/dotnet-tools.json` was removed. Schema changes
are additive (`IgnoreExtraElements`) or one-off backfill scripts.

## First `dotnet test` run is slow / downloads a binary

`EphemeralMongo` downloads a `mongod` binary on first use and caches it at
`~/.cache/ephemeral-mongo`. CI caches this directory. Subsequent runs are fast.
No Docker is needed for `dotnet test` (the integration suite prefers a real
`mongo:7` via Testcontainers **when a Docker daemon is present** and falls back
to EphemeralMongo otherwise).

## Build fails on a warning

`Directory.Build.props` sets `TreatWarningsAsErrors=true` — any analyzer or
compiler warning fails `dotnet build` / `dotnet test`. Fix the warning; do not
suppress it project-wide. `NuGetAudit` is off on the **test** projects only;
shipping projects still fail the build on a vulnerable package.

## Port `8080` (or `5147`) already in use

- Container / compose: the API is `http://localhost:8080`. Change the host port
  via `API_PORT` in `.env` (`docker-compose.yml`), or stop whatever holds it
  (`lsof -i :8080`).
- `dotnet run --project src/CTMS.Api`: `http://localhost:5147` (from
  `launchSettings.json`). Override with
  `ASPNETCORE_URLS=http://localhost:5200 dotnet run --project src/CTMS.Api`.

## Admin UI can't reach the API

- `Ctms:ApiBaseUrl` (default `http://localhost:8080`) must point at the running
  API. When you run the API with `dotnet run` it is on `:5147`, not `:8080`.
- With `Auth:Enabled=true` the Admin UI also needs `Ctms:ApiScope` (e.g.
  `api://<api-client-id>/access_as_user`) and the `AzureAd:*` values, plus its
  own client secret. A `401` from the API often shows in the UI as "Interactive
  sign-in required" — do a full-page navigation to re-run the OIDC challenge.
- `Auth:Enabled=false` on **both** hosts for a no-Entra local run.

## CORS errors in a browser client

`Cors:AllowedOrigins` is empty by default ⇒ the API allows **no** cross-origin
request. Add the browser origin(s) via `Cors__AllowedOrigins__0`,
`Cors__AllowedOrigins__1`, … The compose prod override sets index 0 from
`${CTMS_ALLOWED_ORIGIN}`. The server-rendered Admin UI is same-origin and needs
nothing here.

## `429 Too Many Requests`

The global fixed-window rate limiter (`RateLimit:Enabled=true` outside dev). The
anonymous delivery GET has its own looser IP partition
(`RateLimit:BundlePermitPerWindow`). Respect the `Retry-After` header. Turn it
off for manual testing with `RateLimit__Enabled=false` (the dev compose stack
already does).
