using System.Net;
using System.Net.Http.Json;
using CTMS.Application.Locales;
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

    public static async Task<ProjectDto> CreateProjectAsync(
        this HttpClient client,
        string? name = null,
        string? slug = null,
        string baseLocaleCode = "en")
    {
        name ??= UniqueName("Project");
        var response = await client.PostAsJsonAsync(
            "/api/projects",
            new CreateProjectRequest(name, baseLocaleCode, slug));
        await AssertStatus(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    public static async Task<LocaleDto> CreateLocaleAsync(
        this HttpClient client,
        Guid projectId,
        string code,
        string? displayName = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/locales",
            new CreateLocaleRequest(code, displayName ?? code.ToUpperInvariant()));
        await AssertStatus(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<LocaleDto>())!;
    }

    public static async Task<TranslationKeyDto> CreateKeyAsync(
        this HttpClient client,
        Guid projectId,
        string? keyName = null)
    {
        keyName ??= "key." + Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/keys",
            new CreateTranslationKeyRequest(keyName));
        await AssertStatus(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TranslationKeyDto>())!;
    }

    public static Task<HttpResponseMessage> PutStringRaw(
        this HttpClient client,
        Guid projectId,
        Guid keyId,
        Guid localeId,
        string value,
        long? expectedVersion = null,
        string? updatedBy = null)
        => client.PutAsJsonAsync(
            $"/api/projects/{projectId}/keys/{keyId}/strings/{localeId}",
            new UpsertTranslationStringRequest(value, updatedBy, expectedVersion));

    public static async Task<TranslationStringDto> UpsertStringAsync(
        this HttpClient client,
        Guid projectId,
        Guid keyId,
        Guid localeId,
        string value,
        long? expectedVersion = null,
        string? updatedBy = null)
    {
        var response = await client.PutStringRaw(projectId, keyId, localeId, value, expectedVersion, updatedBy);
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created))
        {
            throw await Failure(response, "200 or 201");
        }

        return (await response.Content.ReadFromJsonAsync<TranslationStringDto>())!;
    }

    public static Task<HttpResponseMessage> ReviewRaw(
        this HttpClient client,
        Guid projectId,
        Guid keyId,
        Guid localeId,
        string action,
        string reviewedBy = "reviewer")
        => client.PostAsJsonAsync(
            $"/api/projects/{projectId}/keys/{keyId}/strings/{localeId}/review",
            new ReviewRequest(action, reviewedBy));

    public static async Task ReviewAsync(
        this HttpClient client,
        Guid projectId,
        Guid keyId,
        Guid localeId,
        string action,
        string reviewedBy = "reviewer")
    {
        var response = await client.ReviewRaw(projectId, keyId, localeId, action, reviewedBy);
        await AssertStatus(response, HttpStatusCode.OK);
    }

    public static Task<HttpResponseMessage> PublishBundleRaw(
        this HttpClient client,
        Guid projectId,
        string localeCode,
        string? publishedBy = null)
        => client.PostAsJsonAsync(
            $"/api/projects/{projectId}/bundles/{localeCode}",
            new PublishBundleRequest(publishedBy));

    public static async Task<TranslationBundleDto> PublishBundleAsync(
        this HttpClient client,
        Guid projectId,
        string localeCode,
        string? publishedBy = null)
    {
        var response = await client.PublishBundleRaw(projectId, localeCode, publishedBy);
        await AssertStatus(response, HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TranslationBundleDto>())!;
    }

    /// <summary>Create key ⇒ upsert ⇒ submit ⇒ approve ⇒ review-<c>publish</c>, leaving the
    /// string in <c>Published</c> so a bundle can be cut for the locale.</summary>
    public static async Task<TranslationKeyDto> PublishStringAsync(
        this HttpClient admin,
        Guid projectId,
        Guid localeId,
        string keyName,
        string value)
    {
        var key = await admin.CreateKeyAsync(projectId, keyName);
        await admin.UpsertStringAsync(projectId, key.Id, localeId, value);
        await admin.ReviewAsync(projectId, key.Id, localeId, "submit");
        await admin.ReviewAsync(projectId, key.Id, localeId, "approve");
        await admin.ReviewAsync(projectId, key.Id, localeId, "publish");
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
