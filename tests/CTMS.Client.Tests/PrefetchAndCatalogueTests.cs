using System.Net;
using CTMS.Client.Caching;

namespace CTMS.Client.Tests;

public sealed class PrefetchAndCatalogueTests
{
    [Fact]
    public async Task PrefetchAsync_warms_multiple_languages()
    {
        var store = new InMemoryTranslationStore();
        var handler = new StubHttpMessageHandler
        {
            Fallback = req =>
            {
                var language = req.RequestUri!.AbsolutePath.Split('/')[^1];
                return StubHttpMessageHandler.TranslationsOk(TestClient.Application, language,
                    new Dictionary<string, string> { ["greeting"] = $"hi-{language}" });
            },
        };
        var client = TestClient.Create(handler, out _, store);

        await client.PrefetchAsync(new[] { "fr-FR", "de-DE", "en-GB" });

        Assert.Equal(3, handler.CallCount);
        Assert.NotNull(await store.GetAsync(TestClient.Application, "fr-FR"));
        Assert.NotNull(await store.GetAsync(TestClient.Application, "de-DE"));
        Assert.Equal("hi-fr-FR", client.Get("greeting", "fr-FR"));
        Assert.Equal("hi-de-DE", client.Get("greeting", "de-DE"));
    }

    [Fact]
    public async Task PrefetchAsync_swallows_per_language_failures()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.TranslationsOk(TestClient.Application, "fr-FR", new Dictionary<string, string> { ["k"] = "v" }))
            .Enqueue(_ => StubHttpMessageHandler.Problem(HttpStatusCode.NotFound, "Resource not found", "no such language"));
        var client = TestClient.Create(handler, out _);

        await client.PrefetchAsync(new[] { "fr-FR", "xx-XX" }); // must not throw

        Assert.Equal("v", client.Get("k", "fr-FR"));
        Assert.Null(client.Get("k", "xx-XX"));
    }

    [Fact]
    public async Task GetLanguagesAsync_maps_the_catalogue()
    {
        const string json =
            "[{\"code\":\"fr-FR\",\"name\":\"French\",\"fallbackCode\":\"en-GB\",\"isRtl\":false,\"active\":true," +
            "\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-02-01T00:00:00Z\"}," +
            "{\"code\":\"ar-SA\",\"name\":\"Arabic\",\"fallbackCode\":null,\"isRtl\":true,\"active\":true," +
            "\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-02-01T00:00:00Z\"}]";
        var handler = new StubHttpMessageHandler().Enqueue(_ => StubHttpMessageHandler.Json(json));
        var client = TestClient.Create(handler, out _);

        var languages = await client.GetLanguagesAsync();

        Assert.Equal("/api/languages", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal(2, languages.Count);
        Assert.Equal("en-GB", languages[0].FallbackCode);
        Assert.Null(languages[1].FallbackCode);
        Assert.True(languages[1].IsRtl);
    }

    [Fact]
    public async Task GetApplicationsAsync_maps_the_catalogue()
    {
        const string json =
            "[{\"code\":\"icoach\",\"name\":\"iCoach\",\"description\":\"the app\",\"isCommon\":false,\"active\":true," +
            "\"baseLanguageCode\":\"en-GB\",\"enabledLanguageCodes\":[\"en-GB\",\"fr-FR\"]," +
            "\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-02-01T00:00:00Z\"}]";
        var handler = new StubHttpMessageHandler().Enqueue(_ => StubHttpMessageHandler.Json(json));
        var client = TestClient.Create(handler, out _);

        var apps = await client.GetApplicationsAsync();

        Assert.Equal("/api/projects", handler.Requests[0].Uri.AbsolutePath);
        var app = Assert.Single(apps);
        Assert.Equal("icoach", app.Code);
        Assert.Equal(new[] { "en-GB", "fr-FR" }, app.EnabledLanguageCodes);
        Assert.False(app.IsCommon);
    }

    [Fact]
    public async Task Catalogue_transport_failure_throws_CtmsOfflineException()
    {
        var handler = new StubHttpMessageHandler().EnqueueThrow(new HttpRequestException("down"));
        var client = TestClient.Create(handler, out _);

        await Assert.ThrowsAsync<CtmsOfflineException>(() => client.GetLanguagesAsync());
    }
}
