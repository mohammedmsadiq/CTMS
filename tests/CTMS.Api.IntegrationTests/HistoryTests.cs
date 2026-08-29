using System.Net.Http.Json;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;
using CTMS.Application.Audit;
using CTMS.Application.Common;
using CTMS.Application.Projects;
using CTMS.Application.Translations;

namespace CTMS.Api.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class HistoryTests(MongoFixture mongo) : IntegrationTest(mongo)
{
    private HttpClient _client = null!;
    private ApplicationDto _app = null!;
    private TranslationKeyDto _key = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _client = Factory.ClientAs(AuthRoles.Admin);
        await _client.CreateLanguageAsync("en-GB", "English");
        _app = await _client.CreateApplicationAsync(
            code: ApiHelpers.UniqueName("history"), enabledLanguageCodes: ["en-GB"]);
        _key = await _client.CreateKeyAsync(_app.Code, "history.key");
        await _client.UpsertStringAsync(_app.Code, _key.Id, "en-GB", "value");
        await _client.UpsertStringAsync(_app.Code, _key.Id, "en-GB", "value 2");
        await _client.ReviewAsync(_app.Code, _key.Id, "en-GB", "submit");
        await _client.ReviewAsync(_app.Code, _key.Id, "en-GB", "approve");
        await _client.ReviewAsync(_app.Code, _key.Id, "en-GB", "publish");
    }

    [Fact]
    public async Task Application_history_has_the_workflow_entries_newest_first()
    {
        var page = (await _client.GetFromJsonAsync<PagedResult<AuditEntryDto>>(
            $"/api/applications/{_app.Code}/history"))!;

        Assert.Contains(page.Items, e => e.Action == "Created" && e.EntityType == "TranslationString");
        Assert.Contains(page.Items, e => e.Action == "Submitted");
        Assert.Contains(page.Items, e => e.Action == "Approved");
        Assert.Contains(page.Items, e => e.Action == "Published");

        AssertNewestFirst(page.Items);
    }

    [Fact]
    public async Task Per_string_history_shows_value_diffs()
    {
        var entries = (await _client.GetFromJsonAsync<List<AuditEntryDto>>(
            $"/api/applications/{_app.Code}/keys/{_key.Id}/strings/en-GB/history"))!;

        Assert.All(entries, e => Assert.Equal("TranslationString", e.EntityType));

        var edited = entries.Single(e => e.Action == "Edited");
        Assert.Equal("value", edited.OldValue);
        Assert.Equal("value 2", edited.NewValue);

        var created = entries.Single(e => e.Action == "Created");
        Assert.Null(created.OldValue);
        Assert.Equal("value", created.NewValue);

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
