using CTMS.Application.Common;
using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations.Import;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class TranslationImportTests : IDisposable
{
    private const string App = "acme-web";

    private readonly CtmsTestHarness _harness;

    public TranslationImportTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);
        Seed.LanguageAsync(_harness, "en-GB").GetAwaiter().GetResult();
        Seed.LanguageAsync(_harness, "fr-FR", fallbackCode: "en-GB").GetAwaiter().GetResult();
        Seed.ApplicationAsync(_harness, App, "en-GB", ["fr-FR"]).GetAwaiter().GetResult();
    }

    private Task<ImportTranslationsResult> ImportAsync(
        string format,
        string content,
        string? category = null,
        string? status = null,
        bool dryRun = false,
        string language = "fr-FR")
        => _harness.TranslationImportService.ImportAsync(
            App,
            new ImportTranslationsRequest(format, language, content, category, status, dryRun),
            actor: "importer");

    private async Task<TranslationString?> StringAsync(string keyName, string language = "fr-FR")
    {
        var keys = await _harness.Keys.ListByProjectsAsync(
            [(await _harness.Projects.GetBySlugAsync(App))!.Id]);
        var key = keys.SingleOrDefault(k => k.KeyName == keyName);
        return key is null ? null : await _harness.Strings.GetAsync(key.Id, language);
    }

    [Fact]
    public async Task Json_flat_object_creates_keys_and_strings()
    {
        var result = await ImportAsync("json", """{ "course.start": "Commencer", "nav.home": "Accueil" }""");

        Assert.Equal(2, result.CreatedKeys);
        Assert.Equal(2, result.CreatedStrings);
        Assert.Equal(0, result.UpdatedStrings);
        Assert.Empty(result.Errors);
        Assert.Equal(["course.start", "nav.home"], result.Keys);
        Assert.Equal("Commencer", (await StringAsync("course.start"))!.Value);
        Assert.Equal(ReviewState.Draft, (await StringAsync("course.start"))!.ReviewState);
    }

    [Fact]
    public async Task Json_nested_object_is_flattened_with_dots()
    {
        var result = await ImportAsync("json", """{ "course": { "start": "Commencer", "steps": { "next": "Suivant" } } }""");

        Assert.Equal(2, result.CreatedStrings);
        Assert.Equal("Commencer", (await StringAsync("course.start"))!.Value);
        Assert.Equal("Suivant", (await StringAsync("course.steps.next"))!.Value);
    }

    [Fact]
    public async Task Flat_key_value_ignores_comments_and_blank_lines()
    {
        var body = "# a comment\n\ncourse.start = Commencer\nnav.home=Accueil\n";
        var result = await ImportAsync("flat", body);

        Assert.Equal(2, result.CreatedStrings);
        Assert.Equal("Commencer", (await StringAsync("course.start"))!.Value); // surrounding whitespace trimmed
    }

    [Fact]
    public async Task An_unknown_format_is_a_validation_error()
    {
        var ex = await Assert.ThrowsAsync<ImportFormatException>(() => ImportAsync("bmp", "key,value\na,b"));
        Assert.Contains("json, flat, csv, xlsx", ex.Message);
    }

    [Fact]
    public async Task Malformed_json_is_a_validation_error_naming_the_line()
    {
        var ex = await Assert.ThrowsAsync<ImportFormatException>(
            () => ImportAsync("json", "{ \"course.start\": \"Commencer\" \n \"nav.home\": }"));
        Assert.NotNull(ex.Line);
        Assert.Contains("Line", ex.Message);
    }

    [Fact]
    public async Task Malformed_flat_line_without_an_equals_is_a_validation_error()
    {
        var ex = await Assert.ThrowsAsync<ImportFormatException>(
            () => ImportAsync("flat", "course.start = ok\nthis line has no equals\n"));
        Assert.Equal(2, ex.Line);
    }

    [Fact]
    public async Task Dry_run_computes_the_plan_but_writes_nothing()
    {
        var result = await ImportAsync("json", """{ "course.start": "Commencer" }""", dryRun: true);

        Assert.Equal(1, result.CreatedKeys);
        Assert.Equal(1, result.CreatedStrings);
        Assert.Null(await StringAsync("course.start"));
    }

    [Fact]
    public async Task Status_Approved_drives_the_new_string_to_Approved()
    {
        await ImportAsync("flat", "course.start=Commencer", status: "Approved");

        Assert.Equal(ReviewState.Approved, (await StringAsync("course.start"))!.ReviewState);
    }

    [Fact]
    public async Task Status_Published_is_rejected_with_400()
        => await Assert.ThrowsAsync<ValidationException>(
            () => ImportAsync("flat", "course.start=Commencer", status: "Published"));

    [Fact]
    public async Task Re_importing_an_identical_file_skips_every_row()
    {
        await ImportAsync("flat", "course.start=Commencer\nnav.home=Accueil");
        var second = await ImportAsync("flat", "course.start=Commencer\nnav.home=Accueil");

        Assert.Equal(0, second.CreatedStrings);
        Assert.Equal(0, second.UpdatedStrings);
        Assert.Equal(2, second.Skipped);
    }

    [Fact]
    public async Task Re_importing_a_changed_value_updates_the_string()
    {
        await ImportAsync("flat", "course.start=Commencer");
        var second = await ImportAsync("flat", "course.start=Recommencer");

        Assert.Equal(1, second.UpdatedStrings);
        Assert.Equal("Recommencer", (await StringAsync("course.start"))!.Value);
    }

    [Fact]
    public async Task Category_is_the_request_value_or_derived_from_the_key_name()
    {
        await ImportAsync("flat", "course.start=Commencer");                 // derived
        await ImportAsync("flat", "nav.home=Accueil", category: "Chrome");   // explicit

        var keys = await _harness.Keys.ListByProjectsAsync(
            [(await _harness.Projects.GetBySlugAsync(App))!.Id]);
        Assert.Equal("Course", keys.Single(k => k.KeyName == "course.start").Category);
        Assert.Equal("Chrome", keys.Single(k => k.KeyName == "nav.home").Category);
    }

    [Fact]
    public async Task An_unenabled_language_is_a_not_found()
        => await Assert.ThrowsAsync<NotFoundException>(
            () => ImportAsync("flat", "course.start=Commencer", language: "en-GB"));

    [Fact]
    public async Task An_invalid_key_name_is_reported_as_a_row_error_not_a_failure()
    {
        var result = await ImportAsync("json", """{ "ok.key": "fine", "bad key!": "nope" }""");

        Assert.Equal(1, result.CreatedStrings);
        var error = Assert.Single(result.Errors);
        Assert.Equal("bad key!", error.Key);
    }

    public void Dispose() => _harness.Dispose();
}
