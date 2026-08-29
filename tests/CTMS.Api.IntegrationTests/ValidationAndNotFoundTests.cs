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
    public async Task Unknown_project_id_is_404()
    {
        using var client = Factory.ClientAs(AuthRoles.Reader);

        using var response = await client.GetAsync($"/api/projects/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_project_slug_is_409()
    {
        using var client = Factory.ClientAs(AuthRoles.Admin);
        var slug = ApiHelpers.UniqueName("dup");

        await client.CreateProjectAsync(slug: slug);

        using var second = await client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest(ApiHelpers.UniqueName("Other"), "en", slug));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Bad_key_name_charset_is_400()
    {
        using var admin = Factory.ClientAs(AuthRoles.Admin);
        using var client = Factory.ClientAs(AuthRoles.Manager);
        var project = await admin.CreateProjectAsync();

        using var response = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/keys",
            new CreateTranslationKeyRequest("not a valid key!", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_review_state_filter_is_400()
    {
        using var client = Factory.ClientAs(AuthRoles.Reader);
        using var admin = Factory.ClientAs(AuthRoles.Admin);
        var project = await admin.CreateProjectAsync();

        using var response = await client.GetAsync(
            $"/api/projects/{project.Id}/strings?reviewState=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
