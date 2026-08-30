using System.Net;

namespace CTMS.Client.Tests;

public sealed class FallbackChainTests
{
    private static readonly Dictionary<string, string> FrFr = new() { ["greeting"] = "Bonjour", ["checkout"] = "Payer" };
    private static readonly Dictionary<string, string> FrCa = new() { ["greeting"] = "Salut" };
    private static readonly Dictionary<string, string> EnGb = new() { ["greeting"] = "Hello", ["checkout"] = "Pay", ["only.en"] = "EN" };

    private static async Task<CtmsClient> LoadedClientAsync()
    {
        var handler = new StubHttpMessageHandler
        {
            Fallback = req =>
            {
                var language = req.RequestUri!.AbsolutePath.Split('/')[^1];
                return language switch
                {
                    "fr-FR" => StubHttpMessageHandler.TranslationsOk(TestClient.Application, "fr-FR", FrFr),
                    "fr-CA" => StubHttpMessageHandler.TranslationsOk(TestClient.Application, "fr-CA", FrCa),
                    "en-GB" => StubHttpMessageHandler.TranslationsOk(TestClient.Application, "en-GB", EnGb),
                    _ => StubHttpMessageHandler.Problem(HttpStatusCode.NotFound, "Resource not found", "x"),
                };
            },
        };
        var client = TestClient.Create(handler, out _);
        await client.PrefetchAsync(new[] { "fr-CA", "fr-FR", "en-GB" });
        return client;
    }

    [Fact]
    public async Task Exact_language_wins()
    {
        var client = await LoadedClientAsync();
        Assert.Equal("Salut", client.Get("greeting", "fr-CA"));
    }

    [Fact]
    public async Task Falls_back_to_an_explicit_extra_language_before_the_default()
    {
        var client = await LoadedClientAsync();
        // fr-CA has no "checkout"; the explicit extra "fr-FR" resolves before the default "en-GB".
        Assert.Equal("Payer", client.Get("checkout", "fr-CA", "fr-FR"));
    }

    [Fact]
    public async Task Falls_back_to_the_configured_default_language()
    {
        var client = await LoadedClientAsync();
        Assert.Equal("EN", client.Get("only.en", "fr-CA"));
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
            Fallback = _ => StubHttpMessageHandler.TranslationsOk(TestClient.Application, "fr-FR", FrFr),
        };
        var client = TestClient.Create(handler, out _, configure: o => o.MissingKeyFallback = k => $"[{k}]");
        await client.GetTranslationsAsync("fr-FR");

        Assert.Equal("[ghost]", client.Get("ghost", "fr-FR", Array.Empty<string>()));
    }

    [Fact]
    public async Task Language_match_is_case_insensitive()
    {
        var client = await LoadedClientAsync();
        Assert.Equal("Bonjour", client.Get("greeting", "FR-fr"));
        Assert.Equal("Salut", client.Get("greeting", "fR-Ca"));
    }
}
