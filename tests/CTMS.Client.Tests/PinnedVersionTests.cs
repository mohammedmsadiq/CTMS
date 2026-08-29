using CTMS.Client.Caching;

namespace CTMS.Client.Tests;

public sealed class PinnedVersionTests
{
    private static readonly Dictionary<string, string> Entries = new() { ["k"] = "v3" };

    [Fact]
    public async Task Pinned_fetch_never_sends_if_none_match_and_serves_from_cache_on_repeat()
    {
        var store = new InMemoryBundleStore();
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.BundleOk(TestClient.ProjectId, "fr", 3, Entries));
        var client = TestClient.Create(handler, out _, store);

        var first = await client.GetBundleAsync("fr", 3);
        var second = await client.GetBundleAsync("fr", 3);

        Assert.Equal(1, handler.CallCount);
        Assert.Empty(handler.Requests[0].IfNoneMatch);
        Assert.EndsWith($"/bundles/fr/versions/3", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal(3, first.Version);
        Assert.Equal(3, second.Version);
        Assert.False(second.IsStale);

        var cached = await store.GetAsync(TestClient.ProjectId, "fr.v3");
        Assert.NotNull(cached);
    }

    [Fact]
    public async Task Pinned_and_latest_use_separate_cache_slots()
    {
        var store = new InMemoryBundleStore();
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.BundleOk(TestClient.ProjectId, "fr", 3, Entries))
            .Enqueue(_ => StubHttpMessageHandler.BundleOk(TestClient.ProjectId, "fr", 5, new Dictionary<string, string> { ["k"] = "v5" }));
        var client = TestClient.Create(handler, out _, store);

        await client.GetBundleAsync("fr", 3);
        var latest = await client.GetBundleAsync("fr");

        Assert.Equal(5, latest.Version);
        Assert.Equal(3, (await store.GetAsync(TestClient.ProjectId, "fr.v3"))!.Version);
        Assert.Equal(5, (await store.GetAsync(TestClient.ProjectId, "fr"))!.Version);
    }

    [Fact]
    public async Task Versions_list_is_mapped()
    {
        const string json =
            "[{\"version\":1,\"etag\":\"a\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"createdBy\":\"x\",\"entryCount\":2}," +
            "{\"version\":2,\"etag\":\"b\",\"createdAt\":\"2026-01-02T00:00:00Z\",\"createdBy\":\"y\",\"entryCount\":3}]";
        var handler = new StubHttpMessageHandler().Enqueue(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        var client = TestClient.Create(handler, out _);

        var versions = await client.GetVersionsAsync("fr");

        Assert.Equal(2, versions.Count);
        Assert.Equal(1, versions[0].Version);
        Assert.Equal("b", versions[1].Etag);
        Assert.Equal(3, versions[1].EntryCount);
        Assert.EndsWith("/bundles/fr/versions", handler.Requests[0].Uri.AbsolutePath);
    }
}
