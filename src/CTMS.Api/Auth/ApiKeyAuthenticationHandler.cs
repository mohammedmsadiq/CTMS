using System.Security.Claims;
using System.Text.Encodings.Web;
using CTMS.Application.ApiKeys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CTMS.Api.Auth;

/// <summary>
/// Authenticates a machine client from the <c>X-Api-Key</c> request header. The raw key is
/// hashed (Base64 SHA-256) and looked up; a match that is <see cref="Domain.ApiKeys.ApiKey.Active"/>
/// yields a principal holding the <b>single</b> role <see cref="AuthRoles.Reader"/> — an API key
/// can only ever read. <see cref="AuthenticationType"/> is distinct from the JWT and dev-bypass
/// identities so <see cref="TokenActor"/> treats it as a real (non-personal) token.
/// <para>
/// No header, an unknown hash, or an inactive key returns <see cref="AuthenticateResult.NoResult"/>
/// (not <c>Fail</c>) so a bearer token on the same request still gets its chance.
/// </para>
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    /// <summary>Request header carrying the raw key (<c>ctms_...</c>).</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>The identity's <c>AuthenticationType</c> — deliberately not the JWT or dev-bypass one.</summary>
    public const string AuthenticationType = "CtmsApiKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var rawHeader))
        {
            return AuthenticateResult.NoResult();
        }

        var rawKey = rawHeader.ToString().Trim();
        if (string.IsNullOrEmpty(rawKey))
        {
            return AuthenticateResult.NoResult();
        }

        var repository = Context.RequestServices.GetRequiredService<IApiKeyRepository>();

        var hash = ApiKeySecret.Hash(rawKey);
        var apiKey = await repository.FindByHashAsync(hash, Context.RequestAborted);
        if (apiKey is null || !apiKey.Active)
        {
            return AuthenticateResult.NoResult();
        }

        // Best-effort "last used" stamp — never block the request or fail it on a write error.
        var id = apiKey.Id;
        var scopeFactory = Context.RequestServices.GetRequiredService<IServiceScopeFactory>();
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider
                    .GetRequiredService<IApiKeyRepository>()
                    .TouchAsync(id, DateTime.UtcNow, CancellationToken.None);
            }
            catch
            {
                // swallow — LastUsedAt is advisory
            }
        });

        var identity = new ClaimsIdentity(AuthenticationType);
        identity.AddClaim(new Claim(ClaimTypes.Name, apiKey.Name));
        identity.AddClaim(new Claim("name", apiKey.Name));
        identity.AddClaim(new Claim(ClaimTypes.Role, AuthRoles.Reader));

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
