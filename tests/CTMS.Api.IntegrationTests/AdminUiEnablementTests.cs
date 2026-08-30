using System.Net;
using System.Net.Http.Json;
using System.Text;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;
using CTMS.Application.Common;
using CTMS.Application.Languages;
using CTMS.Application.Translations;
using CTMS.Application.Translations.Import;

namespace CTMS.Api.IntegrationTests;

/// <summary>
/// HTTP wiring for the Admin-UI enablement endpoints: language catalogue + bulk create, bulk
/// file import (including the larger body cap), the grid status filter, bulk review and the
/// publish preview.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AdminUiEnablementTests(MongoFixture mongo) : IntegrationTest(mongo)
{
    [Fact]
    public async Task Language_bulk_create_is_idempotent()
    {
        using var admin = Factory.ClientAs(AuthRoles.Admin);
        var body = new BulkCreateLanguagesRequest(
        [
            new BulkCreateLanguageItem("en-GB", "English"),
            new BulkCreateLanguageItem("fr-FR", "French", "en-GB"),
        ]);

        var first = (await (await admin.PostAsJsonAsync("/api/languages/bulk", body))
            .Content.ReadFromJsonAsync<BulkCreateLanguagesResult>())!;
        Assert.Equal(2, first.Created.Count);

        var second = (await (await admin.PostAsJsonAsync("/api/languages/bulk", body))
            .Content.ReadFromJsonAsync<BulkCreateLanguagesResult>())!;
        Assert.Empty(second.Created);
        Assert.Equal(2, second.Skipped.Count);
    }

    [Fact]
    public async Task Import_creates_keys_and_strings_and_accepts_a_body_over_the_global_cap()
    {
        using var admin = Factory.ClientAs(AuthRoles.Admin);
        await admin.CreateLanguageAsync("en-GB", "English");
        await admin.CreateLanguageAsync("fr-FR", "French", fallbackCode: "en-GB");
        var app = await admin.CreateApplicationAsync(
            code: ApiHelpers.UniqueName("import"), enabledLanguageCodes: ["en-GB", "fr-FR"]);

        // Build a JSON body larger than the 256 KB global request cap.
        var sb = new StringBuilder("{");
        for (var i = 0; i < 4000; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append($"\"import.key{i:D5}\":\"valeur {i} {new string('x', 40)}\"");
        }

        sb.Append('}');
        Assert.True(sb.Length > 262144);

        var request = new ImportTranslationsRequest("json", "fr-FR", sb.ToString(), Category: "Imported", Status: "InReview");
        using var response = await admin.PostAsJsonAsync($"/api/projects/{app.Code}/import", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = (await response.Content.ReadFromJsonAsync<ImportTranslationsResult>())!;
        Assert.Equal(4000, result.CreatedKeys);
        Assert.Equal(4000, result.CreatedStrings);
        Assert.Equal(200, result.Keys.Count); // capped

        // A malformed body for the declared format is a 400.
        var bad = new ImportTranslationsRequest("json", "fr-FR", "{ not valid");
        using var badResponse = await admin.PostAsJsonAsync($"/api/projects/{app.Code}/import", bad);
        Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);
    }

    [Fact]
    public async Task Grid_status_filter_and_publish_preview_and_bulk_review_over_http()
    {
        using var admin = Factory.ClientAsActor("lead", AuthRoles.Admin);
        await admin.CreateLanguageAsync("en-GB", "English");
        await admin.CreateLanguageAsync("fr-FR", "French", fallbackCode: "en-GB");
        var app = await admin.CreateApplicationAsync(
            code: ApiHelpers.UniqueName("grid"), enabledLanguageCodes: ["en-GB", "fr-FR"]);

        var k1 = await admin.CreateKeyAsync(app.Code, "course.start", "Course");
        var k2 = await admin.CreateKeyAsync(app.Code, "course.resume", "Course");
        var k3 = await admin.CreateKeyAsync(app.Code, "course.finish", "Course");
        await admin.UpsertStringAsync(app.Code, k1.Id, "fr-FR", "Commencer");
        await admin.UpsertStringAsync(app.Code, k2.Id, "fr-FR", "Reprendre");
        await admin.UpsertStringAsync(app.Code, k3.Id, "fr-FR", "Terminer");
        await admin.ReviewAsync(app.Code, k1.Id, "fr-FR", "submit");
        await admin.ReviewAsync(app.Code, k1.Id, "fr-FR", "approve"); // k1 = Approved
        await admin.ReviewAsync(app.Code, k3.Id, "fr-FR", "submit");   // k3 = InReview, k2 = Draft

        var invalidStatus = await admin.GetAsync($"/api/translations?project={app.Code}&status=Bogus");
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatus.StatusCode);

        var approvedGrid = (await admin.GetFromJsonAsync<PagedResult<TranslationRowDto>>(
            $"/api/translations?project={app.Code}&status=Approved"))!;
        var row = Assert.Single(approvedGrid.Items);
        Assert.Equal("course.start", row.Key);
        Assert.Equal("app", row.Values["fr-FR"].Source);

        var preview = (await admin.GetFromJsonAsync<PublishPreviewResponse>(
            $"/api/translations/publish/preview?project={app.Code}&language=fr-FR"))!;
        Assert.Equal(1, preview.AddedCount); // only k1 (Approved) would be delivered
        Assert.Contains(preview.Changes, c => c.Key == "course.start" && c.Kind == "added");

        var missingLanguage = await admin.GetAsync(
            $"/api/translations/publish/preview?project={app.Code}");
        Assert.Equal(HttpStatusCode.BadRequest, missingLanguage.StatusCode);

        // Bulk review: an unfiltered call is rejected; a filtered one only transitions eligible rows.
        var unfiltered = await admin.PostAsJsonAsync(
            $"/api/projects/{app.Code}/review-bulk", new ReviewBulkRequest("approve"));
        Assert.Equal(HttpStatusCode.BadRequest, unfiltered.StatusCode);

        var bulk = (await (await admin.PostAsJsonAsync(
                $"/api/projects/{app.Code}/review-bulk",
                new ReviewBulkRequest("approve", Language: "fr-FR")))
            .Content.ReadFromJsonAsync<ReviewBulkResult>())!;
        Assert.Equal(1, bulk.Transitioned); // k3 InReview -> Approved
        Assert.Equal(2, bulk.Skipped);      // k1 already Approved, k2 Draft -> approve illegal
    }
}
