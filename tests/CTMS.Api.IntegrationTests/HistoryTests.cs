using System.Net.Http.Json;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;
using CTMS.Application.Audit;
using CTMS.Application.Common;
using CTMS.Application.Locales;
using CTMS.Application.Projects;
using CTMS.Application.Translations;

namespace CTMS.Api.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class HistoryTests(MongoFixture mongo) : IntegrationTest(mongo)
{
    private HttpClient _client = null!;
    private ProjectDto _project = null!;
    private LocaleDto _en = null!;
    private TranslationKeyDto _key = null!;
    private TranslationStringDto _string = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _client = Factory.ClientAs(AuthRoles.Admin);
        _project = await _client.CreateProjectAsync(slug: ApiHelpers.UniqueName("history"));
        _en = await _client.CreateLocaleAsync(_project.Id, "en", "English");
        _key = await _client.CreateKeyAsync(_project.Id, "history.key");
        _string = await _client.UpsertStringAsync(_project.Id, _key.Id, _en.Id, "value");
        await _client.ReviewAsync(_project.Id, _key.Id, _en.Id, "submit");
        await _client.ReviewAsync(_project.Id, _key.Id, _en.Id, "approve");
        await _client.ReviewAsync(_project.Id, _key.Id, _en.Id, "publish");
        await _client.PublishBundleAsync(_project.Id, "en");
    }

    [Fact]
    public async Task Project_history_has_the_workflow_and_bundle_entries_newest_first()
    {
        var page = (await _client.GetFromJsonAsync<PagedResult<AuditEntryDto>>(
            $"/api/projects/{_project.Id}/history"))!;

        Assert.Contains(page.Items, e => e.Action == "Submitted" && e.EntityType == "TranslationString");
        Assert.Contains(page.Items, e => e.Action == "Approved" && e.EntityType == "TranslationString");
        Assert.Contains(page.Items, e => e.Action == "Published" && e.EntityType == "TranslationString");
        Assert.Contains(page.Items, e => e.Action == "Published" && e.EntityType == "TranslationBundle");

        AssertNewestFirst(page.Items);
    }

    [Fact]
    public async Task Per_string_history_returns_only_that_strings_entries_newest_first()
    {
        var entries = (await _client.GetFromJsonAsync<List<AuditEntryDto>>(
            $"/api/projects/{_project.Id}/keys/{_key.Id}/strings/{_en.Id}/history"))!;

        Assert.All(entries, e => Assert.Equal("TranslationString", e.EntityType));
        Assert.All(entries, e => Assert.Equal(_string.Id, e.EntityId));
        Assert.Contains(entries, e => e.Action == "Created");
        Assert.Contains(entries, e => e.Action == "Submitted");
        Assert.Contains(entries, e => e.Action == "Approved");
        Assert.Contains(entries, e => e.Action == "Published");

        AssertNewestFirst(entries);
    }

    private static void AssertNewestFirst(IReadOnlyList<AuditEntryDto> entries)
    {
        for (var i = 1; i < entries.Count; i++)
        {
            Assert.True(
                entries[i - 1].Timestamp >= entries[i].Timestamp,
                $"entry {i - 1} ({entries[i - 1].Timestamp:O}) is older than entry {i} ({entries[i].Timestamp:O})");
        }
    }
}
