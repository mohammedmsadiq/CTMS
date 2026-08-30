namespace CTMS.Api.Auth;

/// <summary>
/// The Entra ID application roles CTMS recognises. These are the exact strings that appear in
/// the <c>roles</c> claim of an access token (Entra app-role <c>value</c>). An authenticated
/// principal that carries none of these satisfies no policy and is rejected with <c>403</c>
/// on every <c>/api/*</c> endpoint.
/// </summary>
public static class AuthRoles
{
    /// <summary>Everything, including creating projects.</summary>
    public const string Admin = "TranslationAdministrator";

    /// <summary>Manage languages and keys, publish, plus all reviewer/translator rights.</summary>
    public const string Manager = "TranslationManager";

    /// <summary>Run review transitions (approve/reject/reopen/publish action), edit strings, read.</summary>
    public const string Reviewer = "TranslationReviewer";

    /// <summary>Create/edit translation string values, submit for review, read.</summary>
    public const string Translator = "Translator";

    /// <summary>Read-only: every GET.</summary>
    public const string Reader = "TranslationReadOnly";

    /// <summary>All recognised roles — used by the local-dev bypass principal.</summary>
    public static readonly string[] All = [Admin, Manager, Reviewer, Translator, Reader];
}
