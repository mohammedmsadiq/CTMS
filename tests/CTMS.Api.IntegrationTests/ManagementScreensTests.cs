using System.Net.Http.Json;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;
using CTMS.Application.Common;
using CTMS.Application.Projects;
using CTMS.Application.Translations;

namespace CTMS.Api.IntegrationTests;

/// <summary>The grid / categories / dashboard / missing management screens over HTTP.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class ManagementScreensTests(MongoFixture mongo) : IntegrationTest(mongo)
{
    private HttpClient _client = null!;
    private ApplicationDto _app = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _client = Factory.ClientAs(AuthRoles.Admin);
        await _client.CreateLanguageAsync("en-GB", "English");
        await _client.CreateLanguageAsync("fr-FR", "French", fallbackCode: "en-GB");
        _app = await _client.CreateApplicationAsync(
            code: ApiHelpers.UniqueName("mgmt"), enabledLanguageCodes: ["en-GB", "fr-FR"]);

        var start = await _client.CreateKeyAsync(_app.Code, "course.start", "Course");
        var home = await _client.CreateKeyAsync(_app.Code, "nav.home", "Navigation");
        await _client.UpsertStringAsync(_app.Code, start.Id, "en-GB", "Start");
        await _client.UpsertStringAsync(_app.Code, start.Id, "fr-FR", "Commencer");
        await _client.ReviewAsync(_app.Code, start.Id, "fr-FR", "submit");
        await _client.ReviewAsync(_app.Code, start.Id, "fr-FR", "approve");
        await _client.UpsertStringAsync(_app.Code, home.Id, "en-GB", "Home"); // en only, Draft
    }

    [Fact]
    public async Task Grid_returns_rows_with_language_cells_and_search_matches_values()
    {
        var grid = (await _client.GetFromJsonAsync<PagedResult<TranslationRowDto>>(
            $"/api/translations?application={_app.Code}"))!;
        Assert.Equal(2, grid.Total);
        var row = grid.Items.Single(r => r.Key == "course.start");
        Assert.Equal("Start", row.Values["en-GB"].Value);
        Assert.Equal("Approved", row.Values["fr-FR"].Status);

        var search = (await _client.GetFromJsonAsync<PagedResult<TranslationRowDto>>(
            $"/api/translations?application={_app.Code}&search=commencer"))!;
        Assert.Equal(["course.start"], search.Items.Select(r => r.Key));
    }

    [Fact]
    public async Task Categories_returns_distinct_values()
    {
        var categories = (await _client.GetFromJsonAsync<List<string>>(
            $"/api/categories?application={_app.Code}"))!;
        Assert.Equal(["Course", "Navigation"], categories);
    }

    [Fact]
    public async Task Dashboard_reports_coverage_with_non_draft_as_translated()
    {
        var dashboard = (await _client.GetFromJsonAsync<DashboardResponse>(
            $"/api/dashboard?application={_app.Code}"))!;

        Assert.Equal(1, dashboard.ApplicationCount);
        Assert.Equal(2, dashboard.LanguageCount);
        Assert.Equal(2, dashboard.KeyCount);
        Assert.Equal(0, dashboard.Coverage.Single(c => c.LanguageCode == "en-GB").TranslatedCount);
        Assert.Equal(1, dashboard.Coverage.Single(c => c.LanguageCode == "fr-FR").TranslatedCount);
    }

    [Fact]
    public async Task Missing_lists_keys_without_a_non_draft_value_per_language()
    {
        var missing = (await _client.GetFromJsonAsync<PagedResult<MissingTranslationDto>>(
            $"/api/translations/missing?application={_app.Code}&language=fr-FR"))!;

        var row = Assert.Single(missing.Items);
        Assert.Equal("nav.home", row.Key);
        Assert.Equal(["fr-FR"], row.MissingLanguages);
    }
}
