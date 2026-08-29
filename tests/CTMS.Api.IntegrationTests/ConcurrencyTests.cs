using System.Net;
using System.Text.Json;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;

namespace CTMS.Api.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ConcurrencyTests(MongoFixture mongo) : IntegrationTest(mongo)
{
    [Fact]
    public async Task Stale_expectedVersion_on_the_second_upsert_is_409_with_currentVersion()
    {
        using var admin = Factory.ClientAs(AuthRoles.Admin);
        using var client = Factory.ClientAs(AuthRoles.Translator);

        var project = await admin.CreateProjectAsync(slug: ApiHelpers.UniqueName("concurrency"));
        var en = await admin.CreateLocaleAsync(project.Id, "en", "English");
        var key = await admin.CreateKeyAsync(project.Id, "conflict.key");

        var created = await client.UpsertStringAsync(project.Id, key.Id, en.Id, "first");
        Assert.Equal(0, created.Version);

        var updated = await client.UpsertStringAsync(
            project.Id, key.Id, en.Id, "second", expectedVersion: 0);
        Assert.Equal(1, updated.Version);

        using var stale = await client.PutStringRaw(
            project.Id, key.Id, en.Id, "third", expectedVersion: 0);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var problem = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        Assert.Equal(1, problem.RootElement.GetProperty("currentVersion").GetInt64());
    }
}
