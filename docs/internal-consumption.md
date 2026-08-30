# Internal consumption (in-process, no HTTP)

How an **internal .NET microservice** in the same solution consumes translations:
it references the shared translation project, registers it with one call, injects
`ITranslationService`, and gets a `TranslationBundle` back — **no HTTP request,
no direct MongoDB or Redis access** (spec §2, §14, §37).

External apps use the REST API instead — see
[`external-consumption.md`](external-consumption.md). Both paths run the same
Application logic and produce the same result.

---

## 1. Reference the shared project

Add a project (or package) reference to **`CTMS.Infrastructure`** (which
transitively brings in `CTMS.Application` and `CTMS.Domain`):

```xml
<ItemGroup>
  <ProjectReference Include="..\CTMS.Infrastructure\CTMS.Infrastructure.csproj" />
</ItemGroup>
```

## 2. Register the engine

`AddTranslationServices` is the single entry point for an internal consumer
(`CTMS.Infrastructure/DependencyInjection.cs`, namespace `CTMS.Infrastructure`).
It composes `AddApplication()` (the use-case services) and
`AddInfrastructure(IConfiguration)` (the MongoDB client/context, the five
repositories, the translations cache, the readiness health check, and the
`MongoIndexInitializer` / `DataSeeder` hosted services):

```csharp
using CTMS.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTranslationServices(builder.Configuration);
builder.Services.AddScoped<CourseService>();

var host = builder.Build();
```

Works in any host — an ASP.NET Core app, a worker service, a console
`HostBuilder`. `ITranslationService` is registered **scoped**.

## 3. Inject and call

```csharp
using CTMS.Application.Translations;

public sealed class CourseService
{
    private readonly ITranslationService _translations;

    public CourseService(ITranslationService translations)
        => _translations = translations;

    public async Task<string> GetCourseTitleAsync(string language, CancellationToken ct)
    {
        TranslationBundle bundle =
            await _translations.GetTranslationsAsync("nimbus", language, ct);

        // bundle.Translations is a flat, ordered keyName -> value map.
        return bundle.Translations.TryGetValue("course.start", out var value)
            ? value
            : "course.start";
    }
}
```

### The contract

```csharp
public interface ITranslationService
{
    Task<TranslationBundle> GetTranslationsAsync(
        string project,
        string language,
        CancellationToken cancellationToken = default);
}

public sealed record TranslationBundle(
    string Project,
    string Language,
    IReadOnlyDictionary<string, string> Translations,
    string ETag);
```

- `Translations` is the **fully resolved** map: this project's `Published`
  strings, merged with every `IsCommon` project's `Published` strings (the
  project value wins a key-name collision), with gaps filled from the language's
  `FallbackCode` chain. Only `Published` is included; `Archived` never is. See
  [`architecture.md` §4](architecture.md#4-assemble-on-demand-delivery).
- `ETag` is the content hash of the map — the same value the REST route puts in
  its `ETag` header ([`etag.md`](etag.md)). Use it for your own change detection
  (e.g. skip re-rendering when it is unchanged).
- **Throws `CTMS.Application.Common.NotFoundException`** when the project or
  language is unknown / inactive, or the language is not enabled for the project.
- A Redis (or in-process) read-through cache fronts the call, so a warm bundle
  costs no database round-trip. The cache is invalidated automatically on
  publish. See [`caching.md`](caching.md).

## 4. Configuration keys the consumer needs

`AddInfrastructure` reads these from `IConfiguration` (`appsettings.json` /
environment; `__` maps to `:`):

| Key | Required | Meaning |
|---|---|---|
| `ConnectionStrings:CtmsDatabase` | **yes** — startup throws without it | MongoDB connection string. The same store CTMS itself uses. |
| `Mongo:Database` | no (default `ctms`) | Database name inside the Mongo server. Must match the CTMS deployment. |
| `ConnectionStrings:Redis` | no | Redis connection string (`host:port[,options]`). Unset ⇒ an in-process `IDistributedCache` — fine for a single instance; set it so multiple replicas share one cache. |
| `Cache:TranslationsTtlMinutes` | no (default 60) | TTL for a cached assembled map. |
| `Seed:Enabled` | no (default off) | The dev data seeder runs only in the `Development` environment **and** only when this is `true`. Leave it off in a real service. |

Example (`appsettings.json` of the consuming service):

```json
{
  "ConnectionStrings": {
    "CtmsDatabase": "mongodb://ctms-mongo:27017",
    "Redis": "ctms-redis:6379"
  },
  "Mongo": { "Database": "ctms" }
}
```

## 5. The rule: use the public abstraction only

Spec §15 / §32. An internal microservice may use the translation functionality
**through `ITranslationService` only**. It must not:

- inject or call the repositories (`IProjectRepository`,
  `ITranslationStringRepository`, …) or `PublishedTranslationsService` directly;
- open its own `IMongoClient` against the translation collections;
- read, write, or invalidate `translations:*` Redis keys;
- reference the domain entities as a persistence model.

`ITranslationService` is the seam. Everything behind it — storage, resolution,
common merge, fallback, publishing, history, caching, the ETag — is owned by
CTMS. If you find yourself needing something `ITranslationService` does not
expose, that is a change to CTMS, not a reason to reach around it.

## 6. Business services should stay language-independent

Prefer returning stable codes (`{ "code": "COURSE_NOT_FOUND" }`) from business
microservices and resolving `errors.course_not_found` through CTMS at the edge,
rather than embedding translated strings or a translation dictionary in the
business service (spec §41–§42).

## 7. Verified by a test

`tests/CTMS.Application.Tests/TranslationServiceRegistrationTests.cs` builds a
`ServiceCollection`, calls `AddTranslationServices`, resolves
`ITranslationService`, and asserts it returns a correctly assembled
`TranslationBundle` — with no HTTP anywhere.
