using CTMS.Client.Caching;

namespace CTMS.Client.Tests;

public sealed class FileTranslationStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ctms-client-tests", Guid.NewGuid().ToString("N"));

    private static StoredTranslations Sample() => new()
    {
        Application = TestClient.Application,
        Language = "fr-FR",
        Entries = new(StringComparer.Ordinal) { ["a"] = "1", ["b"] = "2" },
        Etag = "deadbeef",
        RetrievedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
        LastValidatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
    };

    private string AppDir => Path.Combine(_dir, TestClient.Application);

    [Fact]
    public async Task Round_trips_a_set()
    {
        var store = new FileTranslationStore(_dir);
        await store.SetAsync(TestClient.Application, "fr-FR", Sample());

        var read = await store.GetAsync(TestClient.Application, "fr-FR");

        Assert.NotNull(read);
        Assert.Equal("fr-FR", read!.Language);
        Assert.Equal("1", read.Entries["a"]);
        Assert.Equal("deadbeef", read.Etag);
        Assert.Equal(DateTimeOffset.Parse("2026-01-02T00:00:00Z"), read.LastValidatedAt);
    }

    [Fact]
    public async Task Writes_a_directly_consumable_flat_map_plus_a_sibling_meta_file()
    {
        var store = new FileTranslationStore(_dir);
        await store.SetAsync(TestClient.Application, "fr-FR", Sample());

        var data = await File.ReadAllTextAsync(Path.Combine(AppDir, "fr-fr.json"));
        Assert.Contains("\"a\":\"1\"", data);
        Assert.DoesNotContain("etag", data);
        Assert.True(File.Exists(Path.Combine(AppDir, "fr-fr.meta.json")));
    }

    [Fact]
    public async Task Missing_file_is_a_cache_miss()
    {
        var store = new FileTranslationStore(_dir);
        Assert.Null(await store.GetAsync(TestClient.Application, "nope"));
    }

    [Fact]
    public async Task Corrupt_data_file_is_treated_as_a_miss()
    {
        var store = new FileTranslationStore(_dir);
        await store.SetAsync(TestClient.Application, "fr-FR", Sample());

        await File.WriteAllTextAsync(Path.Combine(AppDir, "fr-fr.json"), "{ this is not valid json");

        Assert.Null(await store.GetAsync(TestClient.Application, "fr-FR"));
    }

    [Fact]
    public async Task Missing_meta_sibling_is_treated_as_a_miss()
    {
        var store = new FileTranslationStore(_dir);
        await store.SetAsync(TestClient.Application, "fr-FR", Sample());

        File.Delete(Path.Combine(AppDir, "fr-fr.meta.json"));

        Assert.Null(await store.GetAsync(TestClient.Application, "fr-FR"));
    }

    [Fact]
    public async Task Atomic_write_leaves_no_temp_file()
    {
        var store = new FileTranslationStore(_dir);
        await store.SetAsync(TestClient.Application, "fr-FR", Sample());
        await store.SetAsync(TestClient.Application, "fr-FR", Sample()); // overwrite path

        Assert.Empty(Directory.GetFiles(AppDir, "*.tmp"));
        Assert.Equal(2, Directory.GetFiles(AppDir).Length); // data + meta, no duplicates
    }

    [Fact]
    public async Task Remove_deletes_both_files()
    {
        var store = new FileTranslationStore(_dir);
        await store.SetAsync(TestClient.Application, "fr-FR", Sample());
        await store.RemoveAsync(TestClient.Application, "fr-FR");

        Assert.Null(await store.GetAsync(TestClient.Application, "fr-FR"));
        Assert.False(Directory.Exists(AppDir) && Directory.GetFiles(AppDir).Length > 0);
    }

    [Fact]
    public async Task Round_trips_through_the_client_offline_path()
    {
        var handler = new StubHttpMessageHandler()
            .Enqueue(_ => StubHttpMessageHandler.TranslationsOk(TestClient.Application, "fr-FR", new Dictionary<string, string> { ["k"] = "v" }))
            .EnqueueThrow(new HttpRequestException("down"));
        var client = TestClient.Create(handler, out _, new FileTranslationStore(_dir));

        await client.GetTranslationsAsync("fr-FR");
        var offline = await client.GetTranslationsAsync("fr-FR");

        Assert.True(offline.IsStale);
        Assert.Equal("v", offline.Entries["k"]);
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
