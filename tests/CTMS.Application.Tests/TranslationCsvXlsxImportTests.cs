using ClosedXML.Excel;
using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations.Export;
using CTMS.Application.Translations.Import;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class TranslationCsvXlsxImportTests : IDisposable
{
    private const string App = "acme-web";

    private readonly CtmsTestHarness _harness;
    private readonly Guid _appId;

    public TranslationCsvXlsxImportTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);
        Seed.LanguageAsync(_harness, "en-GB").GetAwaiter().GetResult();
        Seed.LanguageAsync(_harness, "fr-FR", fallbackCode: "en-GB").GetAwaiter().GetResult();
        _appId = Seed.ApplicationAsync(_harness, App, "en-GB", ["en-GB", "fr-FR"])
            .GetAwaiter().GetResult().Id;
    }

    private Task<ImportTranslationsResult> ImportAsync(
        string format,
        string? content = null,
        string? language = null,
        string? category = null,
        string? status = null,
        bool dryRun = false,
        string? contentBase64 = null)
        => _harness.TranslationImportService.ImportAsync(
            App,
            new ImportTranslationsRequest(format, language, content, category, status, dryRun, contentBase64),
            actor: "importer");

    private async Task<TranslationString?> StringAsync(string keyName, string language)
    {
        var keys = await _harness.Keys.ListByProjectsAsync([_appId]);
        var key = keys.SingleOrDefault(k => k.KeyName == keyName);
        return key is null ? null : await _harness.Strings.GetAsync(key.Id, language);
    }

    private async Task<TranslationKey?> KeyAsync(string keyName)
        => (await _harness.Keys.ListByProjectsAsync([_appId])).SingleOrDefault(k => k.KeyName == keyName);

    private static string XlsxBase64(Action<IXLWorksheet> build)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        build(sheet);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Convert.ToBase64String(stream.ToArray());
    }

    // ---- csv wide -------------------------------------------------------------------------------

    [Fact]
    public async Task Csv_wide_creates_keys_and_strings_in_every_language_column()
    {
        // language is passed but must be ignored for a wide file.
        var result = await ImportAsync(
            "csv",
            "key,en-GB,fr-FR\r\nhome.greeting,Hello,Bonjour\r\nhome.bye,Bye,\r\n",
            language: "en-GB");

        Assert.Equal(2, result.CreatedKeys);
        Assert.Equal(3, result.CreatedStrings); // greeting x2 + bye x1 (blank fr-FR skipped)
        Assert.Empty(result.Errors);
        Assert.Equal("Hello", (await StringAsync("home.greeting", "en-GB"))!.Value);
        Assert.Equal("Bonjour", (await StringAsync("home.greeting", "fr-FR"))!.Value);
        Assert.Equal("Bye", (await StringAsync("home.bye", "en-GB"))!.Value);
        Assert.Null(await StringAsync("home.bye", "fr-FR"));
    }

    [Fact]
    public async Task Csv_wide_respects_a_category_column_on_a_created_key()
    {
        await ImportAsync("csv", "key,category,en-GB\r\nfoo.x,Marketing,Hi\r\n");

        Assert.Equal("Marketing", (await KeyAsync("foo.x"))!.Category);
    }

    [Fact]
    public async Task Csv_wide_blank_cell_never_deletes_an_existing_value()
    {
        var key = await Seed.KeyAsync(_harness, _appId, "home.greeting", "Home");
        await Seed.StringAsync(_harness, key.Id, "fr-FR", "Bonjour");

        var result = await ImportAsync("csv", "key,en-GB,fr-FR\r\nhome.greeting,Hello,\r\n");

        Assert.Equal(1, result.CreatedStrings); // en-GB only
        Assert.Equal("Bonjour", (await StringAsync("home.greeting", "fr-FR"))!.Value); // untouched
    }

    [Fact]
    public async Task Csv_wide_malformed_row_is_reported_with_its_line_number()
    {
        var result = await ImportAsync("csv", "key,en-GB\r\n,Hello\r\nfoo.ok,Hi\r\n");

        var error = Assert.Single(result.Errors);
        Assert.Equal(2, error.Line);
        Assert.Equal(1, result.CreatedStrings); // foo.ok still imported
        Assert.Equal("Hi", (await StringAsync("foo.ok", "en-GB"))!.Value);
    }

    [Fact]
    public async Task Csv_wide_dry_run_writes_nothing()
    {
        var result = await ImportAsync(
            "csv", "key,en-GB,fr-FR\r\nhome.greeting,Hello,Bonjour\r\n", dryRun: true);

        Assert.Equal(2, result.CreatedStrings);
        Assert.Null(await KeyAsync("home.greeting"));
    }

    [Fact]
    public async Task Csv_wide_quoted_cell_with_comma_and_newline_round_trips()
    {
        await ImportAsync("csv", "key,en-GB\r\nlegal.notice,\"Yes, really\nnow\"\r\n");

        Assert.Equal("Yes, really\nnow", (await StringAsync("legal.notice", "en-GB"))!.Value);
    }

    // ---- csv narrow ----------------------------------------------------------------------------

    [Fact]
    public async Task Csv_narrow_uses_the_request_language()
    {
        var result = await ImportAsync(
            "csv", "key,value\r\nhome.greeting,Bonjour\r\n", language: "fr-FR");

        Assert.Equal(1, result.CreatedStrings);
        Assert.Equal("Bonjour", (await StringAsync("home.greeting", "fr-FR"))!.Value);
    }

    [Fact]
    public async Task Csv_narrow_without_a_language_is_a_validation_error()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => ImportAsync("csv", "key,value\r\nhome.greeting,Bonjour\r\n"));

        Assert.Contains("language is required", ex.Message);
    }

    [Fact]
    public async Task An_unknown_format_is_a_validation_error()
    {
        var ex = await Assert.ThrowsAsync<ImportFormatException>(
            () => ImportAsync("bmp", "key,value\r\na,b\r\n"));

        Assert.Contains("json, flat, csv, xlsx", ex.Message);
    }

    // ---- xlsx ---------------------------------------------------------------------------------

    [Fact]
    public async Task Xlsx_wide_creates_strings_using_the_1_based_row_number_for_errors()
    {
        var base64 = XlsxBase64(sheet =>
        {
            sheet.Cell(1, 1).Value = "key";
            sheet.Cell(1, 2).Value = "en-GB";
            sheet.Cell(1, 3).Value = "fr-FR";
            sheet.Cell(2, 1).Value = "home.greeting";
            sheet.Cell(2, 2).Value = "Hello";
            sheet.Cell(2, 3).Value = "Bonjour";
            sheet.Cell(3, 2).Value = "orphan"; // row 3 has no key
        });

        var result = await ImportAsync("xlsx", contentBase64: base64);

        Assert.Equal(2, result.CreatedStrings);
        var error = Assert.Single(result.Errors);
        Assert.Equal(3, error.Line);
    }

    [Fact]
    public async Task Xlsx_wide_round_trips_an_exported_file_as_an_upsert_with_review_reset()
    {
        // Seed an approved en-GB value, export, tweak two cells, re-import.
        var key = await Seed.KeyAsync(_harness, _appId, "home.greeting", "Home");
        await Seed.StringAsync(_harness, key.Id, "en-GB", "Hello", ReviewState.Approved);

        var exported = await _harness.TranslationExportService.ExportAsync(
            App, new TranslationExportQuery("xlsx"));
        Assert.NotNull(exported);

        byte[] tweaked;
        using (var stream = new MemoryStream(exported!.Bytes))
        using (var workbook = new XLWorkbook(stream))
        {
            var sheet = workbook.Worksheet(1);
            // Columns: key, category, description, en-GB, fr-FR
            Assert.Equal("home.greeting", sheet.Cell(2, 1).GetString());
            sheet.Cell(2, 4).Value = "Hello there";  // change en-GB
            sheet.Cell(2, 5).Value = "Bonjour";      // add fr-FR
            using var outStream = new MemoryStream();
            workbook.SaveAs(outStream);
            tweaked = outStream.ToArray();
        }

        var result = await ImportAsync(
            "xlsx", contentBase64: Convert.ToBase64String(tweaked), status: "InReview");

        Assert.Equal(1, result.CreatedStrings); // fr-FR
        Assert.Equal(1, result.UpdatedStrings); // en-GB
        var en = (await StringAsync("home.greeting", "en-GB"))!;
        Assert.Equal("Hello there", en.Value);
        Assert.Equal(ReviewState.InReview, en.ReviewState);
        Assert.Equal("Bonjour", (await StringAsync("home.greeting", "fr-FR"))!.Value);
    }

    [Fact]
    public async Task Xlsx_with_non_base64_content_is_a_validation_error()
    {
        var ex = await Assert.ThrowsAsync<ImportFormatException>(
            () => ImportAsync("xlsx", contentBase64: "not-base-64!!"));

        Assert.Contains("base64", ex.Message);
    }

    public void Dispose() => _harness.Dispose();
}
