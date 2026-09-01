using System.Text;
using ClosedXML.Excel;
using CTMS.Application.Common;

namespace CTMS.Application.Translations.Export;

/// <summary>
/// Renders <see cref="TranslationExportData"/> to a downloadable <see cref="ExportedFile"/>. Pure:
/// no store, no HTTP. Two formats:
/// <list type="bullet">
///   <item><c>csv</c> — RFC 4180, CRLF line endings, a UTF-8 BOM so Excel reads accented text
///   correctly; media type <c>text/csv; charset=utf-8</c>;</item>
///   <item><c>xlsx</c> — one <c>Translations</c> worksheet, bold + frozen header row, frozen first
///   column, columns auto-sized; media type
///   <c>application/vnd.openxmlformats-officedocument.spreadsheetml.sheet</c>.</item>
/// </list>
/// The column order is always <c>key, category, description</c> then one column per language code.
/// </summary>
public static class TranslationExporter
{
    public const string CsvContentType = "text/csv; charset=utf-8";

    public const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string WorksheetName = "Translations";
    private static readonly string[] FixedColumns = ["key", "category", "description"];

    /// <summary>The format tokens <see cref="Export"/> accepts (case-insensitive).</summary>
    public static IReadOnlyList<string> SupportedFormats { get; } = ["csv", "xlsx"];

    /// <summary>
    /// Lower-cases and validates <paramref name="format"/>. Lets an endpoint reject a bad
    /// <c>?format=</c> with <c>400</c> before any store round-trip.
    /// </summary>
    /// <exception cref="ValidationException"><paramref name="format"/> is not <c>csv</c> or <c>xlsx</c>.</exception>
    public static string NormalizeFormat(string? format)
    {
        var normalized = (format ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "csv" or "xlsx"
            ? normalized
            : throw new ValidationException(
                $"Unknown export format '{format}'. Expected one of: {string.Join(", ", SupportedFormats)}.");
    }

    /// <exception cref="ValidationException"><paramref name="format"/> is not <c>csv</c> or <c>xlsx</c>.</exception>
    public static ExportedFile Export(string? format, TranslationExportData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return NormalizeFormat(format) switch
        {
            "csv" => new ExportedFile(
                $"{data.ProjectSlug}-translations.csv", CsvContentType, ToCsv(data)),
            "xlsx" => new ExportedFile(
                $"{data.ProjectSlug}-translations.xlsx", XlsxContentType, ToXlsx(data)),
            _ => throw new ValidationException(
                $"Unknown export format '{format}'. Expected one of: {string.Join(", ", SupportedFormats)}."),
        };
    }

    private static byte[] ToCsv(TranslationExportData data)
    {
        var header = new List<string?>(FixedColumns);
        header.AddRange(data.LanguageCodes);

        var rows = new List<IReadOnlyList<string?>> { header };

        foreach (var row in data.Rows)
        {
            var cells = new List<string?> { row.Key, row.Category, row.Description };
            foreach (var code in data.LanguageCodes)
            {
                cells.Add(row.Values.TryGetValue(code, out var value) ? value : null);
            }

            rows.Add(cells);
        }

        var body = Rfc4180.Write(rows);
        var bom = Encoding.UTF8.GetPreamble();
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(body);

        var buffer = new byte[bom.Length + text.Length];
        Buffer.BlockCopy(bom, 0, buffer, 0, bom.Length);
        Buffer.BlockCopy(text, 0, buffer, bom.Length, text.Length);
        return buffer;
    }

    private static byte[] ToXlsx(TranslationExportData data)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(WorksheetName);

        var headers = FixedColumns.Concat(data.LanguageCodes).ToArray();
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        sheet.Row(1).Style.Font.Bold = true;

        var r = 2;
        foreach (var row in data.Rows)
        {
            sheet.Cell(r, 1).Value = row.Key;
            sheet.Cell(r, 2).Value = row.Category;
            if (!string.IsNullOrEmpty(row.Description))
            {
                sheet.Cell(r, 3).Value = row.Description;
            }

            for (var i = 0; i < data.LanguageCodes.Count; i++)
            {
                var value = row.Values.TryGetValue(data.LanguageCodes[i], out var v) ? v : null;
                if (!string.IsNullOrEmpty(value))
                {
                    sheet.Cell(r, 4 + i).Value = value;
                }
            }

            r++;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.SheetView.FreezeColumns(1);

        // Auto-fit, but keep a single long string from blowing a column out to the sheet limit.
        sheet.Columns().AdjustToContents(1, r, 8d, 80d);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
