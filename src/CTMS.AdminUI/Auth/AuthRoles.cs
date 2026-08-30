namespace CTMS.AdminUI.Auth;

/// <summary>
/// The Entra ID application roles CTMS recognises — the exact strings in the token's
/// <c>roles</c> claim. Mirror of <c>CTMS.Api/Auth/AuthRoles.cs</c> (the UI cannot reference the
/// API assembly); change both together.
/// </summary>
public static class AuthRoles
{
    public const string Admin = "TranslationAdministrator";
    public const string Manager = "TranslationManager";
    public const string Reviewer = "TranslationReviewer";
    public const string Translator = "Translator";
    public const string Reader = "TranslationReadOnly";

    public static readonly string[] All = [Admin, Manager, Reviewer, Translator, Reader];
}
