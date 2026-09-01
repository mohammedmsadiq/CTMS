using System.Text.Json;
using ClosedXML.Excel;

namespace CTMS.Application.Translations.Import;

/// <summary>
/// Parses a translation file body into an ordered list of <see cref="ParsedTranslationEntry"/>.
/// Formats understood:
/// <list type="bullet">
///   <item><c>json</c> — a flat <c>{ "key": "value" }</c> object, or a nested object flattened with
///   <c>.</c> between segments;</item>
///   <item><c>flat</c> — <c>key=value</c> lines; <c>#</c> comment lines and blank lines are ignored;</item>
///   <item><c>csv</c> — RFC 4180. The header row decides the shape: a <c>key</c> + <c>value</c>
///   pair is <em>narrow</em> (each row targets the request's <c>language</c>); a <c>key</c> column
///   plus one or more columns whose header is a known language code is <em>wide</em> (each such
///   column imports that language and the request's <c>language</c> is ignored). Optional
///   <c>category</c> / <c>description</c> columns seed a newly-created key; other columns are
///   ignored;</item>
///   <item><c>xlsx</c> — the first worksheet of an OpenXML workbook, same header logic as
///   <c>csv</c>. Supplied as base64 in the request's <c>contentBase64</c> field.</item>
/// </list>
/// A body that does not parse raises <see cref="ImportFormatException"/> (surfaced as <c>400</c>),
/// carrying the offending line/row where known. A later duplicate <c>(key, language)</c> overrides
/// an earlier one; the first occurrence fixes ordering.
/// </summary>
public static class TranslationFileParser
{
    /// <summary>The format tokens <see cref="Parse"/> accepts (case-insensitive).</summary>
    public static IReadOnlyList<string> SupportedFormats { get; } = ["json", "flat", "csv", "xlsx"];

    /// <summary>Narrow formats: the request's <c>language</c> is required and applies to every row.</summary>
    public static IReadOnlyList<string> NarrowFormats { get; } = ["json", "flat"];

    /// <param name="isKnownLanguage">
    /// Tells the <c>csv</c> / <c>xlsx</c> header logic whether a column header is a registered
    /// language code (and therefore a wide-format language column). Ignored for <c>json</c> /
    /// <c>flat</c>.
    /// </param>
    public static ParsedTranslationFile Parse(
        string? format,
        string? content,
        string? contentBase64 = null,
        Func<string, bool>? isKnownLanguage = null)
    {
        var normalized = (format ?? string.Empty).Trim().ToLowerInvariant();
        var body = content ?? string.Empty;
        var known = isKnownLanguage ?? (_ => false);

        return normalized switch
        {
            "json" => Narrow(ParseJson(body)),
            "flat" => Narrow(ParseFlat(body)),
            "csv" => Dedupe(ParseCsv(body, known)),
            "xlsx" => Dedupe(ParseXlsx(contentBase64, known)),
            _ => throw new ImportFormatException(
                $"Unknown import format '{format}'. Expected one of: {string.Join(", ", SupportedFormats)}."),
        };
    }

    // ---- shared shaping --------------------------------------------------------------------------

    private static ParsedTranslationFile Narrow(IEnumerable<ParsedTranslationEntry> entries)
        => Dedupe(new ParsedTranslationFile(IsWide: false, entries.ToList()));

    /// <summary>Collapses duplicate <c>(key, language)</c> pairs: last value wins, first fixes order.</summary>
    private static ParsedTranslationFile Dedupe(ParsedTranslationFile file)
    {
        var order = new List<(string Key, string Lang)>();
        var byPair = new Dictionary<(string Key, string Lang), ParsedTranslationEntry>();

        foreach (var entry in file.Entries)
        {
            var pair = (entry.Key, (entry.LanguageCode ?? string.Empty).ToLowerInvariant());
            if (byPair.TryAdd(pair, entry))
            {
                order.Add(pair);
            }
            else
            {
                byPair[pair] = entry;
            }
        }

        return file with { Entries = order.Select(p => byPair[p]).ToList() };
    }

    // ---- json ---------------------------------------------------------------------------------

    private static List<ParsedTranslationEntry> ParseJson(string body)
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

