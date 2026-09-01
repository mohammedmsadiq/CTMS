using System.Text;
using ClosedXML.Excel;
using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations.Export;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class TranslationExportTests : IDisposable
{
    private const string App = "acme-web";

    private readonly CtmsTestHarness _harness;
    private readonly Guid _appId;

    public TranslationExportTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);
        Seed.LanguageAsync(_harness, "en-GB").GetAwaiter().GetResult();
        Seed.LanguageAsync(_harness, "fr-FR", fallbackCode: "en-GB").GetAwaiter().GetResult();
        _appId = Seed.ApplicationAsync(_harness, App, "en-GB", ["en-GB", "fr-FR"])
            .GetAwaiter().GetResult().Id;
    }

    private Task<ExportedFile?> ExportAsync(
        string format,
        string? language = null,
        string? category = null,
        bool includeInactiveKeys = false,
        string? status = null)
        => _harness.TranslationExportService.ExportAsync(
            App, new TranslationExportQuery(format, language, category, includeInactiveKeys, status));

    private static string Utf8WithoutBom(byte[] bytes)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        var hasBom = bytes.Length >= preamble.Length
            && bytes.Take(preamble.Length).SequenceEqual(preamble);
        return Encoding.UTF8.GetString(hasBom ? bytes[preamble.Length..] : bytes);
    }

    [Fact]
    public async Task Csv_export_has_a_bom_the_expected_header_and_a_blank_cell_for_a_missing_string()
    {
        var greeting = await Seed.KeyAsync(_harness, _appId, "home.greeting", "Home");
        await Seed.KeyAsync(_harness, _appId, "home.tagline", "Home");
        await Seed.StringAsync(_harness, greeting.Id, "en-GB", "Hello");
        await Seed.StringAsync(_harness, greeting.Id, "fr-FR", "Bonjour");

        var file = await ExportAsync("csv");

        Assert.NotNull(file);
        Assert.Equal("acme-web-translations.csv", file!.FileName);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.Equal(Encoding.UTF8.GetPreamble(), file.Bytes.Take(3).ToArray());

        var lines = Utf8WithoutBom(file.Bytes).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("key,category,description,en-GB,fr-FR", lines[0]);
        // Keys are ordered by name; the greeting row carries both values.
        Assert.Equal("home.greeting,Home,,Hello,Bonjour", lines[1]);
        // The tagline has no strings at all — trailing language cells are blank.
        Assert.Equal("home.tagline,Home,,,", lines[2]);
    }

    [Fact]
    public async Task Csv_export_quotes_cells_containing_a_comma_quote_or_newline()
    {
        var key = await Seed.KeyAsync(_harness, _appId, "legal.notice", "Legal");
        await Seed.StringAsync(_harness, key.Id, "en-GB", "Yes, \"really\"\nnow");

        var file = await ExportAsync("csv");
        var text = Utf8WithoutBom(file!.Bytes);

        Assert.Contains("\"Yes, \"\"really\"\"\nnow\"", text);
    }

    [Fact]
    public async Task Csv_export_contains_only_the_projects_own_keys()
    {
        var common = await Seed.ApplicationAsync(_harness, "common", "en-GB", ["en-GB", "fr-FR"], isCommon: true);
        await Seed.KeyAsync(_harness, common.Id, "shared.save", "Common");
        var own = await Seed.KeyAsync(_harness, _appId, "home.title", "Home");
        await Seed.StringAsync(_harness, own.Id, "en-GB", "Title");

        var file = await ExportAsync("csv");
        var text = Utf8WithoutBom(file!.Bytes);

        Assert.Contains("home.title", text);
        Assert.DoesNotContain("shared.save", text);
    }

    [Fact]
    public async Task Csv_export_language_param_restricts_output_to_one_column()
    {
        var key = await Seed.KeyAsync(_harness, _appId, "home.greeting", "Home");
        await Seed.StringAsync(_harness, key.Id, "en-GB", "Hello");
        await Seed.StringAsync(_harness, key.Id, "fr-FR", "Bonjour");

        var file = await ExportAsync("csv", language: "fr-FR");
        var lines = Utf8WithoutBom(file!.Bytes).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("key,category,description,fr-FR", lines[0]);
        Assert.Equal("home.greeting,Home,,Bonjour", lines[1]);
    }

    [Fact]
    public async Task Csv_export_category_param_filters_rows()
    {
        await Seed.KeyAsync(_harness, _appId, "home.greeting", "Home");
        await Seed.KeyAsync(_harness, _appId, "nav.back", "Navigation");

        var file = await ExportAsync("csv", category: "Navigation");
        var text = Utf8WithoutBom(file!.Bytes);

        Assert.Contains("nav.back", text);
        Assert.DoesNotContain("home.greeting", text);
    }

    [Fact]
    public async Task Csv_export_status_param_keeps_only_keys_with_a_value_in_that_state()
    {
        var approved = await Seed.KeyAsync(_harness, _appId, "a.done", "A");
        var draft = await Seed.KeyAsync(_harness, _appId, "b.wip", "B");
        await Seed.StringAsync(_harness, approved.Id, "en-GB", "Done", ReviewState.Approved);
        await Seed.StringAsync(_harness, draft.Id, "en-GB", "Wip", ReviewState.Draft);

        var file = await ExportAsync("csv", status: "Approved");
        var text = Utf8WithoutBom(file!.Bytes);

        Assert.Contains("a.done", text);
        Assert.DoesNotContain("b.wip", text);
    }

    [Fact]
    public async Task An_unknown_export_format_is_a_validation_error()
        => await Assert.ThrowsAsync<ValidationException>(() => ExportAsync("pdf"));

    [Fact]
    public async Task An_unknown_project_is_a_not_found()
    {
        var file = await _harness.TranslationExportService.ExportAsync(
            "no-such-app", new TranslationExportQuery("csv"));
        Assert.Null(file);
    }

    [Fact]
    public async Task Xlsx_export_has_a_bold_frozen_header_and_matching_cell_values()
    {
        var greeting = await Seed.KeyAsync(_harness, _appId, "home.greeting", "Home");
        await Seed.KeyAsync(_harness, _appId, "home.tagline", "Home");
        await Seed.StringAsync(_harness, greeting.Id, "en-GB", "Hello");
        await Seed.StringAsync(_harness, greeting.Id, "fr-FR", "Bonjour");

        var file = await ExportAsync("xlsx");

        Assert.NotNull(file);
        Assert.Equal("acme-web-translations.xlsx", file!.FileName);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            file.ContentType);

        using var stream = new MemoryStream(file.Bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);

        Assert.Equal("Translations", sheet.Name);
        Assert.Equal("key", sheet.Cell(1, 1).GetString());
        Assert.Equal("fr-FR", sheet.Cell(1, 5).GetString());
        Assert.True(sheet.Row(1).Style.Font.Bold);
        Assert.Equal(1, sheet.SheetView.SplitRow);
        Assert.Equal(1, sheet.SheetView.SplitColumn);

        Assert.Equal("home.greeting", sheet.Cell(2, 1).GetString());
        Assert.Equal("Hello", sheet.Cell(2, 4).GetString());
        Assert.Equal("Bonjour", sheet.Cell(2, 5).GetString());
        // The tagline row has no strings — its language cells are blank.
        Assert.True(sheet.Cell(3, 4).IsEmpty());
        Assert.True(sheet.Cell(3, 5).IsEmpty());
    }

    public void Dispose() => _harness.Dispose();
}
