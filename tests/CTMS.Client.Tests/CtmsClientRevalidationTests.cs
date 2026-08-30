using System.Net;
using CTMS.Client.Caching;

namespace CTMS.Client.Tests;

public sealed class CtmsClientRevalidationTests
{
    private static readonly Dictionary<string, string> V1 = new() { ["common.save"] = "Enregistrer", ["course.start"] = "Commencer" };
    private static readonly Dictionary<string, string> V2 = new() { ["common.save"] = "Enregistrer", ["course.start"] = "Commencer le cours" };

    [Fact]
    public async Task First_call_sends_no_if_none_match_and_caches_the_response()
    {
        var store = new InMemoryTranslationStore();
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.TranslationsOk(TestClient.Application, "fr-FR", V1));
        var client = TestClient.Create(handler, out _, store);

        var set = await client.GetTranslationsAsync("fr-FR");

        Assert.Equal("Enregistrer", set.Entries["common.save"]);
        Assert.False(set.IsStale);
        Assert.Empty(handler.Requests[0].IfNoneMatch);
        Assert.Equal($"/api/translations/{TestClient.Application}/fr-FR", handler.Requests[0].Uri.AbsolutePath);

        var cached = await store.GetAsync(TestClient.Application, "fr-FR");
        Assert.NotNull(cached);
        Assert.Equal(StubHttpMessageHandler.ComputeEtag(V1), cached!.Etag);
    }

    [Fact]
    public async Task Second_call_revalidates_with_if_none_match_and_a_304_returns_cache_without_reparsing()
    {
        var etag = StubHttpMessageHandler.ComputeEtag(V1);
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.TranslationsOk(TestClient.Application, "fr-FR", V1))
            .Enqueue(_ => StubHttpMessageHandler.NotModified(etag)); // body is invalid JSON on purpose
        var client = TestClient.Create(handler, out _);

        await client.GetTranslationsAsync("fr-FR");
        var revalidated = await client.GetTranslationsAsync("fr-FR");

        Assert.Equal(2, handler.CallCount);
        Assert.Equal($"\"{etag}\"", Assert.Single(handler.Requests[1].IfNoneMatch));
        Assert.Equal("Enregistrer", revalidated.Entries["common.save"]);
        Assert.False(revalidated.IsStale);
    }

    [Fact]
    public async Task A_200_with_a_new_etag_replaces_the_cache()
    {
        var store = new InMemoryTranslationStore();
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.TranslationsOk(TestClient.Application, "fr-FR", V1))
            .Enqueue(_ => StubHttpMessageHandler.TranslationsOk(TestClient.Application, "fr-FR", V2));
        var client = TestClient.Create(handler, out _, store);

        await client.GetTranslationsAsync("fr-FR");
        var updated = await client.GetTranslationsAsync("fr-FR");

        Assert.Equal("Commencer le cours", updated.Entries["course.start"]);
        var cached = await store.GetAsync(TestClient.Application, "fr-FR");
        Assert.Equal(StubHttpMessageHandler.ComputeEtag(V2), cached!.Etag);
    }

    [Fact]
    public async Task Within_the_staleness_window_the_cache_is_served_without_a_request()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.TranslationsOk(TestClient.Application, "fr-FR", V1));
        var client = TestClient.Create(handler, out var clock, configure: o => o.StalenessTtl = TimeSpan.FromMinutes(10));

        await client.GetTranslationsAsync("fr-FR");
        clock.Advance(TimeSpan.FromMinutes(5));
        var again = await client.GetTranslationsAsync("fr-FR");

        Assert.Equal(1, handler.CallCount);
        Assert.False(again.IsStale);
    }

    [Fact]
    public async Task Network_failure_with_a_warm_cache_returns_the_cached_set_marked_stale()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.TranslationsOk(TestClient.Application, "fr-FR", V1))
            .EnqueueThrow(new HttpRequestException("boom"));
        var client = TestClient.Create(handler, out _);

        await client.GetTranslationsAsync("fr-FR");
        var stale = await client.GetTranslationsAsync("fr-FR");

        Assert.True(stale.IsStale);
        Assert.Equal("Enregistrer", stale.Entries["common.save"]);
    }

    [Fact]
    public async Task Network_failure_with_a_cold_cache_throws_CtmsOfflineException()
    {
        var handler = new StubHttpMessageHandler().EnqueueThrow(new HttpRequestException("boom"));
        var client = TestClient.Create(handler, out _);

        await Assert.ThrowsAsync<CtmsOfflineException>(() => client.GetTranslationsAsync("fr-FR"));
    }

    [Fact]
    public async Task An_api_problem_response_throws_CtmsApiException_with_status_title_and_detail()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.Problem(HttpStatusCode.NotFound, "Resource not found", "Unknown application 'icoach'"));
        var client = TestClient.Create(handler, out _);

        var ex = await Assert.ThrowsAsync<CtmsApiException>(() => client.GetTranslationsAsync("fr-FR"));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("Resource not found", ex.Title);
        Assert.Equal("Unknown application 'icoach'", ex.Detail);
    }

    [Fact]
    public async Task Past_the_staleness_window_a_304_refreshes_last_validated_and_clears_stale()
    {
        var etag = StubHttpMessageHandler.ComputeEtag(V1);
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.TranslationsOk(TestClient.Application, "fr-FR", V1))
            .Enqueue(_ => StubHttpMessageHandler.NotModified(etag));
        var client = TestClient.Create(handler, out var clock, configure: o => o.StalenessTtl = TimeSpan.FromMinutes(1));

        var first = await client.GetTranslationsAsync("fr-FR");
        clock.Advance(TimeSpan.FromMinutes(5));
        var second = await client.GetTranslationsAsync("fr-FR");

        Assert.Equal(2, handler.CallCount);
        Assert.True(second.LastValidatedAt > first.LastValidatedAt);
        Assert.False(second.IsStale);
    }

    [Fact]
    public async Task A_304_with_a_cold_cache_throws_CtmsApiException()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.NotModified("deadbeef"));
        var client = TestClient.Create(handler, out _);

        var ex = await Assert.ThrowsAsync<CtmsApiException>(() => client.GetTranslationsAsync("fr-FR"));
        Assert.Equal(304, ex.StatusCode);
    }

    [Fact]
    public async Task Auth_token_is_sent_as_a_bearer_header()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.TranslationsOk(TestClient.Application, "fr-FR", V1));
        var client = TestClient.Create(handler, out _, configure: o => o.AuthToken = "secret-token");

        await client.GetTranslationsAsync("fr-FR");

        Assert.Equal("Bearer secret-token", handler.Requests[0].Authorization);
    }
}
