using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CTMS.Client;
using Microsoft.Extensions.DependencyInjection;

// CTMS client SDK - console walkthrough.
//
// Runs fully offline against an in-process fake API unless you point it at a real
// CTMS instance:
//
//   CTMS_BASE_URL=http://localhost:5147 CTMS_APPLICATION=nimbus \
//     CTMS_LANGUAGES=fr-CA,fr-FR,en-GB dotnet run --project samples/Ctms.ConsoleSample
//
// Demonstrates: prefetch, revalidation (304), offline replay against a dead URL,
// and language fallback-chain resolution.

var application = Environment.GetEnvironmentVariable("CTMS_APPLICATION") is { Length: > 0 } rawApp
    ? rawApp
    : "nimbus";

var baseUrl = Environment.GetEnvironmentVariable("CTMS_BASE_URL");
var languages = (Environment.GetEnvironmentVariable("CTMS_LANGUAGES") ?? "fr-CA,fr-FR,en-GB")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var cacheDir = Path.Combine(Path.GetTempPath(), "ctms-console-sample", application);
Directory.CreateDirectory(cacheDir);
Console.WriteLine($"Cache directory: {cacheDir}");

// A fake in-process API so the sample runs with no server. It serves one flat
// translation map per language and honours If-None-Match with a 304.
var fakeApi = new FakeCtmsHandler(application);

var services = new ServiceCollection();
services.AddCtmsClient(options =>
{
    options.Application = application;
    options.DefaultLanguage = "en-GB";
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
await client.PrefetchAsync(languages);

Console.WriteLine("\n== 2. Revalidation (immediate re-fetch -> 304 from the fake API) ==");
var set = await client.GetTranslationsAsync(languages[0]);
Console.WriteLine($"{set.Application}/{set.Language}  {set.Entries.Count} keys  etag={Short(set.Etag)}  " +
                  $"retrieved={set.RetrievedAt:HH:mm:ss} validated={set.LastValidatedAt:HH:mm:ss} stale={set.IsStale}");

Console.WriteLine("\n== 3. Fallback chain (fr-CA -> fr-FR -> en-GB -> MissingKeyFallback) ==");
foreach (var key in new[] { "greeting", "checkout.button", "only.english", "totally.missing" })
{
    var guaranteed = client.Get(key, "fr-CA", "fr-FR");
    var nullable = client.Get(key, "fr-CA") is { } v ? $"\"{v}\"" : "null";
    Console.WriteLine($"  Get(\"{key}\", \"fr-CA\", \"fr-FR\") = \"{guaranteed}\"   (nullable overload: {nullable})");
}

Console.WriteLine("\n== 4. Offline replay (new client pointed at a dead URL, warm file cache) ==");
var offlineServices = new ServiceCollection();
offlineServices.AddCtmsClient(options =>
{
    options.Application = application;
    options.DefaultLanguage = "en-GB";
    options.CacheDirectory = cacheDir;
    options.RequestTimeout = TimeSpan.FromSeconds(2);
    options.DiagnosticsLogger = Console.WriteLine;
    options.HttpClient = new HttpClient(new DeadHandler()) { BaseAddress = new Uri("http://127.0.0.1:1/") };
});
using var offlineProvider = offlineServices.BuildServiceProvider();
var offlineClient = offlineProvider.GetRequiredService<ICtmsClient>();

var offlineSet = await offlineClient.GetTranslationsAsync(languages[0]);
Console.WriteLine($"served {offlineSet.Application}/{offlineSet.Language} from cache, IsStale={offlineSet.IsStale}");

try
{
    await offlineClient.GetTranslationsAsync("de-DE-never-cached");
}
catch (CtmsOfflineException ex)
{
    Console.WriteLine($"cold-cache miss threw CtmsOfflineException as expected: {ex.Message}");
}

Console.WriteLine("\nDone.");

static string Short(string etag) => etag.Length <= 12 ? etag : etag[..12];

// ---------------------------------------------------------------------------

file sealed class FakeCtmsHandler(string application) : HttpMessageHandler
{
    private readonly Dictionary<string, Dictionary<string, string>> _sets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fr-FR"] = new() { ["greeting"] = "Bonjour", ["checkout.button"] = "Payer" },
        ["fr-CA"] = new() { ["greeting"] = "Salut" },
        ["en-GB"] = new() { ["greeting"] = "Hello", ["checkout.button"] = "Pay", ["only.english"] = "EN only" },
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var segments = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // api/translations/{application}/{language}
        var language = Uri.UnescapeDataString(segments[^1]);

        if (!_sets.TryGetValue(language, out var translations))
        {
            return Task.FromResult(Problem(HttpStatusCode.NotFound, "Resource not found", $"language '{language}' not enabled"));
        }

        var etag = TranslationContentHash.Compute(translations);

        if (request.Headers.IfNoneMatch.Any(t => t.Tag == $"\"{etag}\"" || t.Tag == "*"))
        {
            var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);
            notModified.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{etag}\"");
            return Task.FromResult(notModified);
        }

        var payload = JsonSerializer.Serialize(new
        {
            project = application,
            language,
            translations,
        });

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{etag}\"");
        response.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        return Task.FromResult(response);
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string title, string detail) => new(status)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { title, detail, status = (int)status }),
            Encoding.UTF8,
            "application/problem+json"),
    };
}

file sealed class DeadHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => throw new HttpRequestException("connection refused (sample)");
}

// Mirrors CTMS.Application's TranslationContentHash.Compute so the fake API
// produces server-compatible tags without referencing the backend.
file static class TranslationContentHash
{
    public static string Compute(IReadOnlyDictionary<string, string> entries)
    {
        var builder = new StringBuilder();
        foreach (var pair in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key).Append('\n').Append(pair.Value).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
