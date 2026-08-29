using Microsoft.AspNetCore.Authorization;

namespace CTMS.AdminUI.Auth;

/// <summary>
/// CTMS role → policy mapping. Mirror of <c>CTMS.Api/Auth/AuthorizationPolicies.cs</c> — the
/// same policy names, the same role sets — so <c>&lt;AuthorizeView Policy="..."&gt;</c> in the
/// UI gates affordances exactly as the API gates the matching endpoint. Change both together.
/// </summary>
public static class AuthorizationPolicies
{
    public const string CanRead = "CanRead";
    public const string CanEditStrings = "CanEditStrings";
    public const string CanReview = "CanReview";
    public const string CanManageContent = "CanManageContent";
    public const string CanPublish = "CanPublish";
    public const string CanAdminProjects = "CanAdminProjects";

    public static readonly IReadOnlyDictionary<string, string[]> RolesByPolicy =
        new Dictionary<string, string[]>
        {
            [CanRead] = [AuthRoles.Admin, AuthRoles.Manager, AuthRoles.Reviewer, AuthRoles.Translator, AuthRoles.Reader],
            [CanEditStrings] = [AuthRoles.Admin, AuthRoles.Manager, AuthRoles.Reviewer, AuthRoles.Translator],
            [CanReview] = [AuthRoles.Admin, AuthRoles.Manager, AuthRoles.Reviewer],
            [CanManageContent] = [AuthRoles.Admin, AuthRoles.Manager],
            [CanPublish] = [AuthRoles.Admin, AuthRoles.Manager],
            [CanAdminProjects] = [AuthRoles.Admin],
        };

    public static void Configure(AuthorizationOptions options)
    {
        foreach (var (policy, roles) in RolesByPolicy)
        {
            options.AddPolicy(policy, builder => builder
                .RequireAuthenticatedUser()
                .RequireRole(roles));
        }
    }
}
