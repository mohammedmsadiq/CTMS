using System.Text.Json;
using System.Text.Json.Serialization;

namespace CTMS.Client.Internal;

/// <summary>Shared System.Text.Json settings for wire payloads and the file cache.</summary>
internal static class CtmsJson
{
    /// <summary>
    /// Case-insensitive so the API's camelCase (<c>fallbackCode</c>, <c>enabledLanguageCodes</c>)
    /// binds to PascalCase members; ignores nulls on write to keep cache files small.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
