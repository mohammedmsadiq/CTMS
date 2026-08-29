using System.Net;
using System.Net.Http.Json;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;
using CTMS.Application.Translations;

namespace CTMS.Api.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class LifecycleTests(MongoFixture mongo) : IntegrationTest(mongo)
{
    [Fact]
    public async Task Create_translate_review_publish_then_read_the_bundle()
    {
        using var client = Factory.ClientAsActor("release-manager", AuthRoles.Admin);

        var project = await client.CreateProjectAsync(slug: ApiHelpers.UniqueName("lifecycle"));

        var en = await client.CreateLocaleAsync(project.Id, "en", "English");
        var fr = await client.CreateLocaleAsync(project.Id, "fr", "French");
        Assert.NotEqual(en.Id, fr.Id);

        var title = await client.CreateKeyAsync(project.Id, "home.hero.title");
        var cta = await client.CreateKeyAsync(project.Id, "home.hero.cta");

        await client.UpsertStringAsync(project.Id, title.Id, en.Id, "Ship translations faster");
        await client.UpsertStringAsync(project.Id, cta.Id, en.Id, "Start free trial");

        foreach (var key in new[] { title, cta })
        {
            await client.ReviewAsync(project.Id, key.Id, en.Id, "submit");
            await client.ReviewAsync(project.Id, key.Id, en.Id, "approve");
            await client.ReviewAsync(project.Id, key.Id, en.Id, "publish");
        }

        var published = await client.PublishBundleAsync(project.Id, "en");
        Assert.Equal(1, published.Version);
        Assert.Equal("en", published.LocaleCode);

        using var getResponse = await client.GetAsync($"/api/projects/{project.Id}/bundles/en");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var bundle = (await getResponse.Content.ReadFromJsonAsync<TranslationBundleDto>())!;
        Assert.Equal(1, bundle.Version);
        Assert.Equal(published.ETag, bundle.ETag);
        Assert.False(string.IsNullOrWhiteSpace(bundle.ETag));
        Assert.Equal(
            new[] { "home.hero.cta", "home.hero.title" },
            bundle.Entries.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("Ship translations faster", bundle.Entries["home.hero.title"]);
        Assert.Equal("Start free trial", bundle.Entries["home.hero.cta"]);
    }
}
