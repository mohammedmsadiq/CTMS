namespace CTMS.AdminUI.Auth;

/// <summary>
/// The Entra ID application roles CTMS recognises — the exact strings in the token's
/// <c>roles</c> claim. Mirror of <c>CTMS.Api/Auth/AuthRoles.cs</c> (the UI cannot reference the
/// API assembly); change both together.
/// </summary>
public static class AuthRoles
{
    public const string Admin = "ctms.admin";
    public const string Manager = "ctms.manager";
    public const string Reviewer = "ctms.reviewer";
    public const string Translator = "ctms.translator";
    public const string Reader = "ctms.reader";

    public static readonly string[] All = [Admin, Manager, Reviewer, Translator, Reader];
}
