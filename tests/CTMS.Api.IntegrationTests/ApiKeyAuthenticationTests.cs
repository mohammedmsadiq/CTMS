using System.Net;
using System.Net.Http.Json;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;
using CTMS.Application.ApiKeys;
using CTMS.Application.Languages;
using CTMS.Application.Projects;
using CTMS.Application.Translations;
using CTMS.Domain.Languages;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CTMS.Api.IntegrationTests;

/// <summary>
/// End-to-end <c>X-Api-Key</c> auth with <c>Auth:Enabled=true</c> (real scheme wiring, no
/// dev-bypass, no <see cref="TestAuthHandler"/> override) and <c>Auth:PublicBundleReads=false</c>
/// so the delivery route actually needs a credential: a minted key reads, a bogus key is 401,
/// a key on a write route is 403, and <c>LastUsedAt</c> is stamped.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ApiKeyAuthenticationTests(MongoFixture mongo) : IAsyncLifetime
{
    private const string AppCode = "keyapp";
    private const string Language = "en-GB";

    private ApiKeyAuthFactory _factory = null!;
    private string _rawKey = null!;
    private Guid _keyId;

    public async Task InitializeAsync()
    {
        _factory = new ApiKeyAuthFactory(mongo.ConnectionString);
        _ = _factory.Server; // force startup

        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var languages = sp.GetRequiredService<ILanguageRepository>();
        await languages.AddAsync(new Language(Language, "English"));

        var projects = sp.GetRequiredService<IProjectRepository>();
        var project = new Project("Key App", AppCode, Language);
        project.SetEnabledLanguages([Language]);
        await projects.AddAsync(project);

        var keys = sp.GetRequiredService<ITranslationKeyRepository>();
        var key = new TranslationKey(project.Id, "greeting", "Common", "seed");
        await keys.AddAsync(key);

        var strings = sp.GetRequiredService<ITranslationStringRepository>();
        var value = new TranslationString(key.Id, Language, "hello", "seed");
        value.ChangeReviewState(ReviewState.NeedsReview, "seed");
        value.ChangeReviewState(ReviewState.Approved, "seed");
        value.ChangeReviewState(ReviewState.Published, "seed");
        await strings.AddAsync(value);

        var minted = await sp.GetRequiredService<ApiKeyService>()
            .CreateAsync(new CreateApiKeyRequest("ci-bot"), "admin");
        _rawKey = minted.Key;
        _keyId = minted.Id;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient Client() => _factory.CreateClient();

    [Fact]
    public async Task A_minted_key_authenticates_the_delivery_read_when_bundle_reads_are_private()
    {
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/translations/{AppCode}/{Language}");
        request.Headers.Add(ApiKeyAuthenticationHandler.HeaderName, _rawKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PublishedTranslationsResponse>();
        Assert.Equal("hello", body!.Translations["greeting"]);
    }

    [Fact]
    public async Task No_key_on_a_private_delivery_read_is_401()
    {
        using var client = Client();

        using var response = await client.GetAsync($"/api/translations/{AppCode}/{Language}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_bogus_key_is_401()
    {
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/translations/{AppCode}/{Language}");
        request.Headers.Add(ApiKeyAuthenticationHandler.HeaderName, "ctms_not_a_real_key_0000000000000000000000");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_api_key_on_a_write_route_is_403_reader_only()
    {
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/applications")
        {
            Content = JsonContent.Create(new CreateApplicationRequest("Blocked", Language, "blocked")),
        };
        request.Headers.Add(ApiKeyAuthenticationHandler.HeaderName, _rawKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LastUsedAt_is_stamped_after_a_successful_authentication()
    {
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/translations/{AppCode}/{Language}");
        request.Headers.Add(ApiKeyAuthenticationHandler.HeaderName, _rawKey);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        DateTime? lastUsed = null;
        for (var attempt = 0; attempt < 40 && lastUsed is null; attempt++)
        {
            await Task.Delay(50);
            using var scope = _factory.Services.CreateScope();
            var keys = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().ListAsync();
            lastUsed = keys.Single(k => k.Id == _keyId).LastUsedAt;
        }

        Assert.NotNull(lastUsed);
    }

    /// <summary>
    /// Real auth wiring: <c>Auth:Enabled=true</c>, no <see cref="TestAuthHandler"/> override, a
    /// private delivery path. Points at the shared test MongoDB with a per-factory database.
    /// </summary>
    private sealed class ApiKeyAuthFactory(string connectionString)
        : WebApplicationFactory<DevBypassAuthHandler>
    {
        private readonly string _databaseName = "ctms_apikey_" + Guid.NewGuid().ToString("N");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:CtmsDatabase", connectionString);
            builder.UseSetting("Mongo:Database", _databaseName);
            builder.UseSetting("ConnectionStrings:Redis", string.Empty);
            builder.UseSetting("Seed:Enabled", "false");
            builder.UseSetting("Auth:Enabled", "true");
            builder.UseSetting("Auth:PublicBundleReads", "false");
            builder.UseSetting("RateLimit:Enabled", "false");
            builder.UseSetting("Webhooks:Enabled", "false");
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await new MongoDB.Driver.MongoClient(connectionString).DropDatabaseAsync(_databaseName);
            }
            catch
            {
                // best-effort
            }

            await base.DisposeAsync();
        }
    }
}
