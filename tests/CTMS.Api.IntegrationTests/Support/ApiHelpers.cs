using System.Net;
using System.Net.Http.Json;
using CTMS.Application.Languages;
using CTMS.Application.Projects;
using CTMS.Application.Translations;

namespace CTMS.Api.IntegrationTests.Support;

/// <summary>
/// Thin request helpers over the HTTP surface. The <c>*Async</c> creators assert the documented
/// success status and return the deserialised DTO; the <c>Raw</c> variants hand back the
/// <see cref="HttpResponseMessage"/> so a test can assert on failure codes itself.
/// </summary>
internal static class ApiHelpers
{
    public static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    public static async Task<LanguageDto> CreateLanguageAsync(
        this HttpClient client,
        string code,
        string? name = null,
        string? fallbackCode = null,
        bool isRtl = false)
    {
        var response = await client.PostAsJsonAsync(
            "/api/languages",
            new CreateLanguageRequest(code, name ?? code, fallbackCode, isRtl));
        // Registering the same language twice across tests sharing a database is fine.
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return (await client.GetFromJsonAsync<LanguageDto>($"/api/languages/{code}"))!;
        }

        await AssertStatus(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<LanguageDto>())!;
    }

    public static async Task<ProjectDto> CreateApplicationAsync(
        this HttpClient client,
        string? code = null,
        string? name = null,
        string baseLanguageCode = "en-GB",
        bool isCommon = false,
        IReadOnlyList<string>? enabledLanguageCodes = null)
    {
        name ??= UniqueName("App");
        var response = await client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest(name, baseLanguageCode, code, null, isCommon, enabledLanguageCodes));
        await AssertStatus(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    public static async Task<ProjectDto> EnableLanguageAsync(
        this HttpClient client,
        string applicationCode,
        string language)
    {
        var response = await client.PutAsync(
            $"/api/projects/{applicationCode}/languages/{language}", content: null);
        await AssertStatus(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    public static async Task<TranslationKeyDto> CreateKeyAsync(
        this HttpClient client,
        string applicationCode,
        string? keyName = null,
        string category = "Common")
    {
        keyName ??= "key." + Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{applicationCode}/keys",
            new CreateTranslationKeyRequest(keyName, category));
        await AssertStatus(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TranslationKeyDto>())!;
    }

    public static Task<HttpResponseMessage> PutStringRaw(
        this HttpClient client,
        string applicationCode,
        Guid keyId,
        string language,
        string value,
        string? updatedBy = null)
        => client.PutAsJsonAsync(
            $"/api/projects/{applicationCode}/keys/{keyId}/strings/{language}",
            new UpsertTranslationStringRequest(value, updatedBy));

    public static async Task<TranslationStringDto> UpsertStringAsync(
        this HttpClient client,
        string applicationCode,
        Guid keyId,
        string language,
        string value,
        string? updatedBy = null)
    {
        var response = await client.PutStringRaw(applicationCode, keyId, language, value, updatedBy);
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created))
        {
            throw await Failure(response, "200 or 201");
        }

        return (await response.Content.ReadFromJsonAsync<TranslationStringDto>())!;
    }

    public static Task<HttpResponseMessage> ReviewRaw(
        this HttpClient client,
        string applicationCode,
        Guid keyId,
        string language,
        string action,
        string reviewedBy = "reviewer")
        => client.PostAsJsonAsync(
            $"/api/projects/{applicationCode}/keys/{keyId}/strings/{language}/review",
            new ReviewRequest(action, reviewedBy));

    public static async Task ReviewAsync(
        this HttpClient client,
        string applicationCode,
        Guid keyId,
        string language,
        string action,
        string reviewedBy = "reviewer")
    {
        var response = await client.ReviewRaw(applicationCode, keyId, language, action, reviewedBy);
        await AssertStatus(response, HttpStatusCode.OK);
    }

    public static Task<HttpResponseMessage> BulkPublishRaw(
        this HttpClient client,
        string applicationCode,
        string? language = null)
        => client.PostAsJsonAsync("/api/translations/publish", new PublishTranslationsRequest(applicationCode, language));

    public static async Task<PublishTranslationsResult> BulkPublishAsync(
        this HttpClient client,
        string applicationCode,
        string? language = null)
    {
        var response = await client.BulkPublishRaw(applicationCode, language);
        await AssertStatus(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PublishTranslationsResult>())!;
    }

    /// <summary>Create key ⇒ upsert ⇒ submit ⇒ approve ⇒ review-<c>publish</c>, leaving the
    /// string in <c>Published</c> for the language.</summary>
    public static async Task<TranslationKeyDto> PublishStringAsync(
        this HttpClient admin,
        string applicationCode,
        string language,
        string keyName,
        string value,
        string category = "Common")
    {
        var key = await admin.CreateKeyAsync(applicationCode, keyName, category);
        await admin.UpsertStringAsync(applicationCode, key.Id, language, value);
        await admin.ReviewAsync(applicationCode, key.Id, language, "submit");
        await admin.ReviewAsync(applicationCode, key.Id, language, "approve");
        await admin.ReviewAsync(applicationCode, key.Id, language, "publish");
        return key;
    }

    public static async Task AssertStatus(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            throw await Failure(response, expected.ToString());
        }
    }

    private static async Task<Xunit.Sdk.XunitException> Failure(HttpResponseMessage response, string expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        return new Xunit.Sdk.XunitException(
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} " +
            $"expected {expected} but got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
    }
}
