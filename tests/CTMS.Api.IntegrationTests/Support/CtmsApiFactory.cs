using CTMS.Api.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace CTMS.Api.IntegrationTests.Support;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> over the real API composition root
/// (<c>Program</c>), the real DI graph and a real MongoDB. <see cref="DevBypassAuthHandler"/>
/// is only used as the entry-point type token — it lives in the <c>CTMS.Api</c> assembly and
/// <c>Program</c> uses top-level statements, so there is no accessible <c>Program</c> type to
/// name here.
/// </summary>
/// <remarks>
/// Config overrides: Mongo points at the assembly's test server with a per-factory database;
/// <c>ConnectionStrings:Redis</c> is left unset so the in-memory <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
/// fallback backs the bundle cache; <c>Seed:Enabled=false</c>; <c>Auth:Enabled=false</c> (the
/// environment is <c>Development</c>, so this is allowed) — but the dev-bypass scheme is then
/// replaced by <see cref="TestAuthHandler"/> as the default scheme so the real
/// <c>AuthorizationPolicies</c> evaluate against header-driven roles.
/// <c>MongoIndexInitializer</c> is left running so the suite exercises the production indexes.
/// </remarks>
public sealed class CtmsApiFactory(
    string connectionString,
    IReadOnlyDictionary<string, string?>? settingOverrides = null)
    : WebApplicationFactory<DevBypassAuthHandler>
{
    private readonly string _connectionString = connectionString;
    private readonly IReadOnlyDictionary<string, string?> _settingOverrides =
        settingOverrides ?? new Dictionary<string, string?>();

    public string DatabaseName { get; } = "ctms_it_" + Guid.NewGuid().ToString("N");

    /// <summary>An <see cref="HttpClient"/> that authenticates as <paramref name="roles"/>.</summary>
    public HttpClient ClientAs(params string[] roles)
    {
        var client = CreateClient();
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        }

        return client;
    }

    /// <summary>An <see cref="HttpClient"/> that authenticates as <paramref name="roles"/> and
    /// carries <paramref name="actorName"/> as the token <c>name</c> claim. Distinct name from
    /// <see cref="ClientAs(string[])"/> so a single-string call (<c>ClientAs("TranslationAdministrator")</c>)
    /// is unambiguously a role, not an actor.</summary>
    public HttpClient ClientAsActor(string actorName, params string[] roles)
    {
        var client = ClientAs(roles);
        client.DefaultRequestHeaders.Add(TestAuthHandler.NameHeader, actorName);
        return client;
    }

    /// <summary>An unauthenticated <see cref="HttpClient"/> (no role header).</summary>
    public HttpClient AnonymousClient() => CreateClient();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("ConnectionStrings:CtmsDatabase", _connectionString);
        builder.UseSetting("Mongo:Database", DatabaseName);
        builder.UseSetting("ConnectionStrings:Redis", string.Empty);
        builder.UseSetting("Seed:Enabled", "false");
        builder.UseSetting("Auth:Enabled", "false");
        builder.UseSetting("Auth:PublicBundleReads", "true");

        // The global rate limiter is off by default for the suite so per-test request volume
        // never trips it; RateLimitingTests opts back in through settingOverrides.
        builder.UseSetting("RateLimit:Enabled", "false");

        foreach (var (key, value) in _settingOverrides)
        {
            builder.UseSetting(key, value ?? string.Empty);
        }

        builder.ConfigureTestServices(services =>
        {
            // Register the test handler's scheme, then force every default-scheme slot to it in
            // a PostConfigure so it wins regardless of what Program's AddCtmsAuth (JwtBearer or
            // the dev-bypass scheme) set via Configure/PostConfigure. The real
            // AuthorizationPolicies then evaluate against the header-driven principal.
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
                options.DefaultSignInScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await new MongoClient(_connectionString).DropDatabaseAsync(DatabaseName);
        }
        catch
        {
            // best-effort: the server is torn down with the assembly anyway
        }

        await base.DisposeAsync();
    }
}
