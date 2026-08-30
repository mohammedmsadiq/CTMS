using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

namespace CTMS.Api.Auth;

/// <summary>
/// Wires authentication + authorization for the API. Two modes, selected by the
/// <c>Auth:Enabled</c> configuration flag (default <c>true</c>):
/// <list type="bullet">
///   <item><b>enabled</b> — validate Entra ID JWT bearer tokens (<c>AzureAd</c> section) as the
///   default scheme. Every CTMS policy requires one of the <see cref="AuthRoles"/>.</item>
///   <item><b>disabled</b> — register <see cref="DevBypassAuthHandler"/> so local runs and the
///   test suite work with no IdP. Refused outright under <c>Production</c>. The bypass principal
///   already holds every role.</item>
/// </list>
/// </summary>
public static class AuthenticationSetup
{
    public const string AuthEnabledKey = "Auth:Enabled";
    public const string PublicBundleReadsKey = "Auth:PublicBundleReads";

    public static bool AuthEnabled(this IConfiguration configuration) =>
        configuration.GetValue(AuthEnabledKey, true);

    /// <summary>Latest bundle GET routes are anonymous unless this is explicitly <c>false</c>.</summary>
    public static bool PublicBundleReads(this IConfiguration configuration) =>
        configuration.GetValue(PublicBundleReadsKey, true);

    public static WebApplicationBuilder AddCtmsAuth(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var authEnabled = configuration.AuthEnabled();

        if (!authEnabled && builder.Environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Auth:Enabled=false is not permitted when ASPNETCORE_ENVIRONMENT=Production. " +
                "Remove the override or configure the AzureAd section.");
        }

        if (authEnabled)
        {
            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));
        }
        else
        {
            builder.Services
                .AddAuthentication(DevBypassAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DevBypassAuthHandler>(
                    DevBypassAuthHandler.SchemeName, _ => { });
        }

        builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
        return builder;
    }

    /// <summary>Emits the loud "auth is OFF" warning once the logger is available.</summary>
    public static void WarnIfAuthDisabled(this WebApplication app)
    {
        if (app.Configuration.AuthEnabled())
        {
            return;
        }

        app.Logger.LogWarning(
            "================ AUTH IS DISABLED (Auth:Enabled=false) ================\n" +
            "Every request is authenticated as the synthetic '{User}' principal holding ALL " +
            "roles. This is for local development and tests only and is refused under " +
            "Production. Do not run a shared or internet-facing instance in this mode.",
            DevBypassAuthHandler.SyntheticUserName);
    }
}
