using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CTMS.Api.Auth;

/// <summary>
/// Local-development / test escape hatch. When <c>Auth:Enabled</c> is <c>false</c> this scheme
/// is registered instead of JWT bearer and authenticates <b>every</b> request as a synthetic
/// principal that holds all <see cref="AuthRoles.All"/> roles, so <c>dotnet run</c> and the
/// test suite work without an identity provider. It is never wired up when
/// <c>ASPNETCORE_ENVIRONMENT=Production</c> (startup throws first).
/// </summary>
public sealed class DevBypassAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevBypass";

    /// <summary>The synthetic identity's <c>AuthenticationType</c>. <see cref="TokenActor"/> uses
    /// it to recognise the bypass principal and defer to the request-body actor field.</summary>
    public const string AuthenticationType = "CtmsDevBypass";

    public const string SyntheticUserName = "dev-bypass";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(AuthenticationType);
        identity.AddClaim(new Claim(ClaimTypes.Name, SyntheticUserName));
        identity.AddClaim(new Claim("name", SyntheticUserName));
        identity.AddClaim(new Claim("preferred_username", SyntheticUserName));
        foreach (var role in AuthRoles.All)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
