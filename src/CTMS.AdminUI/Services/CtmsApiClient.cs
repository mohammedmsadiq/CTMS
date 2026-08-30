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

    // ---- Projects --------------------------------------------------

    public Task<Result<IReadOnlyList<ProjectDto>>> GetProjectsAsync(
        bool includeInactive = false, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ProjectDto>>(
            "api/projects" + Query(("includeInactive", includeInactive ? "true" : null)), ct);

    public Task<Result<ProjectDto>> GetProjectAsync(string code, CancellationToken ct = default) =>
        GetAsync<ProjectDto>($"api/projects/{Esc(code)}", ct);

    public Task<Result<ProjectDto>> CreateProjectAsync(
        CreateProjectRequest request, CancellationToken ct = default) =>
        SendAsync<ProjectDto>(HttpMethod.Post, "api/projects", request, ct);

    public Task<Result<ProjectDto>> UpdateProjectAsync(
        string code, UpdateProjectRequest request, CancellationToken ct = default) =>
        SendAsync<ProjectDto>(HttpMethod.Patch, $"api/projects/{Esc(code)}", request, ct);

    public Task<Result<ProjectDto>> EnableProjectLanguageAsync(
        string code, string language, CancellationToken ct = default) =>
        SendAsync<ProjectDto>(
            HttpMethod.Put, $"api/projects/{Esc(code)}/languages/{Esc(language)}", null, ct);

    public Task<Result<ProjectDto>> DisableProjectLanguageAsync(
        string code, string language, CancellationToken ct = default) =>
        SendAsync<ProjectDto>(
            HttpMethod.Delete, $"api/projects/{Esc(code)}/languages/{Esc(language)}", null, ct);

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

    /// <summary>Idempotently add a set of languages to the global catalogue.</summary>
    public Task<Result<BulkLanguagesResult>> BulkCreateLanguagesAsync(
        BulkLanguagesRequest request, CancellationToken ct = default) =>
        SendAsync<BulkLanguagesResult>(HttpMethod.Post, "api/languages/bulk", request, ct);

    // ---- Translation keys ---------------------------------------

    public Task<Result<PagedResult<TranslationKeyDto>>> GetKeysAsync(
        string project, string? category, int skip, int take, CancellationToken ct = default) =>
        GetAsync<PagedResult<TranslationKeyDto>>(
            $"api/projects/{Esc(project)}/keys" + Query(
                ("category", string.IsNullOrWhiteSpace(category) ? null : category),
                ("skip", skip.ToString()),
                ("take", take.ToString())),
            ct);

    public Task<Result<TranslationKeyDto>> GetKeyAsync(
        string project, Guid keyId, CancellationToken ct = default) =>
        GetAsync<TranslationKeyDto>($"api/projects/{Esc(project)}/keys/{keyId}", ct);

    public Task<Result<TranslationKeyDto>> CreateKeyAsync(
        string project, CreateTranslationKeyRequest request, CancellationToken ct = default) =>
        SendAsync<TranslationKeyDto>(HttpMethod.Post, $"api/projects/{Esc(project)}/keys", request, ct);

    public Task<Result<TranslationKeyDto>> UpdateKeyAsync(
        string project, Guid keyId, UpdateTranslationKeyRequest request, CancellationToken ct = default) =>
        SendAsync<TranslationKeyDto>(
            HttpMethod.Patch, $"api/projects/{Esc(project)}/keys/{keyId}", request, ct);

    public Task<Result> DeleteKeyAsync(string project, Guid keyId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/projects/{Esc(project)}/keys/{keyId}", ct);

    // ---- Import --------------------------------------------------

    /// <summary>
    /// Bulk-import translations for one language. <c>dryRun: true</c> returns a plan without
    /// persisting. A <c>400</c> carries the offending line in <see cref="ApiError.Detail"/>.
    /// </summary>
    public Task<Result<ImportTranslationsResult>> ImportTranslationsAsync(
        string project, ImportTranslationsRequest request, CancellationToken ct = default) =>
        SendAsync<ImportTranslationsResult>(
            HttpMethod.Post, $"api/projects/{Esc(project)}/import", request, ct);

    /// <summary>Apply one review verb to every string matching a filter (language / category / keyIds).</summary>
    public Task<Result<ReviewBulkResult>> ReviewBulkAsync(
        string project, ReviewBulkRequest request, CancellationToken ct = default) =>
        SendAsync<ReviewBulkResult>(
            HttpMethod.Post, $"api/projects/{Esc(project)}/review-bulk", request, ct);

    // ---- Translation strings -------------------------------

    public Task<Result<IReadOnlyList<TranslationStringDto>>> GetStringsForKeyAsync(
        string project, Guid keyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<TranslationStringDto>>(
            $"api/projects/{Esc(project)}/keys/{keyId}/strings", ct);

    public Task<Result<TranslationStringDto>> GetStringAsync(
        string project, Guid keyId, string language, CancellationToken ct = default) =>
        GetAsync<TranslationStringDto>(
            $"api/projects/{Esc(project)}/keys/{keyId}/strings/{Esc(language)}", ct);

    public Task<Result<TranslationStringDto>> UpsertStringAsync(
        string project, Guid keyId, string language, UpsertTranslationStringRequest request,
        CancellationToken ct = default) =>
        SendAsync<TranslationStringDto>(
            HttpMethod.Put,
            $"api/projects/{Esc(project)}/keys/{keyId}/strings/{Esc(language)}",
            request,
            ct);

    public Task<Result<TranslationStringDto>> ReviewStringAsync(
        string project, Guid keyId, string language, ReviewRequest request, CancellationToken ct = default) =>
        SendAsync<TranslationStringDto>(
            HttpMethod.Post,
            $"api/projects/{Esc(project)}/keys/{keyId}/strings/{Esc(language)}/review",
            request,
            ct);

    public Task<Result<PagedResult<TranslationStringDto>>> GetProjectStringsAsync(
        string project, string? reviewState, int skip, int take, CancellationToken ct = default) =>
        GetAsync<PagedResult<TranslationStringDto>>(
            $"api/projects/{Esc(project)}/strings" + Query(
                ("reviewState", string.IsNullOrWhiteSpace(reviewState) ? null : reviewState),
                ("skip", skip.ToString()),
                ("take", take.ToString())),
            ct);

    // ---- Management ------------------------------------------------

    public Task<Result<PagedResult<TranslationRowDto>>> GetGridAsync(
        string? project, string? category, string? language, string? search,
        int skip, int take, string? status = null, CancellationToken ct = default) =>
        GetAsync<PagedResult<TranslationRowDto>>(
            "api/translations" + Query(
                ("project", project),
                ("category", string.IsNullOrWhiteSpace(category) ? null : category),
                ("language", string.IsNullOrWhiteSpace(language) ? null : language),
                ("search", string.IsNullOrWhiteSpace(search) ? null : search),
                ("status", string.IsNullOrWhiteSpace(status) ? null : status),
                ("skip", skip.ToString()),
                ("take", take.ToString())),
            ct);

    public Task<Result<IReadOnlyList<string>>> GetCategoriesAsync(
        string? project, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<string>>("api/categories" + Query(("project", project)), ct);

    public Task<Result<DashboardResponse>> GetDashboardAsync(
        string? project, CancellationToken ct = default) =>
        GetAsync<DashboardResponse>("api/dashboard" + Query(("project", project)), ct);

    public Task<Result<PagedResult<MissingTranslationDto>>> GetMissingAsync(
        string? project, string? language, int skip, int take, CancellationToken ct = default) =>
        GetAsync<PagedResult<MissingTranslationDto>>(
            "api/translations/missing" + Query(
                ("project", project),
                ("language", string.IsNullOrWhiteSpace(language) ? null : language),
                ("skip", skip.ToString()),
                ("take", take.ToString())),
            ct);

    public Task<Result<PublishTranslationsResult>> PublishAsync(
        string project, string? language, CancellationToken ct = default) =>
        SendAsync<PublishTranslationsResult>(
            HttpMethod.Post,
            "api/translations/publish",
            new PublishTranslationsRequest(project, string.IsNullOrWhiteSpace(language) ? null : language),
            ct);

    /// <summary>
    /// The pending delivery diff for a <c>(project, language)</c> pair — what a publish would
    /// add or change. <paramref name="language"/> is required by the server.
    /// </summary>
    public Task<Result<PublishPreviewResult>> GetPublishPreviewAsync(
        string project, string language, CancellationToken ct = default) =>
        GetAsync<PublishPreviewResult>(
            "api/translations/publish/preview" + Query(
                ("project", project),
                ("language", language)),
            ct);

    // ---- History / audit trail --------------------------------

    public Task<Result<PagedResult<AuditEntryDto>>> GetProjectHistoryAsync(
        string project, int skip, int take, CancellationToken ct = default) =>
        GetAsync<PagedResult<AuditEntryDto>>(
            $"api/projects/{Esc(project)}/history" + Query(
                ("skip", skip.ToString()), ("take", take.ToString())),
            ct);

    public Task<Result<IReadOnlyList<AuditEntryDto>>> GetStringHistoryAsync(
        string project, Guid keyId, string language, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<AuditEntryDto>>(
            $"api/projects/{Esc(project)}/keys/{keyId}/strings/{Esc(language)}/history", ct);

    // ---- Client delivery ------------------------------------------

    /// <summary>
    /// Assemble-on-demand published map for one <c>(project, language)</c> pair, plus the
    /// strong <c>ETag</c> validator from the response header. No <c>If-None-Match</c> is sent,
    /// so a live map always comes back <c>200</c>; <c>404</c> when the pair is unknown/inactive.
    /// </summary>
    public async Task<Result<PublishedDelivery>> GetPublishedTranslationsAsync(
        string project, string language, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(
                $"api/translations/{Esc(project)}/{Esc(language)}", ct);
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
