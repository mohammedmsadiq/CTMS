namespace CTMS.Api.Infrastructure;

/// <summary>
/// The single CORS policy for the API. Cross-origin callers are the browser-based SDK / CDN
/// delivery path (WS6) and any external tooling; the server-side Blazor Admin UI is same-origin
/// from the browser's point of view and needs nothing here.
/// </summary>
/// <remarks>
/// Origins come from <c>Cors:AllowedOrigins</c> (a string array). When it is empty or absent the
/// policy allows <b>no</b> cross-origin request — the safe default for a fresh deployment.
/// When origins are configured the policy allows them with any header and method, permits
/// credentials, and exposes <c>ETag</c> and <c>Location</c> so a browser client can read the
/// bundle entity tag and the created-resource location.
/// </remarks>
internal static class CorsSetup
{
    public const string PolicyName = "ctms";

    public const string AllowedOriginsKey = "Cors:AllowedOrigins";

    public static IServiceCollection AddCtmsCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection(AllowedOriginsKey).Get<string[]>() ?? [];

        return services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            if (origins.Length == 0)
            {
                // No cross-origin access. CORS middleware still runs but never emits
                // Access-Control-Allow-Origin, so browsers block every cross-site call.
                policy.SetIsOriginAllowed(_ => false);
                return;
            }

            policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .WithExposedHeaders("ETag", "Location");
        }));
    }
}
