using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CTMS.AdminUI.ApiContracts;

namespace CTMS.AdminUI.Services;

/// <summary>
/// Typed wrapper over backend-core's <c>/api/*</c> HTTP surface. Every method returns a
/// <see cref="Result"/> / <see cref="Result{T}"/> so callers can render loading / empty /
/// error states without catching exceptions. The underlying <see cref="HttpClient"/> is
/// supplied by <see cref="IHttpClientFactory"/> with its base address bound to the
/// <c>Ctms:ApiBaseUrl</c> configuration key.
/// </summary>
/// <remarks>
/// The string upsert is last-write-wins on the server: there is no version token, no
/// <c>expectedVersion</c> request member and no <c>409</c> concurrency response, so this
/// client carries no conflict handling.
/// </remarks>
public sealed class CtmsApiClient(HttpClient http)
{
    public const string HttpClientName = "CtmsApi";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ---- Applications -------------------------------------------------

    public Task<Result<IReadOnlyList<ApplicationDto>>> GetApplicationsAsync(
        bool includeInactive = false, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ApplicationDto>>(
            "api/applications" + Query(("includeInactive", includeInactive ? "true" : null)), ct);

    public Task<Result<ApplicationDto>> GetApplicationAsync(string code, CancellationToken ct = default) =>
        GetAsync<ApplicationDto>($"api/applications/{Esc(code)}", ct);

    public Task<Result<ApplicationDto>> CreateApplicationAsync(
        CreateApplicationRequest request, CancellationToken ct = default) =>
        SendAsync<ApplicationDto>(HttpMethod.Post, "api/applications", request, ct);

    public Task<Result<ApplicationDto>> UpdateApplicationAsync(
        string code, UpdateApplicationRequest request, CancellationToken ct = default) =>
        SendAsync<ApplicationDto>(HttpMethod.Patch, $"api/applications/{Esc(code)}", request, ct);

    public Task<Result<ApplicationDto>> EnableApplicationLanguageAsync(
        string code, string language, CancellationToken ct = default) =>
        SendAsync<ApplicationDto>(
            HttpMethod.Put, $"api/applications/{Esc(code)}/languages/{Esc(language)}", null, ct);

    public Task<Result<ApplicationDto>> DisableApplicationLanguageAsync(
        string code, string language, CancellationToken ct = default) =>
        SendAsync<ApplicationDto>(
            HttpMethod.Delete, $"api/applications/{Esc(code)}/languages/{Esc(language)}", null, ct);

    // ---- Languages (global) ----------------------------------------

    public Task<Result<IReadOnlyList<LanguageDto>>> GetLanguagesAsync(
        bool includeInactive = false, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<LanguageDto>>(
            "api/languages" + Query(("includeInactive", includeInactive ? "true" : null)), ct);

    public Task<Result<LanguageDto>> GetLanguageAsync(string code, CancellationToken ct = default) =>
        GetAsync<LanguageDto>($"api/languages/{Esc(code)}", ct);

    public Task<Result<LanguageDto>> CreateLanguageAsync(
        CreateLanguageRequest request, CancellationToken ct = default) =>
        SendAsync<LanguageDto>(HttpMethod.Post, "api/languages", request, ct);

    public Task<Result<LanguageDto>> UpdateLanguageAsync(
        string code, UpdateLanguageRequest request, CancellationToken ct = default) =>
        SendAsync<LanguageDto>(HttpMethod.Patch, $"api/languages/{Esc(code)}", request, ct);

    /// <summary>The static standard-language suggestion list (anonymous in the default config).</summary>
    public Task<Result<IReadOnlyList<LanguageSuggestionDto>>> GetLanguageSuggestionsAsync(
        CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<LanguageSuggestionDto>>("api/languages/suggestions", ct);

    /// <summary>Idempotently add a set of languages to the global catalogue.</summary>
    public Task<Result<BulkLanguagesResult>> BulkCreateLanguagesAsync(
        BulkLanguagesRequest request, CancellationToken ct = default) =>
        SendAsync<BulkLanguagesResult>(HttpMethod.Post, "api/languages/bulk", request, ct);

    // ---- Translation keys ---------------------------------------

    public Task<Result<PagedResult<TranslationKeyDto>>> GetKeysAsync(
        string application, string? category, int skip, int take, CancellationToken ct = default) =>
        GetAsync<PagedResult<TranslationKeyDto>>(
            $"api/applications/{Esc(application)}/keys" + Query(
                ("category", string.IsNullOrWhiteSpace(category) ? null : category),
                ("skip", skip.ToString()),
                ("take", take.ToString())),
            ct);

    public Task<Result<TranslationKeyDto>> GetKeyAsync(
        string application, Guid keyId, CancellationToken ct = default) =>
        GetAsync<TranslationKeyDto>($"api/applications/{Esc(application)}/keys/{keyId}", ct);

    public Task<Result<TranslationKeyDto>> CreateKeyAsync(
        string application, CreateTranslationKeyRequest request, CancellationToken ct = default) =>
        SendAsync<TranslationKeyDto>(HttpMethod.Post, $"api/applications/{Esc(application)}/keys", request, ct);

    public Task<Result<TranslationKeyDto>> UpdateKeyAsync(
        string application, Guid keyId, UpdateTranslationKeyRequest request, CancellationToken ct = default) =>
        SendAsync<TranslationKeyDto>(
            HttpMethod.Patch, $"api/applications/{Esc(application)}/keys/{keyId}", request, ct);

    public Task<Result> DeleteKeyAsync(string application, Guid keyId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/applications/{Esc(application)}/keys/{keyId}", ct);

    // ---- Import --------------------------------------------------

    /// <summary>
    /// Bulk-import translations for one language. <c>dryRun: true</c> returns a plan without
    /// persisting. A <c>400</c> carries the offending line in <see cref="ApiError.Detail"/>.
    /// </summary>
    public Task<Result<ImportTranslationsResult>> ImportTranslationsAsync(
        string application, ImportTranslationsRequest request, CancellationToken ct = default) =>
        SendAsync<ImportTranslationsResult>(
            HttpMethod.Post, $"api/applications/{Esc(application)}/import", request, ct);

    /// <summary>Apply one review verb to every string matching a filter (language / category / keyIds).</summary>
    public Task<Result<ReviewBulkResult>> ReviewBulkAsync(
        string application, ReviewBulkRequest request, CancellationToken ct = default) =>
        SendAsync<ReviewBulkResult>(
            HttpMethod.Post, $"api/applications/{Esc(application)}/review-bulk", request, ct);

    // ---- Translation strings -------------------------------

    public Task<Result<IReadOnlyList<TranslationStringDto>>> GetStringsForKeyAsync(
        string application, Guid keyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<TranslationStringDto>>(
            $"api/applications/{Esc(application)}/keys/{keyId}/strings", ct);

    public Task<Result<TranslationStringDto>> GetStringAsync(
        string application, Guid keyId, string language, CancellationToken ct = default) =>
        GetAsync<TranslationStringDto>(
            $"api/applications/{Esc(application)}/keys/{keyId}/strings/{Esc(language)}", ct);

    public Task<Result<TranslationStringDto>> UpsertStringAsync(
        string application, Guid keyId, string language, UpsertTranslationStringRequest request,
        CancellationToken ct = default) =>
        SendAsync<TranslationStringDto>(
            HttpMethod.Put,
            $"api/applications/{Esc(application)}/keys/{keyId}/strings/{Esc(language)}",
            request,
            ct);

    public Task<Result<TranslationStringDto>> ReviewStringAsync(
        string application, Guid keyId, string language, ReviewRequest request, CancellationToken ct = default) =>
        SendAsync<TranslationStringDto>(
            HttpMethod.Post,
            $"api/applications/{Esc(application)}/keys/{keyId}/strings/{Esc(language)}/review",
            request,
            ct);

    public Task<Result<PagedResult<TranslationStringDto>>> GetApplicationStringsAsync(
        string application, string? reviewState, int skip, int take, CancellationToken ct = default) =>
        GetAsync<PagedResult<TranslationStringDto>>(
            $"api/applications/{Esc(application)}/strings" + Query(
                ("reviewState", string.IsNullOrWhiteSpace(reviewState) ? null : reviewState),
                ("skip", skip.ToString()),
                ("take", take.ToString())),
            ct);

    // ---- Management ------------------------------------------------

    public Task<Result<PagedResult<TranslationRowDto>>> GetGridAsync(
        string? application, string? category, string? language, string? search,
        int skip, int take, string? status = null, CancellationToken ct = default) =>
        GetAsync<PagedResult<TranslationRowDto>>(
            "api/translations" + Query(
                ("application", application),
                ("category", string.IsNullOrWhiteSpace(category) ? null : category),
                ("language", string.IsNullOrWhiteSpace(language) ? null : language),
                ("search", string.IsNullOrWhiteSpace(search) ? null : search),
                ("status", string.IsNullOrWhiteSpace(status) ? null : status),
                ("skip", skip.ToString()),
                ("take", take.ToString())),
            ct);

    public Task<Result<IReadOnlyList<string>>> GetCategoriesAsync(
        string? application, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<string>>("api/categories" + Query(("application", application)), ct);

    public Task<Result<DashboardResponse>> GetDashboardAsync(
        string? application, CancellationToken ct = default) =>
        GetAsync<DashboardResponse>("api/dashboard" + Query(("application", application)), ct);

    public Task<Result<PagedResult<MissingTranslationDto>>> GetMissingAsync(
        string? application, string? language, int skip, int take, CancellationToken ct = default) =>
        GetAsync<PagedResult<MissingTranslationDto>>(
            "api/translations/missing" + Query(
                ("application", application),
                ("language", string.IsNullOrWhiteSpace(language) ? null : language),
                ("skip", skip.ToString()),
                ("take", take.ToString())),
            ct);

    public Task<Result<PublishTranslationsResult>> PublishAsync(
        string application, string? language, CancellationToken ct = default) =>
        SendAsync<PublishTranslationsResult>(
            HttpMethod.Post,
            "api/translations/publish",
            new PublishTranslationsRequest(application, string.IsNullOrWhiteSpace(language) ? null : language),
            ct);

    /// <summary>
    /// The pending delivery diff for a <c>(application, language)</c> pair — what a publish would
    /// add or change. <paramref name="language"/> is required by the server.
    /// </summary>
    public Task<Result<PublishPreviewResult>> GetPublishPreviewAsync(
        string application, string language, CancellationToken ct = default) =>
        GetAsync<PublishPreviewResult>(
            "api/translations/publish/preview" + Query(
                ("application", application),
                ("language", language)),
            ct);

    // ---- History / audit trail --------------------------------

    public Task<Result<PagedResult<AuditEntryDto>>> GetApplicationHistoryAsync(
        string application, int skip, int take, CancellationToken ct = default) =>
        GetAsync<PagedResult<AuditEntryDto>>(
            $"api/applications/{Esc(application)}/history" + Query(
                ("skip", skip.ToString()), ("take", take.ToString())),
            ct);

    public Task<Result<IReadOnlyList<AuditEntryDto>>> GetStringHistoryAsync(
        string application, Guid keyId, string language, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<AuditEntryDto>>(
            $"api/applications/{Esc(application)}/keys/{keyId}/strings/{Esc(language)}/history", ct);

    // ---- Client delivery ------------------------------------------

    /// <summary>
    /// Assemble-on-demand published map for one <c>(application, language)</c> pair, plus the
    /// strong <c>ETag</c> validator from the response header. No <c>If-None-Match</c> is sent,
    /// so a live map always comes back <c>200</c>; <c>404</c> when the pair is unknown/inactive.
    /// </summary>
    public async Task<Result<PublishedDelivery>> GetPublishedTranslationsAsync(
        string application, string language, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(
                $"api/translations/{Esc(application)}/{Esc(language)}", ct);
            if (!response.IsSuccessStatusCode)
            {
                return Result<PublishedDelivery>.Failure(await ReadErrorAsync(response, ct));
            }

            var body = await response.Content.ReadFromJsonAsync<PublishedTranslationsResponse>(JsonOptions, ct);
            return body is null
                ? Result<PublishedDelivery>.Failure(
                    ApiError.FromStatus((int)response.StatusCode, "The server returned an empty body."))
                : Result<PublishedDelivery>.Success(new PublishedDelivery(body, response.Headers.ETag?.Tag));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return Result<PublishedDelivery>.Failure(ApiError.Transport(ex.Message));
        }
    }

    // ---- transport --------------------------------------------------

    private static string Esc(string segment) => Uri.EscapeDataString(segment);

    private static string Query(params (string Key, string? Value)[] parts)
    {
        var pairs = parts
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}")
            .ToArray();
        return pairs.Length == 0 ? string.Empty : "?" + string.Join("&", pairs);
    }

    private async Task<Result<T>> GetAsync<T>(string uri, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(uri, ct);
            return await ReadAsync<T>(response, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return Result<T>.Failure(ApiError.Transport(ex.Message));
        }
    }

    private async Task<Result<T>> SendAsync<T>(HttpMethod method, string uri, object? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, mediaType: null, JsonOptions);
            }

            using var response = await http.SendAsync(request, ct);
            return await ReadAsync<T>(response, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return Result<T>.Failure(ApiError.Transport(ex.Message));
        }
    }

    private async Task<Result> SendAsync(HttpMethod method, string uri, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            using var response = await http.SendAsync(request, ct);
            return response.IsSuccessStatusCode
                ? Result.Success()
                : Result.Failure(await ReadErrorAsync(response, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return Result.Failure(ApiError.Transport(ex.Message));
        }
    }

    private async Task<Result<T>> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            return Result<T>.Failure(await ReadErrorAsync(response, ct));
        }

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return Result<T>.Failure(ApiError.FromStatus(204, "The server returned no content."));
        }

        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return value is null
            ? Result<T>.Failure(ApiError.FromStatus((int)response.StatusCode, "The server returned an empty body."))
            : Result<T>.Success(value);
    }

    private static async Task<ApiError> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(JsonOptions, ct);
            if (problem is not null && (problem.Title is not null || problem.Detail is not null))
            {
                return new ApiError(
                    problem.Status ?? status,
                    problem.Title ?? ApiError.FromStatus(status).Title,
                    problem.Detail);
            }
        }
        catch (JsonException)
        {
            // Not a problem+json body — fall through to a status-only error.
        }
        catch (NotSupportedException)
        {
            // Unexpected content type — fall through.
        }

        return ApiError.FromStatus(status);
    }

    private sealed record ProblemPayload
    {
        public string? Title { get; init; }
        public string? Detail { get; init; }
        public int? Status { get; init; }
    }
}
