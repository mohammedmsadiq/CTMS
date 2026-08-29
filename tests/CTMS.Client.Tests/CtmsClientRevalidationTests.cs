using System.Net;
using CTMS.Client.Caching;

namespace CTMS.Client.Tests;

public sealed class CtmsClientRevalidationTests
{
    private static readonly Dictionary<string, string> V1 = new() { ["greeting"] = "Hello", ["bye"] = "Bye" };
    private static readonly Dictionary<string, string> V2 = new() { ["greeting"] = "Hello there", ["bye"] = "Bye" };

    [Fact]
    public async Task First_call_sends_no_if_none_match_and_caches_the_response()
    {
        var store = new InMemoryBundleStore();
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.BundleOk(TestClient.ProjectId, "en", 1, V1));
        var client = TestClient.Create(handler, out _, store);

        var bundle = await client.GetBundleAsync("en");

        Assert.Equal(1, bundle.Version);
        Assert.Equal("Hello", bundle.Entries["greeting"]);
        Assert.False(bundle.IsStale);
        Assert.Empty(handler.Requests[0].IfNoneMatch);
        Assert.EndsWith($"/api/projects/{TestClient.ProjectId:D}/bundles/en", handler.Requests[0].Uri.AbsolutePath);

        var cached = await store.GetAsync(TestClient.ProjectId, "en");
        Assert.NotNull(cached);
        Assert.Equal(StubHttpMessageHandler.ComputeEtag(V1), cached!.Etag);
    }

    [Fact]
    public async Task Second_call_revalidates_with_if_none_match_and_a_304_returns_cache_without_reparsing()
    {
        var etag = StubHttpMessageHandler.ComputeEtag(V1);
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.BundleOk(TestClient.ProjectId, "en", 1, V1))
            .Enqueue(_ => StubHttpMessageHandler.NotModified(etag)); // body is invalid JSON on purpose
        var client = TestClient.Create(handler, out _);

        await client.GetBundleAsync("en");
        var revalidated = await client.GetBundleAsync("en");

        Assert.Equal(2, handler.CallCount);
        Assert.Equal($"\"{etag}\"", Assert.Single(handler.Requests[1].IfNoneMatch));
        Assert.Equal(1, revalidated.Version);
        Assert.Equal("Hello", revalidated.Entries["greeting"]);
        Assert.False(revalidated.IsStale);
    }

    [Fact]
    public async Task A_200_with_a_new_etag_replaces_the_cache()
    {
        var store = new InMemoryBundleStore();
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.BundleOk(TestClient.ProjectId, "en", 1, V1))
            .Enqueue(_ => StubHttpMessageHandler.BundleOk(TestClient.ProjectId, "en", 2, V2));
        var client = TestClient.Create(handler, out _, store);

        await client.GetBundleAsync("en");
        var updated = await client.GetBundleAsync("en");

        Assert.Equal(2, updated.Version);
        Assert.Equal("Hello there", updated.Entries["greeting"]);
        var cached = await store.GetAsync(TestClient.ProjectId, "en");
        Assert.Equal(2, cached!.Version);
        Assert.Equal(StubHttpMessageHandler.ComputeEtag(V2), cached.Etag);
    }

    [Fact]
    public async Task Within_the_staleness_window_the_cache_is_served_without_a_request()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.BundleOk(TestClient.ProjectId, "en", 1, V1));
        var client = TestClient.Create(handler, out var clock, configure: o => o.StalenessTtl = TimeSpan.FromMinutes(10));

        await client.GetBundleAsync("en");
        clock.Advance(TimeSpan.FromMinutes(5));
        var again = await client.GetBundleAsync("en");

        Assert.Equal(1, handler.CallCount);
        Assert.False(again.IsStale);
    }

    [Fact]
    public async Task Network_failure_with_a_warm_cache_returns_the_cached_bundle_marked_stale()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.BundleOk(TestClient.ProjectId, "en", 1, V1))
            .EnqueueThrow(new HttpRequestException("boom"));
        var client = TestClient.Create(handler, out _);

        await client.GetBundleAsync("en");
        var stale = await client.GetBundleAsync("en");

        Assert.True(stale.IsStale);
        Assert.Equal("Hello", stale.Entries["greeting"]);
    }

    [Fact]
    public async Task Network_failure_with_a_cold_cache_throws_CtmsOfflineException()
    {
        var handler = new StubHttpMessageHandler().EnqueueThrow(new HttpRequestException("boom"));
        var client = TestClient.Create(handler, out _);

        await Assert.ThrowsAsync<CtmsOfflineException>(() => client.GetBundleAsync("en"));
    }

    [Fact]
    public async Task An_api_problem_response_throws_CtmsApiException_with_status_and_detail()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.Problem(HttpStatusCode.NotFound, "Not Found", "No bundle published"));
        var client = TestClient.Create(handler, out _);

        var ex = await Assert.ThrowsAsync<CtmsApiException>(() => client.GetBundleAsync("en"));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("Not Found", ex.Title);
        Assert.Equal("No bundle published", ex.Detail);
    }

    [Fact]
    public async Task Past_the_staleness_window_a_304_refreshes_last_validated_and_clears_stale()
    {
        var etag = StubHttpMessageHandler.ComputeEtag(V1);
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.BundleOk(TestClient.ProjectId, "en", 1, V1))
            .Enqueue(_ => StubHttpMessageHandler.NotModified(etag));
        var client = TestClient.Create(handler, out var clock, configure: o => o.StalenessTtl = TimeSpan.FromMinutes(1));

        var first = await client.GetBundleAsync("en");
        clock.Advance(TimeSpan.FromMinutes(5));
        var second = await client.GetBundleAsync("en");

        Assert.Equal(2, handler.CallCount);
        Assert.True(second.LastValidatedAt > first.LastValidatedAt);
        Assert.False(second.IsStale);
    }
}
