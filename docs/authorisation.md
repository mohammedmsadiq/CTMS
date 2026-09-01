# Authorisation

Role-based, enforced at the API layer through **named policies** — endpoints
reference a policy constant, never a raw role check
(`src/CTMS.Api/Auth/AuthorizationPolicies.cs`; `CTMS.AdminUI/Auth` keeps a
byte-identical copy). Authentication is in
[`authentication.md`](authentication.md).

An authenticated principal that carries **none** of the recognised roles
satisfies no policy and gets `403` on every `/api/*` route (except the
anonymous-by-default consumer reads).

---

## The five roles

Exact strings expected in the token's `roles` claim (Entra app-role `value`),
`src/CTMS.Api/Auth/AuthRoles.cs`:

| Role constant | Claim value | Intended for |
|---|---|---|
| `Admin` | `TranslationAdministrator` | Everything, including creating projects |
| `Manager` | `TranslationManager` | Manage languages / keys / projects, publish, plus all reviewer and translator rights |
| `Reviewer` | `TranslationReviewer` | Run review transitions; edit string values; read |
| `Translator` | `Translator` | Create / edit string values; read |
| `Reader` | `TranslationReadOnly` | Read-only — every GET |

## The six policies → roles that satisfy them

| Policy | Admin | Manager | Reviewer | Translator | Reader |
|---|:--:|:--:|:--:|:--:|:--:|
| `CanRead` — every GET | ✓ | ✓ | ✓ | ✓ | ✓ |
| `CanEditStrings` — the string upsert | ✓ | ✓ | ✓ | ✓ | |
| `CanReview` — review transitions (incl. the `publish` action) and bulk review | ✓ | ✓ | ✓ | | |
| `CanManageContent` — create/update/delete keys and languages, `PATCH` a project, project-language enable/disable, bulk import | ✓ | ✓ | | | |
| `CanPublish` — `POST /api/translations/publish` | ✓ | ✓ | | | |
| `CanAdminProjects` — `POST /api/projects` | ✓ | | | | |

`AuthorizationPolicies.RolesByPolicy` is the single source; every `(role,
policy)` pair is exercised by `AuthorizationPoliciesTests` (application suite)
and `AuthorizationMatrixTests` (integration suite).

## Spec §46 intent vs. the implemented matrix

| Actor | Spec §46 | Implemented |
|---|---|---|
| Administrator | Everything | `CanAdminProjects` + every other policy — matches |
| Manager | Create, Edit, Review, Approve, Publish | `CanManageContent` + `CanReview` + `CanPublish` + `CanEditStrings` + `CanRead` — matches |
| Translator | Create, Edit, **Submit for review** | `CanEditStrings` + `CanRead`. **"Create" = create/edit string *values* (the upsert), not create keys** (that is `CanManageContent`). `POST .../review` and `POST .../review-bulk` accept `action: "submit"` for `CanEditStrings` (Translator included); every other action on those routes requires `CanReview`, enforced per-request. |
| Reviewer | Review, Approve | `CanReview` + `CanRead`, **and also `CanEditStrings`** — a Reviewer can additionally edit string values, which §46 does not list. |
| ReadOnly | View | `CanRead` only — matches |

> **Review-route authorisation.** `POST /api/projects/{project}/keys/{keyId}/strings/{language}/review`
> and `POST /api/projects/{project}/review-bulk` carry the group policy
> `CanEditStrings`, then the handler additionally requires `CanReview` unless the
> body's `action` is `"submit"` — so a Translator may submit their own work
> (spec §46) but not approve/reject/reopen/publish/archive/unarchive.
>
> **One remaining §46 divergence (code is source of truth):** a **Reviewer can
> edit string values** (`CanEditStrings` includes `Reviewer`); §46 lists only
> "Review, Approve" for a Reviewer. A small widening within the review team, not
> a security hole.

## Endpoint → policy

| Route(s) | Policy |
|---|---|
| `GET /api/translations/{project}/{language}` | anonymous while `Auth:PublicBundleReads=true`, else `CanRead` |
| `GET /api/projects` (list), `GET /api/languages` (list) | anonymous while `Auth:PublicBundleReads=true`, else `CanRead` |
| `GET /api/projects/{code}`, `GET /api/languages/{code}` | `CanRead` |
| `GET /api/projects/{project}/keys…`, `…/strings…` (all GETs) | `CanRead` |
| `GET /api/translations` (grid), `…/publish/preview`, `…/missing`, `GET /api/categories`, `GET /api/dashboard` | `CanRead` |
| `GET /api/projects/{project}/history`, `…/strings/{language}/history` | `CanRead` |
| `GET /api/projects/{project}/export` (CSV / XLSX work file) | `CanRead` |
| `PUT /api/projects/{project}/keys/{keyId}/strings/{language}` (upsert) | `CanEditStrings` |
| `POST /api/projects/{project}/keys/{keyId}/strings/{language}/review` | `CanReview` |
| `POST /api/projects/{project}/review-bulk` | `CanReview` |
| `POST` / `PATCH` / `DELETE /api/projects/{project}/keys/{keyId}` | `CanManageContent` |
| `POST /api/languages`, `PATCH /api/languages/{code}`, `POST /api/languages/bulk` | `CanManageContent` |
| `PATCH /api/projects/{code}`, `PUT` / `DELETE /api/projects/{code}/languages/{language}` | `CanManageContent` |
| `POST /api/projects/{project}/import` | `CanManageContent` |
| `POST /api/translations/publish` | `CanPublish` |
| `POST /api/projects` | `CanAdminProjects` |
| `/health`, `/health/live`, `/health/ready`, Swagger | always anonymous |

## Where it is wired

| Concern | Location |
|---|---|
| Role name constants | `src/CTMS.Api/Auth/AuthRoles.cs` (mirror: `src/CTMS.AdminUI/Auth/AuthRoles.cs`) |
| Role → policy mapping | `src/CTMS.Api/Auth/AuthorizationPolicies.cs` (mirror in `CTMS.AdminUI/Auth`) |
| Policy registration | `AuthorizationPolicies.Configure` passed to `AddAuthorization` in `AddCtmsAuth()` |
| Per-endpoint policy | `.RequireAuthorization("<policy>")` in each `src/CTMS.Api/Endpoints/*.cs`; the anonymous reads use `.GatePublicRead(...)` (`Endpoints/EndpointConventions.cs`) |
| Admin UI gating | `<AuthorizeView Policy="...">` in pages; `Services/CurrentUser.cs` |

Consumer access is entirely separate from management access: consuming
translations grants no management permission (spec §45).
