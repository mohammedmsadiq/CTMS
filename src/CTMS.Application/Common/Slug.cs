using System.Text;

namespace CTMS.Application.Common;

/// <summary>Derives URL-safe slugs from free text.</summary>
public static class Slug
{
    /// <summary>
    /// Lowercases <paramref name="value"/>, keeps letters and digits, and collapses every
    /// other run of characters into a single hyphen. Returns an empty string when nothing
    /// usable remains.
    /// </summary>
    public static string From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        var pendingSeparator = false;

        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(ch);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString();
    }
}
