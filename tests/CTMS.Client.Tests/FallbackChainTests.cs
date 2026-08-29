namespace CTMS.Client.Tests;

public sealed class FallbackChainTests
{
    private static readonly Dictionary<string, string> Fr = new() { ["greeting"] = "Bonjour", ["checkout"] = "Payer" };
    private static readonly Dictionary<string, string> FrCa = new() { ["greeting"] = "Salut" };
    private static readonly Dictionary<string, string> En = new() { ["greeting"] = "Hello", ["checkout"] = "Pay", ["only_en"] = "EN" };

    private static async Task<CtmsClient> LoadedClientAsync()
    {
        var handler = new StubHttpMessageHandler
        {
            Fallback = req =>
            {
                var locale = req.RequestUri!.AbsolutePath.Split('/')[^1].ToLowerInvariant();
                return locale switch
                {
                    "fr" => StubHttpMessageHandler.BundleOk(TestClient.ProjectId, "fr", 2, Fr),
                    "fr-ca" => StubHttpMessageHandler.BundleOk(TestClient.ProjectId, "fr-CA", 1, FrCa),
                    "en" => StubHttpMessageHandler.BundleOk(TestClient.ProjectId, "en", 4, En),
                    _ => StubHttpMessageHandler.Problem(System.Net.HttpStatusCode.NotFound, "Not Found", "x"),
                };
            },
        };
        var client = TestClient.Create(handler, out _);
        await client.PrefetchAsync(new[] { "fr-CA", "fr", "en" });
        return client;
    }

    [Fact]
    public async Task Exact_locale_wins()
    {
        var client = await LoadedClientAsync();
        Assert.Equal("Salut", client.Get("greeting", "fr-CA"));
    }

    [Fact]
    public async Task Falls_back_to_the_parent_locale()
    {
        var client = await LoadedClientAsync();
        Assert.Equal("Payer", client.Get("checkout", "fr-CA"));
    }

    [Fact]
    public async Task Falls_back_to_the_configured_default_locale()
    {
        var client = await LoadedClientAsync();
        Assert.Equal("EN", client.Get("only_en", "fr-CA"));
    }

    [Fact]
    public async Task Unresolved_key_returns_null_from_the_nullable_overload()
    {
        var client = await LoadedClientAsync();
        Assert.Null(client.Get("nope", "fr-CA"));
    }

    [Fact]
    public async Task Unresolved_key_returns_the_key_from_the_non_nullable_overload()
    {
        var client = await LoadedClientAsync();
        Assert.Equal("nope", client.Get("nope", "fr-CA", Array.Empty<string>()));
    }

    [Fact]
    public async Task Missing_key_fallback_is_used_when_configured()
    {
        var handler = new StubHttpMessageHandler
        {
            Fallback = _ => StubHttpMessageHandler.BundleOk(TestClient.ProjectId, "fr", 1, Fr),
        };
        var client = TestClient.Create(handler, out _, configure: o => o.MissingKeyFallback = k => $"[{k}]");
        await client.GetBundleAsync("fr");

        Assert.Equal("[ghost]", client.Get("ghost", "fr", Array.Empty<string>()));
    }

    [Fact]
    public async Task Explicit_fallback_locales_are_tried_before_the_default()
    {
        var client = await LoadedClientAsync();
        // "de" has no bundle; explicit fallback "fr" resolves before default "en".
        Assert.Equal("Bonjour", client.Get("greeting", "de", "fr"));
    }

    [Fact]
    public async Task Locale_match_is_case_insensitive()
    {
        var client = await LoadedClientAsync();
        Assert.Equal("Bonjour", client.Get("greeting", "FR"));
        Assert.Equal("Salut", client.Get("greeting", "fR-Ca"));
    }
}
