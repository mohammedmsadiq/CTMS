using System.Security.Claims;
using CTMS.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace CTMS.Application.Tests;

/// <summary>
/// Table-driven coverage of the CTMS role → policy matrix (<see cref="AuthorizationPolicies"/>).
/// Exercises the real authorization runtime built from <see cref="AuthorizationPolicies.Configure"/>
/// — the same call both hosts make — rather than a <c>WebApplicationFactory</c>: the mapping is
/// the whole contract, and every endpoint just names one of these policies. Endpoint-level
/// wiring (which verb gets which policy) is asserted by reading the endpoint source in review;
/// standing up the Mongo-backed host for a role header check is out of proportion (same call
/// made by <c>BundleConditionalRequestTests</c>).
/// </summary>
public sealed class AuthorizationPoliciesTests
{
    private static readonly IAuthorizationService Authorization = BuildAuthorizationService();

    public static TheoryData<string, string> EveryRoleAndPolicy()
    {
        var data = new TheoryData<string, string>();
        foreach (var role in AuthRoles.All)
        {
            foreach (var policy in AuthorizationPolicies.RolesByPolicy.Keys)
            {
                data.Add(role, policy);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryRoleAndPolicy))]
    public async Task Role_satisfies_exactly_the_policies_it_is_mapped_to(string role, string policy)
    {
        var expected = AuthorizationPolicies.RolesByPolicy[policy].Contains(role);

        var result = await Authorization.AuthorizeAsync(PrincipalWithRoles(role), resource: null, policy);

        Assert.Equal(expected, result.Succeeded);
    }

    [Theory]
    [InlineData(AuthorizationPolicies.CanRead)]
    [InlineData(AuthorizationPolicies.CanEditStrings)]
    [InlineData(AuthorizationPolicies.CanReview)]
    [InlineData(AuthorizationPolicies.CanManageContent)]
    [InlineData(AuthorizationPolicies.CanPublish)]
    [InlineData(AuthorizationPolicies.CanAdminProjects)]
    public async Task Authenticated_user_with_no_recognised_role_is_denied_every_policy(string policy)
    {
        var noRole = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"));

        var result = await Authorization.AuthorizeAsync(noRole, resource: null, policy);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Anonymous_principal_is_denied_every_policy()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        foreach (var policy in AuthorizationPolicies.RolesByPolicy.Keys)
        {
            var result = await Authorization.AuthorizeAsync(anonymous, resource: null, policy);
            Assert.False(result.Succeeded);
        }
    }

    [Fact]
    public void Admin_satisfies_every_policy()
    {
        Assert.All(
            AuthorizationPolicies.RolesByPolicy.Values,
            roles => Assert.Contains(AuthRoles.Admin, roles));
    }

    [Fact]
    public void Reader_satisfies_only_CanRead()
    {
        var readerPolicies = AuthorizationPolicies.RolesByPolicy
            .Where(kv => kv.Value.Contains(AuthRoles.Reader))
            .Select(kv => kv.Key);

        Assert.Equal([AuthorizationPolicies.CanRead], readerPolicies);
    }

    private static ClaimsPrincipal PrincipalWithRoles(params string[] roles)
    {
        var identity = new ClaimsIdentity(authenticationType: "test");
        identity.AddClaims(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(identity);
    }

    private static IAuthorizationService BuildAuthorizationService() =>
        new ServiceCollection()
            .AddLogging()
            .AddAuthorization(AuthorizationPolicies.Configure)
            .BuildServiceProvider()
            .GetRequiredService<IAuthorizationService>();
}
