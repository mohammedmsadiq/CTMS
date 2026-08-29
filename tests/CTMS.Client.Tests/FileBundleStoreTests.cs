using CTMS.Client.Caching;

namespace CTMS.Client.Tests;

public sealed class FileBundleStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ctms-client-tests", Guid.NewGuid().ToString("N"));

    private static StoredBundle Sample() => new()
    {
        ProjectId = TestClient.ProjectId,
        LocaleCode = "fr-CA",
        Version = 7,
        Entries = new(StringComparer.Ordinal) { ["a"] = "1", ["b"] = "2" },
        Etag = "deadbeef",
        CreatedBy = "tester",
        CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        RetrievedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
        LastValidatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
    };

    [Fact]
    public async Task Round_trips_a_bundle()
    {
        var store = new FileBundleStore(_dir);
        await store.SetAsync(TestClient.ProjectId, "fr-ca", Sample());

        var read = await store.GetAsync(TestClient.ProjectId, "fr-ca");

        Assert.NotNull(read);
        Assert.Equal(7, read!.Version);
        Assert.Equal("fr-CA", read.LocaleCode);
        Assert.Equal("1", read.Entries["a"]);
        Assert.Equal("deadbeef", read.Etag);
    }

    [Fact]
    public async Task Missing_file_is_a_cache_miss()
    {
        var store = new FileBundleStore(_dir);
        Assert.Null(await store.GetAsync(TestClient.ProjectId, "nope"));
    }

    [Fact]
    public async Task Corrupt_file_is_treated_as_a_miss()
    {
        var store = new FileBundleStore(_dir);
        await store.SetAsync(TestClient.ProjectId, "fr-ca", Sample());

        var file = Directory.GetFiles(Path.Combine(_dir, TestClient.ProjectId.ToString("D")), "*.json").Single();
        File.WriteAllText(file, "{ this is not valid json");

        Assert.Null(await store.GetAsync(TestClient.ProjectId, "fr-ca"));
    }

    [Fact]
    public async Task Atomic_write_leaves_no_temp_file()
    {
        var store = new FileBundleStore(_dir);
        await store.SetAsync(TestClient.ProjectId, "fr-ca", Sample());
        await store.SetAsync(TestClient.ProjectId, "fr-ca", Sample()); // overwrite path

        var projectDir = Path.Combine(_dir, TestClient.ProjectId.ToString("D"));
        Assert.Single(Directory.GetFiles(projectDir));
        Assert.Empty(Directory.GetFiles(projectDir, "*.tmp"));
    }

    [Fact]
    public async Task Remove_deletes_the_file()
    {
        var store = new FileBundleStore(_dir);
        await store.SetAsync(TestClient.ProjectId, "fr-ca", Sample());
        await store.RemoveAsync(TestClient.ProjectId, "fr-ca");

        Assert.Null(await store.GetAsync(TestClient.ProjectId, "fr-ca"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }
}
