using System.Text.Json;

namespace CTMS.Application.Translations.Import;

/// <summary>
/// Parses a translation file body into a flat, ordered list of <see cref="ParsedTranslationEntry"/>.
/// Two formats are understood:
/// <list type="bullet">
///   <item><c>json</c> — a flat <c>{ "key": "value" }</c> object, or a nested object flattened with
///   <c>.</c> between segments;</item>
///   <item><c>flat</c> — <c>key=value</c> lines; <c>#</c> comment lines and blank lines are ignored.</item>
/// </list>
/// A body that does not parse for its declared format raises <see cref="ImportFormatException"/>
/// (which the API surfaces as <c>400</c>), carrying the offending line number where one is known.
/// A later duplicate key overrides an earlier one; the first occurrence fixes ordering.
/// </summary>
public static class TranslationFileParser
{
    /// <summary>The format tokens <see cref="Parse"/> accepts (case-insensitive).</summary>
    public static IReadOnlyList<string> SupportedFormats { get; } = ["json", "flat"];

    public static IReadOnlyList<ParsedTranslationEntry> Parse(string? format, string? content)
    {
        var normalized = (format ?? string.Empty).Trim().ToLowerInvariant();
        var body = content ?? string.Empty;

        var pairs = normalized switch
        {
            "json" => ParseJson(body),
            "flat" => ParseFlat(body),
            _ => throw new ImportFormatException(
                $"Unknown import format '{format}'. Expected one of: {string.Join(", ", SupportedFormats)}."),
        };

        // De-duplicate on key, last value wins, first occurrence fixes order.
        var order = new List<string>();
        var byKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            if (byKey.TryAdd(key, value))
            {
                order.Add(key);
            }
            else
            {
                byKey[key] = value;
            }
        }

        return order.Select(k => new ParsedTranslationEntry(k, byKey[k])).ToList();
    }

    // ---- json ---------------------------------------------------------------------------------

    private static List<(string Key, string Value)> ParseJson(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            var line = ex.LineNumber is { } n ? (int)n + 1 : (int?)null;
            throw new ImportFormatException($"the content is not valid JSON ({ex.Message}).", line);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ImportFormatException("the root of a JSON translation file must be an object.");
            }

            var result = new List<(string, string)>();
            Flatten(document.RootElement, prefix: null, result);
            return result;
        }
    }

    private static void Flatten(JsonElement element, string? prefix, List<(string, string)> into)
    {
        foreach (var property in element.EnumerateObject())
        {
            var path = prefix is null ? property.Name : $"{prefix}.{property.Name}";
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    Flatten(property.Value, path, into);
                    break;
                case JsonValueKind.String:
                    into.Add((path, property.Value.GetString() ?? string.Empty));
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    into.Add((path, property.Value.GetRawText()));
                    break;
                case JsonValueKind.Null:
                    into.Add((path, string.Empty));
                    break;
                case JsonValueKind.Array:
                    throw new ImportFormatException($"the value of '{path}' is an array, which is not supported.");
                default:
                    throw new ImportFormatException($"the value of '{path}' has an unsupported JSON type.");
            }
        }
    }

    // ---- flat (key=value) --------------------------------------------------------------------

    private static List<(string Key, string Value)> ParseFlat(string body)
    {
        var result = new List<(string, string)>();
        var lines = body.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].TrimEnd('\r');
            var trimmed = raw.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            var eq = raw.IndexOf('=');
            if (eq < 0)
            {
                throw new ImportFormatException("expected 'key=value'.", i + 1);
            }

            var key = raw[..eq].Trim();
            if (key.Length == 0)
            {
                throw new ImportFormatException("the key is empty.", i + 1);
            }

            // Trim surrounding whitespace from the value (the common `key = value` convention).
            result.Add((key, raw[(eq + 1)..].Trim()));
        }

        return result;
    }
}