            var result = new List<ParsedTranslationEntry>();
            Flatten(document.RootElement, prefix: null, result);
            return result;
        }
    }

    private static void Flatten(JsonElement element, string? prefix, List<ParsedTranslationEntry> into)
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
                    into.Add(new ParsedTranslationEntry(path, property.Value.GetString() ?? string.Empty));
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    into.Add(new ParsedTranslationEntry(path, property.Value.GetRawText()));
                    break;
                case JsonValueKind.Null:
                    into.Add(new ParsedTranslationEntry(path, string.Empty));
                    break;
                case JsonValueKind.Array:
                    throw new ImportFormatException($"the value of '{path}' is an array, which is not supported.");
                default:
                    throw new ImportFormatException($"the value of '{path}' has an unsupported JSON type.");
            }
        }
    }

    // ---- flat (key=value) --------------------------------------------------------------------

    private static List<ParsedTranslationEntry> ParseFlat(string body)
    {
        var result = new List<ParsedTranslationEntry>();
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

            result.Add(new ParsedTranslationEntry(key, raw[(eq + 1)..].Trim(), Line: i + 1));
        }

        return result;
    }

    // ---- csv ---------------------------------------------------------------------------------

    private static ParsedTranslationFile ParseCsv(string body, Func<string, bool> isKnownLanguage)
    {
        IReadOnlyList<Rfc4180.Record> records;
        try
        {
            records = Rfc4180.Read(body);
        }
        catch (FormatException ex)
        {
            throw new ImportFormatException(ex.Message);
        }

        if (records.Count == 0)
        {
            return new ParsedTranslationFile(IsWide: false, []);
        }

        var header = records[0].Fields.Select(h => h.Trim()).ToList();
        var rows = records
            .Skip(1)
            .Select(r => (r.Line, Cells: (IReadOnlyList<string>)r.Fields))
            .ToList();

        return ParseTable(header, rows, isKnownLanguage);
    }

    // ---- xlsx ------------------------------------------------------------------------------

    private static ParsedTranslationFile ParseXlsx(string? contentBase64, Func<string, bool> isKnownLanguage)
    {
        var raw = (contentBase64 ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            throw new ImportFormatException(
                "no xlsx content; put the file bytes, base64-encoded, in the request's 'contentBase64' field.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(raw);
        }
        catch (FormatException)
        {
            throw new ImportFormatException("'contentBase64' is not valid base64.");
        }

        using var stream = new MemoryStream(bytes);
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not ImportFormatException)
        {
            throw new ImportFormatException("the content is not a valid .xlsx (OpenXML) workbook.");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new ImportFormatException("the workbook has no worksheets.");

            var used = sheet.RangeUsed();
            if (used is null)
            {
                return new ParsedTranslationFile(IsWide: false, []);
            }

            var firstRow = used.RangeAddress.FirstAddress.RowNumber;
            var lastRow = used.RangeAddress.LastAddress.RowNumber;
            var firstCol = used.RangeAddress.FirstAddress.ColumnNumber;
            var lastCol = used.RangeAddress.LastAddress.ColumnNumber;

            var header = new List<string>();
            for (var c = firstCol; c <= lastCol; c++)
            {
                header.Add(sheet.Cell(firstRow, c).GetString().Trim());
            }

            var rows = new List<(int Line, IReadOnlyList<string> Cells)>();
            for (var r = firstRow + 1; r <= lastRow; r++)
            {
                var cells = new List<string>();
                for (var c = firstCol; c <= lastCol; c++)
                {
                    cells.Add(sheet.Cell(r, c).GetString());
                }

                rows.Add((r, cells));
            }

            return ParseTable(header, rows, isKnownLanguage);
        }
    }

    // ---- shared table (csv + xlsx) header logic --------------------------------------------

    private static ParsedTranslationFile ParseTable(
        IReadOnlyList<string> header,
        IReadOnlyList<(int Line, IReadOnlyList<string> Cells)> rows,
        Func<string, bool> isKnownLanguage)
    {
        var keyIdx = IndexOf(header, "key");
        if (keyIdx < 0)
        {
            throw new ImportFormatException("a 'key' column is required.", 1);
        }

        var valueIdx = IndexOf(header, "value");
        var categoryIdx = IndexOf(header, "category");
        var descriptionIdx = IndexOf(header, "description");

        var languageColumns = new List<(int Index, string Code)>();
        for (var c = 0; c < header.Count; c++)
        {
            if (c == keyIdx || c == valueIdx || c == categoryIdx || c == descriptionIdx)
            {
                continue;
            }

            var name = header[c];
            if (name.Length > 0 && isKnownLanguage(name))
            {
                languageColumns.Add((c, name));
            }
        }

        var isWide = languageColumns.Count > 0;
        if (!isWide && valueIdx < 0)
        {
            throw new ImportFormatException(
                "the header needs a 'value' column (narrow) or at least one language-code column (wide).",
                1);
        }

        var entries = new List<ParsedTranslationEntry>();
        foreach (var (line, cells) in rows)
        {
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var key = Cell(cells, keyIdx).Trim();
            var category = NullIfBlank(Cell(cells, categoryIdx));
            var description = NullIfBlank(Cell(cells, descriptionIdx));

            if (!isWide)
            {
                entries.Add(new ParsedTranslationEntry(
                    key, Cell(cells, valueIdx), LanguageCode: null, category, description, line));
                continue;
            }

            if (key.Length == 0)
            {
                // One error per row, not one per language column.
                entries.Add(new ParsedTranslationEntry(string.Empty, string.Empty, null, category, description, line));
                continue;
            }

            foreach (var (index, code) in languageColumns)
            {
                var value = Cell(cells, index);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue; // a blank cell is a skip, never a delete
                }

                entries.Add(new ParsedTranslationEntry(key, value, code, category, description, line));
            }
        }

        return new ParsedTranslationFile(isWide, entries);
    }

    private static int IndexOf(IReadOnlyList<string> header, string name)
    {
        for (var i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Cell(IReadOnlyList<string> cells, int index)
        => index >= 0 && index < cells.Count ? cells[index] : string.Empty;

    private static string? NullIfBlank(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
