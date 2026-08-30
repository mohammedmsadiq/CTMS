using System.Net;
using System.Net.Http.Json;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;
using CTMS.Application.Projects;
using CTMS.Application.Translations;

namespace CTMS.Api.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ValidationAndNotFoundTests(MongoFixture mongo) : IntegrationTest(mongo)
{
    [Fact]
    public async Task Unknown_application_code_is_404()
    {
        using var client = Factory.ClientAs(AuthRoles.Reader);

        using var response = await client.GetAsync($"/api/projects/{ApiHelpers.UniqueName("nope")}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_application_code_is_409()
    {
        using var client = Factory.ClientAs(AuthRoles.Admin);
        var code = ApiHelpers.UniqueName("dup");

        await client.CreateApplicationAsync(code: code);

        using var second = await client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest(ApiHelpers.UniqueName("Other"), "en-GB", code));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Bad_key_name_charset_is_400()
    {
        using var admin = Factory.ClientAs(AuthRoles.Admin);
        using var client = Factory.ClientAs(AuthRoles.Manager);
        var app = await admin.CreateApplicationAsync();

        using var response = await client.PostAsJsonAsync(
            $"/api/projects/{app.Code}/keys",
            new CreateTranslationKeyRequest("not a valid key!", "Common"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_category_on_a_key_is_derived_from_the_key_name_prefix()
    {
        using var admin = Factory.ClientAs(AuthRoles.Admin);
        var app = await admin.CreateApplicationAsync();

        using var response = await admin.PostAsJsonAsync(
            $"/api/projects/{app.Code}/keys",
            new CreateTranslationKeyRequest("valid.key", ""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var key = (await response.Content.ReadFromJsonAsync<TranslationKeyDto>())!;
        Assert.Equal("Valid", key.Category);
    }

    [Fact]
    public async Task Unknown_review_state_filter_is_400()
    {
        using var admin = Factory.ClientAs(AuthRoles.Admin);
        var app = await admin.CreateApplicationAsync();

        using var response = await admin.GetAsync(
            $"/api/projects/{app.Code}/strings?reviewState=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upsert_to_a_language_not_enabled_for_the_application_is_404()
    {
        using var admin = Factory.ClientAs(AuthRoles.Admin);
        await admin.CreateLanguageAsync("en-GB", "English");
        await admin.CreateLanguageAsync("de-DE", "German");
        var app = await admin.CreateApplicationAsync(enabledLanguageCodes: ["en-GB"]);
        var key = await admin.CreateKeyAsync(app.Code, "k.one");

        using var response = await admin.PutStringRaw(app.Code, key.Id, "de-DE", "Wert");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
