using System.Net;
using System.Text;
using System.Text.Json;
using CTMS.Client;
using CTMS.Client.Caching;
using Microsoft.Extensions.DependencyInjection;

// CTMS client SDK - console walkthrough.
//
// Runs fully offline against an in-process fake API unless you point it at a real
// CTMS instance:
//
//   CTMS_BASE_URL=http://localhost:5147 CTMS_PROJECT_ID=<guid> \
//     CTMS_LOCALES=fr-CA,fr,en dotnet run --project samples/Ctms.ConsoleSample
//
// Demonstrates: prefetch, revalidation (304), offline replay against a dead URL,
// and locale fallback-chain resolution.

var projectId = Environment.GetEnvironmentVariable("CTMS_PROJECT_ID") is { Length: > 0 } rawId
    ? Guid.Parse(rawId)
    : Guid.Parse("11111111-1111-1111-1111-111111111111");

var baseUrl = Environment.GetEnvironmentVariable("CTMS_BASE_URL");
var locales = (Environment.GetEnvironmentVariable("CTMS_LOCALES") ?? "fr-CA,fr,en")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var cacheDir = Path.Combine(Path.GetTempPath(), "ctms-console-sample", projectId.ToString("D"));
Directory.CreateDirectory(cacheDir);
Console.WriteLine($"Cache directory: {cacheDir}");

// A fake in-process API so the sample runs with no server. It serves one bundle per
// locale and honours If-None-Match with a 304.
var fakeApi = new FakeCtmsHandler(projectId);

var services = new ServiceCollection();
services.AddCtmsClient(options =>
{
    options.ProjectId = projectId;
    options.DefaultLocale = "en";
    options.CacheDirectory = cacheDir;
    options.StalenessTtl = TimeSpan.Zero; // always revalidate so the 304 path is visible
    options.DiagnosticsLogger = Console.WriteLine;
    options.MissingKeyFallback = key => $"!!{key}!!";

    if (baseUrl is { Length: > 0 })
    {
        options.BaseAddress = new Uri(baseUrl);
    }
    else
    {
        options.HttpClient = new HttpClient(fakeApi) { BaseAddress = new Uri("http://fake.ctms.local/") };
    }
});

using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<ICtmsClient>();

Console.WriteLine("\n== 1. Prefetch ==");
await client.PrefetchAsync(locales);

Console.WriteLine("\n== 2. Revalidation (immediate re-fetch -> 304 from the fake API) ==");
var bundle = await client.GetBundleAsync(locales[0]);
Console.WriteLine($"{bundle.LocaleCode} v{bundle.Version} etag={bundle.Etag[..8]} " +
                  $"retrieved={bundle.RetrievedAt:HH:mm:ss} validated={bundle.LastValidatedAt:HH:mm:ss} stale={bundle.IsStale}");

Console.WriteLine("\n== 3. Fallback chain (fr-CA -> fr -> en -> MissingKeyFallback) ==");
foreach (var key in new[] { "greeting", "checkout.button", "only.english", "totally.missing" })
{
    Console.WriteLine($"  Get(\"{key}\", \"fr-CA\") = \"{client.Get(key, "fr-CA", Array.Empty<string>())}\"" +
                      $"   (nullable overload: {(client.Get(key, "fr-CA") is { } v ? $"\"{v}\"" : "null")})");
}

Console.WriteLine("\n== 4. Offline replay (new client pointed at a dead URL, warm file cache) ==");
var offlineServices = new ServiceCollection();
offlineServices.AddCtmsClient(options =>
{
    options.ProjectId = projectId;
    options.DefaultLocale = "en";
    options.CacheDirectory = cacheDir;
    options.RequestTimeout = TimeSpan.FromSeconds(2);
    options.DiagnosticsLogger = Console.WriteLine;
    options.HttpClient = new HttpClient(new DeadHandler()) { BaseAddress = new Uri("http://127.0.0.1:1/") };
});
using var offlineProvider = offlineServices.BuildServiceProvider();
var offlineClient = offlineProvider.GetRequiredService<ICtmsClient>();

var offlineBundle = await offlineClient.GetBundleAsync(locales[0]);
Console.WriteLine($"served {offlineBundle.LocaleCode} v{offlineBundle.Version} from cache, IsStale={offlineBundle.IsStale}");

try
{
    await offlineClient.GetBundleAsync("de-DE-never-cached");
}
catch (CtmsOfflineException ex)
{
    Console.WriteLine($"cold-cache miss threw CtmsOfflineException as expected: {ex.Message}");
}

Console.WriteLine("\nDone.");

// ---------------------------------------------------------------------------

file sealed class FakeCtmsHandler(Guid projectId) : HttpMessageHandler
{
    private readonly Dictionary<string, (int Version, Dictionary<string, string> Entries)> _bundles = new()
    {
        ["fr"] = (3, new() { ["greeting"] = "Bonjour", ["checkout.button"] = "Payer" }),
        ["fr-ca"] = (1, new() { ["greeting"] = "Salut" }),
        ["en"] = (5, new() { ["greeting"] = "Hello", ["checkout.button"] = "Pay", ["only.english"] = "EN only" }),
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var segments = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // api/projects/{id}/bundles/{locale}
        var locale = Uri.UnescapeDataString(segments[^1]).ToLowerInvariant();

        if (!_bundles.TryGetValue(locale, out var data))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        var etag = TranslationBundleEtag.Compute(data.Entries);

        if (request.Headers.IfNoneMatch.Any(t => t.Tag == $"\"{etag}\"" || t.Tag == "*"))
        {
            var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);
            notModified.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{etag}\"");
            return Task.FromResult(notModified);
        }

        var dto = new
        {
            id = Guid.NewGuid(),
            projectId,
            localeCode = locale,
            version = data.Version,
            entries = data.Entries,
            etag,
            createdBy = "sample",
            createdAt = DateTime.UtcNow,
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(dto), Encoding.UTF8, "application/json"),
        };
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{etag}\"");
        response.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        return Task.FromResult(response);
    }
}

file sealed class DeadHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => throw new HttpRequestException("connection refused (sample)");
}

// Mirrors CTMS.Domain.Translations.TranslationBundle.ComputeETag so the fake API
// produces server-compatible tags without referencing the backend.
file static class TranslationBundleEtag
{
    public static string Compute(IReadOnlyDictionary<string, string> entries)
    {
        var builder = new StringBuilder();
        foreach (var pair in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key).Append('\n').Append(pair.Value).Append('\n');
        }

        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash);
    }
}
