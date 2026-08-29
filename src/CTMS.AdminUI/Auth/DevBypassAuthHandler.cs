using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CTMS.AdminUI.Auth;

/// <summary>
/// Local-development escape hatch for the Admin UI. When <c>Auth:Enabled</c> is <c>false</c>
/// this scheme replaces OpenID Connect and signs every request in as a synthetic user holding
/// all <see cref="AuthRoles.All"/> roles, so the UI is usable without Entra ID. Never wired up
/// under <c>Production</c> (startup throws first).
/// </summary>
public sealed class DevBypassAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevBypass";
    public const string AuthenticationType = "CtmsDevBypass";
    public const string SyntheticUserName = "dev-bypass";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(AuthenticationType, "name", ClaimTypes.Role);
        identity.AddClaim(new Claim("name", SyntheticUserName));
        identity.AddClaim(new Claim("preferred_username", SyntheticUserName));
        foreach (var role in AuthRoles.All)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
