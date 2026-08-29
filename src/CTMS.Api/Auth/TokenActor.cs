using System.Security.Claims;

namespace CTMS.Api.Auth;

/// <summary>
/// Resolves the "actor" recorded on a write (<c>updatedBy</c> / <c>reviewedBy</c> /
/// <c>publishedBy</c>). When the caller is authenticated with a real bearer token the actor is
/// taken from the token and any client-supplied value in the request body is ignored. When auth
/// is disabled (the <see cref="DevBypassAuthHandler"/> principal) or the request is anonymous,
/// the request-body value is honoured, falling back to <paramref name="fallback"/>.
/// </summary>
public static class TokenActor
{
    /// <param name="user">The request principal (<c>HttpContext.User</c>).</param>
    /// <param name="bodyValue">The actor field from the request body, if any.</param>
    /// <param name="fallback">Value to use when neither a token nor a body value is available.</param>
    public static string Resolve(ClaimsPrincipal? user, string? bodyValue, string fallback)
    {
        if (IsRealToken(user))
        {
            return FromToken(user!) ?? fallback;
        }

        return string.IsNullOrWhiteSpace(bodyValue) ? fallback : bodyValue.Trim();
    }

    private static bool IsRealToken(ClaimsPrincipal? user) =>
        user?.Identity is { IsAuthenticated: true } identity
        && identity.AuthenticationType != DevBypassAuthHandler.AuthenticationType;

    /// <summary>
    /// Token display identity, best first: <c>name</c>, then <c>preferred_username</c>, then the
    /// object id (<c>oid</c> / the <c>objectidentifier</c> claim).
    /// </summary>
    private static string? FromToken(ClaimsPrincipal user)
    {
        string? Claim(params string[] types) => types
            .Select(t => user.FindFirst(t)?.Value)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        return Claim("name", ClaimTypes.Name)
            ?? Claim("preferred_username", "upn", ClaimTypes.Upn, ClaimTypes.Email)
            ?? Claim("oid", "http://schemas.microsoft.com/identity/claims/objectidentifier", ClaimTypes.NameIdentifier);
    }
}
