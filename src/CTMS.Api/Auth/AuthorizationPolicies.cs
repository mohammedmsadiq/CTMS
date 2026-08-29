using Microsoft.AspNetCore.Authorization;

namespace CTMS.Api.Auth;

/// <summary>
/// The single place where CTMS roles are mapped to authorization policies. Endpoints reference
/// the policy name constants (never a raw role check). <see cref="CTMS.AdminUI"/> keeps a
/// byte-identical copy of this file (it cannot reference this assembly) — change both together.
/// </summary>
/// <remarks>
/// <para>Policy → roles that satisfy it:</para>
/// <list type="table">
///   <item><term><see cref="CanRead"/></term><description>admin, manager, reviewer, translator, reader</description></item>
///   <item><term><see cref="CanEditStrings"/></term><description>admin, manager, reviewer, translator</description></item>
///   <item><term><see cref="CanReview"/></term><description>admin, manager, reviewer</description></item>
///   <item><term><see cref="CanManageContent"/></term><description>admin, manager</description></item>
///   <item><term><see cref="CanPublish"/></term><description>admin, manager</description></item>
///   <item><term><see cref="CanAdminProjects"/></term><description>admin</description></item>
/// </list>
/// </remarks>
public static class AuthorizationPolicies
{
    /// <summary>Any recognised role. Guards every GET.</summary>
    public const string CanRead = "CanRead";

    /// <summary>Create/edit translation string values (the string upsert).</summary>
    public const string CanEditStrings = "CanEditStrings";

    /// <summary>Review transitions — submit/approve/reject/reopen and the <c>publish</c> review action.</summary>
    public const string CanReview = "CanReview";

    /// <summary>Manage locales and translation keys (create/update/delete).</summary>
    public const string CanManageContent = "CanManageContent";

    /// <summary>Publish a translation bundle (<c>POST .../bundles/{localeCode}</c>).</summary>
    public const string CanPublish = "CanPublish";

    /// <summary>Create or delete projects.</summary>
    public const string CanAdminProjects = "CanAdminProjects";

    /// <summary>
    /// Policy name → the roles that satisfy it. Exposed so tests can assert the matrix
    /// table-driven and so both hosts build the same policy set from one source.
    /// </summary>
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

    /// <summary>Registers every CTMS policy. Pass to <c>AddAuthorization(...)</c>.</summary>
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
