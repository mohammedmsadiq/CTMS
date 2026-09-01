using System.Text;

namespace CTMS.Application.Translations;

/// <summary>
/// A minimal, dependency-free RFC 4180 CSV reader/writer. Fields are comma-separated; a field is
/// quoted with <c>"</c> when it contains a comma, quote, CR or LF, and an embedded quote is
/// doubled. Records are separated by CRLF or LF (a bare CR is also tolerated on read). A leading
/// UTF-8 BOM is ignored on read.
/// </summary>
internal static class Rfc4180
{
    /// <summary>One parsed record: its fields and the 1-based physical line it started on.</summary>
    public readonly record struct Record(IReadOnlyList<string> Fields, int Line);

    /// <summary>
    /// Parses <paramref name="text"/> into records. A completely empty trailing line is not
    /// returned as a record. Throws <see cref="FormatException"/> for an unterminated quoted field.
    /// </summary>
    public static IReadOnlyList<Record> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Strip a leading UTF-8 BOM if present.
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var records = new List<Record>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordStartLine = 1;
        var sawAnyChar = false;   // record carried content (distinguishes a blank line)
        var atFieldStart = true;  // the current field has had no characters yet

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
            atFieldStart = true;
        }

        void EndRecord()
        {
            EndField();
            // Ignore a blank line (a single empty field and nothing else).
            if (!(fields.Count == 1 && fields[0].Length == 0))
            {
                records.Add(new Record(fields.ToArray(), recordStartLine));
            }

            fields = [];
            sawAnyChar = false;
            recordStartLine = line;
        }

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
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when atFieldStart:
                    inQuotes = true;
                    atFieldStart = false;
                    sawAnyChar = true;
                    break;
                case '"':
                    // A quote in the middle of an unquoted field — keep it verbatim.
                    field.Append('"');
                    break;
                case ',':
                    EndField();
                    sawAnyChar = true;
                    break;
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    line++;
                    EndRecord();
                    break;
                case '\n':
                    line++;
                    EndRecord();
                    break;
                default:
                    field.Append(c);
                    atFieldStart = false;
                    sawAnyChar = true;
                    break;
            }
        }

        if (inQuotes)
        {
            throw new FormatException($"Line {recordStartLine}: a quoted field is not closed before the end of the file.");
        }

        // Flush a final record with no trailing newline.
        if (field.Length > 0 || fields.Count > 0 || sawAnyChar)
        {
            EndRecord();
        }

        return records;
    }

    /// <summary>Renders <paramref name="rows"/> as an RFC 4180 document with CRLF line endings.</summary>
    public static string Write(IEnumerable<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(QuoteField(row[i]));
            }

            sb.Append("\r\n");
        }

        return sb.ToString();
    }

    private static string QuoteField(string? value)
    {
        var s = value ?? string.Empty;
        var mustQuote = s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r');
        if (!mustQuote)
        {
            return s;
        }

        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
