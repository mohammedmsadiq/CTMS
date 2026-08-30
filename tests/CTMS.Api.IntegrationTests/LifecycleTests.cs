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
    public async Task Create_translate_review_publish_then_read_the_assembled_translations()
    {
        using var client = Factory.ClientAsActor("release-manager", AuthRoles.Admin);

        await client.CreateLanguageAsync("en-GB", "English");
        await client.CreateLanguageAsync("fr-FR", "French", fallbackCode: "en-GB");

        var app = await client.CreateApplicationAsync(
            code: ApiHelpers.UniqueName("lifecycle"),
            baseLanguageCode: "en-GB",
            enabledLanguageCodes: ["en-GB", "fr-FR"]);

        var title = await client.CreateKeyAsync(app.Code, "home.hero.title", "Content");
        var cta = await client.CreateKeyAsync(app.Code, "home.hero.cta", "Content");

        await client.UpsertStringAsync(app.Code, title.Id, "en-GB", "Ship translations faster");
        await client.UpsertStringAsync(app.Code, cta.Id, "en-GB", "Start free trial");

        foreach (var key in new[] { title, cta })
        {
            await client.ReviewAsync(app.Code, key.Id, "en-GB", "submit");
            await client.ReviewAsync(app.Code, key.Id, "en-GB", "approve");
            await client.ReviewAsync(app.Code, key.Id, "en-GB", "publish");
        }

        using var getResponse = await client.GetAsync($"/api/translations/{app.Code}/en-GB");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(getResponse.Headers.ETag);

        var body = (await getResponse.Content.ReadFromJsonAsync<PublishedTranslationsResponse>())!;
        Assert.Equal(app.Code, body.Project);
        Assert.Equal("en-GB", body.Language);
        Assert.Equal(
            new[] { "home.hero.cta", "home.hero.title" },
            body.Translations.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("Ship translations faster", body.Translations["home.hero.title"]);
        Assert.Equal("Start free trial", body.Translations["home.hero.cta"]);
    }

    [Fact]
    public async Task Bulk_publish_promotes_every_approved_string()
    {
        using var admin = Factory.ClientAs(AuthRoles.Admin);

        await admin.CreateLanguageAsync("en-GB", "English");
        var app = await admin.CreateApplicationAsync(
            code: ApiHelpers.UniqueName("bulk"), enabledLanguageCodes: ["en-GB"]);

        var key = await admin.CreateKeyAsync(app.Code, "k.one", "Common");
        await admin.UpsertStringAsync(app.Code, key.Id, "en-GB", "One");
        await admin.ReviewAsync(app.Code, key.Id, "en-GB", "submit");
        await admin.ReviewAsync(app.Code, key.Id, "en-GB", "approve");

        var result = await admin.BulkPublishAsync(app.Code);
        Assert.Equal(1, result.Published);

        var body = (await admin.GetFromJsonAsync<PublishedTranslationsResponse>(
            $"/api/translations/{app.Code}/en-GB"))!;
        Assert.Equal("One", body.Translations["k.one"]);
    }
}
