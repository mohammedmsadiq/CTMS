using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace CTMS.Application.Translations.Import;

/// <summary>
/// Parses a translation file body into a flat, ordered list of <see cref="ParsedTranslationEntry"/>.
/// Four formats are understood:
/// <list type="bullet">
///   <item><c>json</c> — a flat <c>{ "key": "value" }</c> object, or a nested object flattened with
///   <c>.</c> between segments;</item>
///   <item><c>flat</c> — <c>key=value</c> lines; <c>#</c> comment lines and blank lines are ignored;</item>
///   <item><c>csv</c> — a header row naming (at least) <c>key</c> and <c>value</c> columns, then one
///   record per row (RFC 4180 quoting);</item>
///   <item><c>resx</c> — the <c>&lt;data name="…"&gt;&lt;value&gt;…&lt;/value&gt;&lt;/data&gt;</c>
///   elements; comments, <c>&lt;resheader&gt;</c>, <c>xml:space</c> and typed resources are ignored.</item>
/// </list>
/// A body that does not parse for its declared format raises <see cref="ImportFormatException"/>
/// (which the API surfaces as <c>400</c>), carrying the offending line number where one is known.
/// A later duplicate key overrides an earlier one; the first occurrence fixes ordering.
/// </summary>
public static class TranslationFileParser
{
    /// <summary>The format tokens <see cref="Parse"/> accepts (case-insensitive).</summary>
    public static IReadOnlyList<string> SupportedFormats { get; } = ["json", "flat", "csv", "resx"];

    public static IReadOnlyList<ParsedTranslationEntry> Parse(string? format, string? content)
    {
        var normalized = (format ?? string.Empty).Trim().ToLowerInvariant();
        var body = content ?? string.Empty;

        var pairs = normalized switch
        {
            "json" => ParseJson(body),
            "flat" => ParseFlat(body),
            "csv" => ParseCsv(body),
            "resx" => ParseResx(body),
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

    // ---- csv ---------------------------------------------------------------------------------

    private static List<(string Key, string Value)> ParseCsv(string body)
    {
        var rows = CsvReader.ReadAll(body);
        if (rows.Count == 0)
        {
            return [];
        }

        var header = rows[0];
        var keyIndex = header.FindIndex(h => h.Trim().Equals("key", StringComparison.OrdinalIgnoreCase));
        var valueIndex = header.FindIndex(h => h.Trim().Equals("value", StringComparison.OrdinalIgnoreCase));
        if (keyIndex < 0 || valueIndex < 0)
        {
            throw new ImportFormatException("the header row must name a 'key' column and a 'value' column.", 1);
        }

        var result = new List<(string, string)>();
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 1 && row[0].Length == 0)
            {
                continue; // blank line
            }

            if (row.Count <= keyIndex || row.Count <= valueIndex)
            {
                throw new ImportFormatException("too few columns for the header.", i + 1);
            }

            var key = row[keyIndex].Trim();
            if (key.Length == 0)
            {
                throw new ImportFormatException("the key column is empty.", i + 1);
            }

            result.Add((key, row[valueIndex]));
        }

        return result;
    }

    // ---- resx ------------------------------------------------------------------------------

    private static List<(string Key, string Value)> ParseResx(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(body, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new ImportFormatException(
                $"the content is not well-formed XML ({ex.Message}).",
                ex.LineNumber > 0 ? ex.LineNumber : null);
        }

        var result = new List<(string, string)>();
        foreach (var data in document.Descendants("data"))
        {
            var name = (string?)data.Attribute("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            // Skip typed / file-backed resources — only plain string data carries a translation.
            if (data.Attribute("type") is not null || data.Attribute("mimetype") is not null)
            {
                continue;
            }

            var valueElement = data.Element("value");
            if (valueElement is null)
            {
                continue;
            }

            result.Add((name.Trim(), valueElement.Value));
        }

        return result;
    }

    /// <summary>Minimal RFC 4180 CSV reader: quoted fields, <c>""</c> escapes, embedded newlines.</summary>
    private static class CsvReader
    {
        public static List<List<string>> ReadAll(string text)
        {
            var rows = new List<List<string>>();
            var field = new StringBuilder();
            var row = new List<string>();
            var inQuotes = false;
            var fieldStarted = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }

                    continue;
                }

                switch (c)
                {
                    case '"' when field.Length == 0 && !fieldStarted:
                        inQuotes = true;
                        fieldStarted = true;
                        break;
                    case ',':
                        row.Add(field.ToString());
                        field.Clear();
                        fieldStarted = false;
                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add(field.ToString());
                        field.Clear();
                        fieldStarted = false;
                        rows.Add(row);
                        row = [];
                        break;
                    default:
                        field.Append(c);
                        fieldStarted = true;
                        break;
                }
            }

            if (inQuotes)
            {
                throw new ImportFormatException("a quoted CSV field is not closed.");
            }

            if (field.Length > 0 || fieldStarted || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            return rows;
        }
    }
}
