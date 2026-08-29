# 3. Production hardening: CORS, rate limiting, request-size cap, persistent Data Protection, structured logging

Date: 2026-08-29

## Status

Accepted

## Context

The API had no cross-origin policy, no abuse throttling, no explicit request-body
ceiling, ephemeral Data Protection keys, and console-only unstructured logging.
That was fine while the only consumers were the test suite and a same-origin dev
run, but the target deployment is an internet-facing Azure Container App with:

- a browser SPA / Blazor Admin UI on a **different origin**;
- **anonymous** bundle delivery GETs (`Auth:PublicBundleReads=true`) that any
  client on the internet can hit;
- **more than one replica** behind the ingress, replicas recycled on every
  deploy;
- a need to correlate a request across logs when something breaks in production.

These are all decisions that are awkward to retrofit piecemeal (every one of them
changes externally observable behaviour), so they are recorded together.

## Decision

Add the following to `CTMS.Api`, each a small `Infrastructure/*Setup` helper
wired from `Program.cs`, each driven by configuration and each an effective
no-op in `Development` / tests:

1. **CORS** (`CorsSetup`). One named policy, `"ctms"`. Origins come from
   `Cors:AllowedOrigins` (string array). **Empty / absent ⇒ no cross-origin
   access** (`SetIsOriginAllowed(_ => false)`) - the safe fresh-deploy default,
   and what `appsettings.Production.json` ships. When origins are configured the
   policy allows them with any header/method, permits credentials, and exposes
   `ETag` and `Location` so a browser SDK can read the bundle entity tag and a
   created-resource location. `UseCors` runs **before** auth so an
   unauthenticated preflight is answered.

2. **Rate limiting** (`RateLimitingSetup`). One global partitioned fixed-window
   limiter. Authenticated callers are partitioned by stable user id
   (`oid` → name-identifier → `preferred_username` → name); anonymous callers by
   remote IP; the anonymous bundle **delivery** GET path (`.../bundles/...`) gets
   its own looser IP-keyed partition so a busy CDN edge cannot exhaust a
   translator's budget. Knobs (with defaults): `RateLimit:Enabled` (`true`;
   `false` in the test factory), `RateLimit:PermitPerWindow` (`120`),
   `RateLimit:WindowSeconds` (`60`), `RateLimit:QueueLimit` (`0`),
   `RateLimit:BundlePermitPerWindow` (`PermitPerWindow × 5`). A rejection is
   `429` + RFC 7807 body + `Retry-After`. `/health` and `/health/ready` opt out.
   `UseRateLimiter` runs **after** auth so the user-id partition is available.

3. **Request-size cap** (`RequestBodySizeLimit`). `Limits:MaxRequestBodyBytes`
   (default `262144` = 256 KB; `<= 0` ⇒ default). Applied both as Kestrel's
   `MaxRequestBodySize` and by an early middleware that returns `413` + RFC 7807
   - the middleware also covers hosting models that ignore the Kestrel limit
   (the integration test server), and tightens the per-request limit for chunked
   bodies with no `Content-Length`.

4. **Persistent Data Protection** (`DataProtectionSetup`).
   `AddDataProtection().SetApplicationName("CTMS")`; when `ConnectionStrings:Redis`
   is set the key ring is persisted to Redis
   (`PersistKeysToStackExchangeRedis`, key `DataProtection-Keys`) so every
   replica shares one set of keys and they survive restarts. Unset ⇒ the
   framework default (local, ephemeral) with an info line logged
   (`DataProtectionModeLogger`, same degrade-quietly pattern as
   `CacheModeLogger`). At-rest key encryption (certificate / Key Vault) is left
   as a `TODO` pending a provisioned key.

5. **Structured logging** (`LoggingSetup`). Providers cleared and rebound from
   the `Logging` section. Development keeps the human-readable console;
   every other environment gets the built-in `AddJsonConsole` (scopes on, UTC
   timestamps) - no third-party logging package. `ActivityTrackingOptions`
   stamps `TraceId` / `SpanId` / `ParentId` on every scope, lining logs up with
   the `traceId` that `AddProblemDetails` already puts on error responses.
   `AddHttpLogging` emits one line per request (method, path, status, elapsed -
   no headers, no bodies); `/health*` is excluded.

`appsettings.Production.json` ships `Cors:AllowedOrigins: []`,
`RateLimit:Enabled: true`, `Auth:Enabled: true`, `Seed:Enabled: false`; the
numeric rate-limit / body-size knobs fall back to the code defaults above unless
overridden per environment.

## Consequences

### Positive

- The Admin UI and third-party SPAs can call the API from their own origin
  without opening it to every origin.
- A single client can no longer trivially exhaust the anonymous bundle endpoint
  or the write endpoints.
- Antiforgery / auth cookies and any other Data-Protection-backed payloads keep
  working across a scale-out or a rolling deploy instead of breaking on every
  replica change.
- Production logs are queryable by field and a failing request can be traced end
  to end by the `traceId` that already appears on RFC 7807 error bodies.

### Negative / risks

- **More configuration to get right per environment.** A missing or wrong
  `Cors:AllowedOrigins` entry shows up as a browser CORS failure, not a server
  error; a too-low `RateLimit` or `Limits:MaxRequestBodyBytes` rejects
  legitimate traffic. Mitigation: permissive-but-present defaults, and all five
  features are inert in `Development`.
- **Redis becomes load-bearing for Data Protection** where before it was a pure
  cache. Losing the Redis key data invalidates issued protected payloads. It is
  still not a readiness dependency (the app starts and serves bundles without
  it), but operationally Redis now needs a backup/retention policy.
- Rate limiting keyed on IP is coarse behind a shared NAT / corporate proxy;
  revisit with per-token partitioning if that bites.
- JSON logs are less pleasant to read raw during an incident without a viewer.
