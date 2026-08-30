using CTMS.Application.Tests.Infrastructure;
using CTMS.Application.Translations;
using CTMS.Domain.Audit;
using CTMS.Domain.Translations;

namespace CTMS.Application.Tests;

[Collection("mongo")]
public sealed class AuditTests : IDisposable
{
    private readonly CtmsTestHarness _harness;
    private readonly Guid _projectId;
    private readonly Guid _keyId;

    public AuditTests(MongoFixture fixture)
    {
        _harness = new CtmsTestHarness(fixture.ConnectionString);

        Seed.LanguageAsync(_harness, "en-GB").GetAwaiter().GetResult();
        Seed.LanguageAsync(_harness, "fr-FR", fallbackCode: "en-GB").GetAwaiter().GetResult();
        var project = Seed.ApplicationAsync(_harness, "acme-web", "en-GB", ["fr-FR"]).GetAwaiter().GetResult();
        var key = Seed.KeyAsync(_harness, project.Id, "checkout.title").GetAwaiter().GetResult();
        _projectId = project.Id;
        _keyId = key.Id;
    }

    [Fact]
    public async Task AppendAsync_and_ListByEntityAsync_return_entries_newest_first()
    {
        var entityId = Guid.NewGuid();
        await _harness.Audit.AppendAsync(new AuditEntry(_projectId, "TranslationString", entityId, AuditAction.Created, "a"));
        await _harness.Audit.AppendAsync(new AuditEntry(
            _projectId, "TranslationString", entityId, AuditAction.Submitted, "b", ReviewState.Draft, ReviewState.NeedsReview));
        await _harness.Audit.AppendAsync(new AuditEntry(_projectId, "TranslationString", Guid.NewGuid(), AuditAction.Created, "c"));

        var entries = await _harness.Audit.ListByEntityAsync("TranslationString", entityId);

        Assert.Equal(2, entries.Count);
        Assert.Equal(AuditAction.Submitted, entries[0].Action);
        Assert.Equal(AuditAction.Created, entries[1].Action);
    }

    [Fact]
    public async Task ListByApplicationAsync_pages_and_reports_the_total()
    {
        for (var i = 0; i < 7; i++)
        {
            await _harness.Audit.AppendAsync(
                new AuditEntry(_projectId, "TranslationString", Guid.NewGuid(), AuditAction.Created, $"actor-{i}"));
        }

        var page = await _harness.AuditService.ListByApplicationAsync("acme-web", skip: 2, take: 3);

        Assert.NotNull(page);
        Assert.Equal(7, page!.Total);
        Assert.Equal(3, page.Items.Count);
    }

    [Fact]
    public async Task ListByApplicationAsync_returns_null_for_an_unknown_application()
        => Assert.Null(await _harness.AuditService.ListByApplicationAsync("nope", 0, 50));

    [Fact]
    public async Task AuditEntryDto_exposes_the_owning_application_id_as_applicationId()
    {
        await _harness.Audit.AppendAsync(
            new AuditEntry(_projectId, "TranslationString", Guid.NewGuid(), AuditAction.Created, "a"));

        var page = await _harness.AuditService.ListByApplicationAsync("acme-web", 0, 50);

        Assert.Equal(_projectId, page!.Items[0].ApplicationId);
    }

    [Fact]
    public async Task Upsert_and_review_write_an_audit_trail_with_value_diffs()
    {
        var created = await _harness.TranslationStringService.UpsertAsync(
            "acme-web", _keyId, "fr-FR", new UpsertTranslationStringRequest("v1", UpdatedBy: "alice"));
        var stringId = created.String.Id;

        await _harness.TranslationStringService.UpsertAsync(
            "acme-web", _keyId, "fr-FR", new UpsertTranslationStringRequest("v2", UpdatedBy: "alice"));
        await _harness.TranslationStringService.ReviewAsync("acme-web", _keyId, "fr-FR", "submit", "alice");
        await _harness.TranslationStringService.ReviewAsync("acme-web", _keyId, "fr-FR", "approve", "lead");
        await _harness.TranslationStringService.ReviewAsync("acme-web", _keyId, "fr-FR", "publish", "release-bot");

        var trail = await _harness.Audit.ListByEntityAsync("TranslationString", stringId);
        var actionsOldestFirst = trail.Select(e => e.Action).Reverse().ToArray();

        Assert.Equal(
            new[]
            {
                AuditAction.Created, AuditAction.Edited, AuditAction.Submitted, AuditAction.Approved, AuditAction.Published,
            },
            actionsOldestFirst);

        var createdEntry = trail.Single(e => e.Action == AuditAction.Created);
        Assert.Null(createdEntry.OldValue);
        Assert.Equal("v1", createdEntry.NewValue);

        var editedEntry = trail.Single(e => e.Action == AuditAction.Edited);
        Assert.Equal("v1", editedEntry.OldValue);
        Assert.Equal("v2", editedEntry.NewValue);

        var publishEntry = trail[0];
        Assert.Equal(ReviewState.Approved, publishEntry.FromState);
        Assert.Equal(ReviewState.Published, publishEntry.ToState);
        Assert.Null(publishEntry.OldValue);
    }

    public void Dispose() => _harness.Dispose();
}
