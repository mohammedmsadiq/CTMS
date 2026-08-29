using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations;
using CTMS.Domain.Audit;
using CTMS.Domain.Locales;
using CTMS.Domain.Projects;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class AuditTests : IDisposable
{
    private readonly CtmsTestHarness _harness;
    private readonly Guid _projectId;
    private readonly Guid _keyId;
    private readonly Guid _localeId;

    public AuditTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);

        var project = new Project("Acme Web", "acme-web", "en");
        var key = new TranslationKey(project.Id, "checkout.title");
        var locale = new Locale(project.Id, "fr", "French");
        _harness.Projects.AddAsync(project).GetAwaiter().GetResult();
        _harness.Keys.AddAsync(key).GetAwaiter().GetResult();
        _harness.Locales.AddAsync(locale).GetAwaiter().GetResult();

        _projectId = project.Id;
        _keyId = key.Id;
        _localeId = locale.Id;
    }

    [Fact]
    public async Task AppendAsync_and_ListByEntityAsync_return_entries_newest_first()
    {
        var entityId = Guid.NewGuid();
        await _harness.Audit.AppendAsync(new AuditEntry(_projectId, "TranslationString", entityId, AuditAction.Created, "a"));
        await _harness.Audit.AppendAsync(
            new AuditEntry(_projectId, "TranslationString", entityId, AuditAction.Submitted, "b", ReviewState.Draft, ReviewState.NeedsReview));
        await _harness.Audit.AppendAsync(new AuditEntry(_projectId, "TranslationString", Guid.NewGuid(), AuditAction.Created, "c"));

        var entries = await _harness.Audit.ListByEntityAsync("TranslationString", entityId);

        Assert.Equal(2, entries.Count);
        Assert.Equal(AuditAction.Submitted, entries[0].Action);
        Assert.Equal(AuditAction.Created, entries[1].Action);
        Assert.Equal(ReviewState.NeedsReview, entries[0].ToState);
    }

    [Fact]
    public async Task ListByProjectAsync_pages_and_reports_the_total()
    {
        for (var i = 0; i < 7; i++)
        {
            await _harness.Audit.AppendAsync(
                new AuditEntry(_projectId, "TranslationString", Guid.NewGuid(), AuditAction.Created, $"actor-{i}"));
        }

        var page = await _harness.AuditService.ListByProjectAsync(_projectId, skip: 2, take: 3);

        Assert.Equal(7, page.Total);
        Assert.Equal(3, page.Items.Count);
        Assert.All(page.Items, e => Assert.Equal("Created", e.Action));
    }

    [Fact]
    public async Task TranslationStringService_writes_an_audit_trail_for_upsert_and_review()
    {
        var created = await _harness.TranslationStringService.UpsertAsync(
            _projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v1", UpdatedBy: "alice"));
        var stringId = created.String.Id;

        await _harness.TranslationStringService.UpsertAsync(
            _projectId, _keyId, _localeId, new UpsertTranslationStringRequest("v2", UpdatedBy: "alice"));
        await _harness.TranslationStringService.ReviewAsync(_projectId, _keyId, _localeId, "submit", "alice");
        await _harness.TranslationStringService.ReviewAsync(_projectId, _keyId, _localeId, "approve", "lead");
        await _harness.TranslationStringService.ReviewAsync(_projectId, _keyId, _localeId, "publish", "release-bot");

        var trail = await _harness.Audit.ListByEntityAsync("TranslationString", stringId);
        var actionsOldestFirst = trail.Select(e => e.Action).Reverse().ToArray();

        Assert.Equal(
            new[]
            {
                AuditAction.Created,
                AuditAction.Edited,
                AuditAction.Submitted,
                AuditAction.Approved,
                AuditAction.Published,
            },
            actionsOldestFirst);

        var publishEntry = trail[0];
        Assert.Equal(ReviewState.Approved, publishEntry.FromState);
        Assert.Equal(ReviewState.Published, publishEntry.ToState);
        Assert.Equal("release-bot", publishEntry.Actor);
    }

    public void Dispose() => _harness.Dispose();
}
