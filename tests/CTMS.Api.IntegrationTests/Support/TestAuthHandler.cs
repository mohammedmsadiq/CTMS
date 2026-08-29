using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CTMS.Api.IntegrationTests.Support;

/// <summary>
/// Test authentication handler registered as the default scheme by <see cref="CtmsApiFactory"/>
/// (replacing the API's dev-bypass all-roles handler). It reads a comma/space separated role
/// list from the <c>X-Test-Roles</c> request header and an optional actor name from
/// <c>X-Test-Name</c>, and builds a <see cref="ClaimsPrincipal"/>:
/// <list type="bullet">
///   <item>one <see cref="ClaimTypes.Role"/> claim per role (what <c>RequireRole</c> checks) and
///   a mirrored <c>roles</c> claim (the Entra shape);</item>
///   <item>a <see cref="ClaimTypes.Name"/> claim plus a <c>name</c> claim carrying the actor.</item>
/// </list>
/// No <c>X-Test-Roles</c> header ⇒ <see cref="AuthenticateResult.NoResult"/>, i.e. an anonymous
/// request: protected endpoints answer <c>401</c>, <c>AllowAnonymous</c> ones still run.
/// The identity's <c>AuthenticationType</c> is deliberately not the dev-bypass one, so
/// <c>TokenActor</c> treats it as a real token and takes the actor from <c>name</c>.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "IntegrationTest";
    public const string RolesHeader = "X-Test-Roles";
    public const string NameHeader = "X-Test-Name";

    private static readonly char[] RoleSeparators = [',', ' '];

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RolesHeader, out var rawRoles))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var roles = rawRoles.ToString()
            .Split(RoleSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var name = Request.Headers.TryGetValue(NameHeader, out var rawName)
            && !string.IsNullOrWhiteSpace(rawName.ToString())
            ? rawName.ToString()
            : "test-user";

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, name),
            new("name", name),
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("roles", role));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
