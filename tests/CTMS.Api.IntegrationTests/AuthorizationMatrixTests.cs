using System.Net;
using System.Net.Http.Json;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;
using CTMS.Application.Projects;
using CTMS.Application.Translations;

namespace CTMS.Api.IntegrationTests;

/// <summary>
/// The role → HTTP-status matrix, one representative endpoint per authorization policy, driven
/// through the real pipeline (<see cref="TestAuthHandler"/> as the default scheme, the real
/// <c>AuthorizationPolicies</c>). <c>null</c> role means an anonymous request.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AuthorizationMatrixTests(MongoFixture mongo) : IntegrationTest(mongo)
{
    private HttpClient _admin = null!;
    private ProjectDto _app = null!;
    private TranslationKeyDto _existingKey = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _admin = Factory.ClientAs(AuthRoles.Admin);
        await _admin.CreateLanguageAsync("en-GB", "English");
        _app = await _admin.CreateApplicationAsync(enabledLanguageCodes: ["en-GB"]);

        await _admin.PublishStringAsync(_app.Code, "en-GB", "matrix.published", "hello");

        _existingKey = await _admin.CreateKeyAsync(_app.Code, "matrix.existing");
        await _admin.UpsertStringAsync(_app.Code, _existingKey.Id, "en-GB", "x");
    }

    private HttpClient ClientFor(string? role) =>
        role is null ? Factory.AnonymousClient() : Factory.ClientAs(role);

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(AuthRoles.Reader, HttpStatusCode.OK)]
    [InlineData(AuthRoles.Translator, HttpStatusCode.OK)]
    [InlineData(AuthRoles.Reviewer, HttpStatusCode.OK)]
    [InlineData(AuthRoles.Manager, HttpStatusCode.OK)]
    [InlineData(AuthRoles.Admin, HttpStatusCode.OK)]
    public async Task Get_application_needs_CanRead(string? role, HttpStatusCode expected)
    {
        using var client = ClientFor(role);

        using var response = await client.GetAsync($"/api/projects/{_app.Code}");

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(AuthRoles.Reader, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Translator, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Reviewer, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Manager, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Admin, HttpStatusCode.Created)]
    public async Task Post_applications_needs_CanAdminProjects(string? role, HttpStatusCode expected)
    {
        using var client = ClientFor(role);

        using var response = await client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest(ApiHelpers.UniqueName("authz"), "en-GB"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(AuthRoles.Reader, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Translator, HttpStatusCode.OK)]
    [InlineData(AuthRoles.Reviewer, HttpStatusCode.OK)]
    [InlineData(AuthRoles.Manager, HttpStatusCode.OK)]
    [InlineData(AuthRoles.Admin, HttpStatusCode.OK)]
    public async Task Put_string_needs_CanEditStrings(string? role, HttpStatusCode expected)
    {
        using var client = ClientFor(role);

        using var response = await client.PutStringRaw(
            _app.Code, _existingKey.Id, "en-GB", "edited-by-" + (role ?? "anon"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(AuthRoles.Reader, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Translator, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Reviewer, HttpStatusCode.OK)]
    [InlineData(AuthRoles.Manager, HttpStatusCode.OK)]
    [InlineData(AuthRoles.Admin, HttpStatusCode.OK)]
    public async Task Post_review_needs_CanReview(string? role, HttpStatusCode expected)
    {
        var key = await _admin.CreateKeyAsync(_app.Code);
        await _admin.UpsertStringAsync(_app.Code, key.Id, "en-GB", "v");
        await _admin.ReviewAsync(_app.Code, key.Id, "en-GB", "submit");

        using var client = ClientFor(role);

        using var response = await client.ReviewRaw(_app.Code, key.Id, "en-GB", "approve");

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(AuthRoles.Reader, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Translator, HttpStatusCode.OK)]   // spec §46 — a translator may submit their own work
    [InlineData(AuthRoles.Reviewer, HttpStatusCode.OK)]
    [InlineData(AuthRoles.Admin, HttpStatusCode.OK)]
    public async Task Post_review_submit_needs_CanEditStrings(string? role, HttpStatusCode expected)
    {
        var key = await _admin.CreateKeyAsync(_app.Code);
        await _admin.UpsertStringAsync(_app.Code, key.Id, "en-GB", "v");

        using var client = ClientFor(role);

        using var response = await client.ReviewRaw(_app.Code, key.Id, "en-GB", "submit");

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(AuthRoles.Reader, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Translator, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Reviewer, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Manager, HttpStatusCode.OK)]
    [InlineData(AuthRoles.Admin, HttpStatusCode.OK)]
    public async Task Post_bulk_publish_needs_CanPublish(string? role, HttpStatusCode expected)
    {
        using var client = ClientFor(role);

        using var response = await client.BulkPublishRaw(_app.Code);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(AuthRoles.Reader)]
    [InlineData(AuthRoles.Admin)]
    public async Task Get_translations_delivery_is_anonymous_while_PublicBundleReads_is_true(string? role)
    {
        using var client = ClientFor(role);

        using var response = await client.GetAsync($"/api/translations/{_app.Code}/en-GB");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_but_unrecognised_role_is_forbidden()
    {
        using var client = Factory.ClientAs("some.unknown.role");

        using var response = await client.GetAsync($"/api/projects/{_app.Code}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
