using System.Net;
using System.Net.Http.Json;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;
using CTMS.Application.Locales;
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
    private ProjectDto _project = null!;
    private LocaleDto _en = null!;
    private TranslationKeyDto _existingKey = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _admin = Factory.ClientAs(AuthRoles.Admin);
        _project = await _admin.CreateProjectAsync();
        _en = await _admin.CreateLocaleAsync(_project.Id, "en", "English");

        // A Published string so a bundle can be cut and read.
        await _admin.PublishStringAsync(_project.Id, _en.Id, "matrix.published", "hello");
        await _admin.PublishBundleAsync(_project.Id, "en");

        // A plain string for negative mutation checks (authorization fails before it is touched).
        _existingKey = await _admin.CreateKeyAsync(_project.Id, "matrix.existing");
        await _admin.UpsertStringAsync(_project.Id, _existingKey.Id, _en.Id, "x");
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
    public async Task Get_projects_list(string? role, HttpStatusCode expected)
    {
        using var client = ClientFor(role);

        using var response = await client.GetAsync("/api/projects");

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(AuthRoles.Reader, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Translator, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Reviewer, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Manager, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Admin, HttpStatusCode.Created)]
    public async Task Post_projects_needs_CanAdminProjects(string? role, HttpStatusCode expected)
    {
        using var client = ClientFor(role);

        using var response = await client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest(ApiHelpers.UniqueName("authz"), "en", null));

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
            _project.Id, _existingKey.Id, _en.Id, "edited-by-" + (role ?? "anon"));

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
        // A fresh NeedsReview string per case, so an allowed role's "approve" is a legal move.
        var key = await _admin.CreateKeyAsync(_project.Id);
        await _admin.UpsertStringAsync(_project.Id, key.Id, _en.Id, "v");
        await _admin.ReviewAsync(_project.Id, key.Id, _en.Id, "submit");

        using var client = ClientFor(role);

        using var response = await client.ReviewRaw(_project.Id, key.Id, _en.Id, "approve");

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(AuthRoles.Reader, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Translator, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Reviewer, HttpStatusCode.Forbidden)]
    [InlineData(AuthRoles.Manager, HttpStatusCode.Created)]
    [InlineData(AuthRoles.Admin, HttpStatusCode.Created)]
    public async Task Post_bundle_needs_CanPublish(string? role, HttpStatusCode expected)
    {
        using var client = ClientFor(role);

        using var response = await client.PublishBundleRaw(_project.Id, "en");

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(AuthRoles.Reader)]
    [InlineData(AuthRoles.Admin)]
    public async Task Get_bundle_is_anonymous_while_PublicBundleReads_is_true(string? role)
    {
        using var client = ClientFor(role);

        using var response = await client.GetAsync($"/api/projects/{_project.Id}/bundles/en");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_but_unrecognised_role_is_forbidden_everywhere()
    {
        using var client = Factory.ClientAs("some.unknown.role");

        using var response = await client.GetAsync("/api/projects");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
