using System.Security.Claims;
using CTMS.Api.Auth;

namespace CTMS.Application.Tests;

/// <summary>
/// Coverage for <see cref="TokenActor.Resolve"/> — how the write "actor"
/// (<c>updatedBy</c> / <c>reviewedBy</c> / <c>publishedBy</c>) is chosen. A real bearer token
/// overrides the request body; the dev-bypass principal and anonymous callers keep the body value.
/// </summary>
public sealed class TokenActorTests
{
    private const string Fallback = "system";

    [Fact]
    public void Real_token_uses_the_name_claim_and_ignores_the_body()
    {
        var user = RealToken(("name", "Ada Lovelace"), ("preferred_username", "ada@contoso.com"));

        Assert.Equal("Ada Lovelace", TokenActor.Resolve(user, bodyValue: "spoofed", Fallback));
    }

    [Fact]
    public void Real_token_falls_back_to_preferred_username_then_oid()
    {
        var byUpn = RealToken(("preferred_username", "grace@contoso.com"));
        Assert.Equal("grace@contoso.com", TokenActor.Resolve(byUpn, bodyValue: null, Fallback));

        var byOid = RealToken(("oid", "00000000-0000-0000-0000-000000000042"));
        Assert.Equal("00000000-0000-0000-0000-000000000042", TokenActor.Resolve(byOid, bodyValue: "x", Fallback));
    }

    [Fact]
    public void Dev_bypass_principal_keeps_the_body_value()
    {
        var user = DevBypass();

        Assert.Equal("alice", TokenActor.Resolve(user, bodyValue: "alice", Fallback));
    }

    [Fact]
    public void Dev_bypass_principal_with_no_body_value_uses_the_fallback()
    {
        var user = DevBypass();

        Assert.Equal(Fallback, TokenActor.Resolve(user, bodyValue: "   ", Fallback));
    }

    [Fact]
    public void Anonymous_caller_keeps_the_body_value_or_the_fallback()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Equal("bob", TokenActor.Resolve(anonymous, bodyValue: "bob", Fallback));
        Assert.Equal(Fallback, TokenActor.Resolve(anonymous, bodyValue: null, Fallback));
    }

    [Fact]
    public void Null_principal_keeps_the_body_value()
        => Assert.Equal("carol", TokenActor.Resolve(user: null, bodyValue: "carol", Fallback));

    private static ClaimsPrincipal RealToken(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "Bearer");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal DevBypass()
    {
        var identity = new ClaimsIdentity(
            [new Claim("name", DevBypassAuthHandler.SyntheticUserName)],
            DevBypassAuthHandler.AuthenticationType);
        return new ClaimsPrincipal(identity);
    }
}
