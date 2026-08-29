using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CTMS.AdminUI.ApiContracts;

namespace CTMS.AdminUI.Services;

/// <summary>
/// Typed wrapper over backend-core's <c>/api/*</c> HTTP surface. Every method returns a
/// <see cref="Result"/> / <see cref="Result{T}"/> so callers can render loading / error /
/// conflict states without catching exceptions. The underlying <see cref="HttpClient"/> is
/// supplied by <see cref="IHttpClientFactory"/> with its base address bound to the
/// <c>Ctms:ApiBaseUrl</c> configuration key.
/// </summary>
public sealed class CtmsApiClient(HttpClient http)
{
    public const string HttpClientName = "CtmsApi";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ---- Projects -------------------------------------------------------

    public Task<Result<IReadOnlyList<ProjectDto>>> GetProjectsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ProjectDto>>("api/projects", ct);

    public Task<Result<ProjectDto>> GetProjectAsync(Guid projectId, CancellationToken ct = default) =>
        GetAsync<ProjectDto>($"api/projects/{projectId}", ct);

    public Task<Result<ProjectDto>> CreateProjectAsync(CreateProjectRequest request, CancellationToken ct = default) =>
        SendAsync<ProjectDto>(HttpMethod.Post, "api/projects", request, ct);

    // ---- Locales ------------------------------------------------------

    public Task<Result<IReadOnlyList<LocaleDto>>> GetLocalesAsync(Guid projectId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<LocaleDto>>($"api/projects/{projectId}/locales", ct);

    public Task<Result<LocaleDto>> CreateLocaleAsync(Guid projectId, CreateLocaleRequest request, CancellationToken ct = default) =>
        SendAsync<LocaleDto>(HttpMethod.Post, $"api/projects/{projectId}/locales", request, ct);

    public Task<Result<LocaleDto>> UpdateLocaleAsync(Guid projectId, Guid localeId, UpdateLocaleRequest request, CancellationToken ct = default) =>
        SendAsync<LocaleDto>(HttpMethod.Patch, $"api/projects/{projectId}/locales/{localeId}", request, ct);

    public Task<Result> DeleteLocaleAsync(Guid projectId, Guid localeId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/projects/{projectId}/locales/{localeId}", ct);

    // ---- Translation keys -------------------------------------------

    public Task<Result<PagedResult<TranslationKeyDto>>> GetKeysAsync(Guid projectId, int skip, int take, CancellationToken ct = default) =>
        GetAsync<PagedResult<TranslationKeyDto>>($"api/projects/{projectId}/keys?skip={skip}&take={take}", ct);

    public Task<Result<TranslationKeyDto>> GetKeyAsync(Guid projectId, Guid keyId, CancellationToken ct = default) =>
        GetAsync<TranslationKeyDto>($"api/projects/{projectId}/keys/{keyId}", ct);

    public Task<Result<TranslationKeyDto>> CreateKeyAsync(Guid projectId, CreateTranslationKeyRequest request, CancellationToken ct = default) =>
        SendAsync<TranslationKeyDto>(HttpMethod.Post, $"api/projects/{projectId}/keys", request, ct);

    public Task<Result<TranslationKeyDto>> UpdateKeyAsync(Guid projectId, Guid keyId, UpdateTranslationKeyRequest request, CancellationToken ct = default) =>
        SendAsync<TranslationKeyDto>(HttpMethod.Patch, $"api/projects/{projectId}/keys/{keyId}", request, ct);

    public Task<Result> DeleteKeyAsync(Guid projectId, Guid keyId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/projects/{projectId}/keys/{keyId}", ct);

    // ---- Translation strings -------------------------------------

    public Task<Result<IReadOnlyList<TranslationStringDto>>> GetStringsAsync(Guid projectId, Guid keyId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<TranslationStringDto>>($"api/projects/{projectId}/keys/{keyId}/strings", ct);

    public Task<Result<TranslationStringDto>> GetStringAsync(Guid projectId, Guid keyId, Guid localeId, CancellationToken ct = default) =>
        GetAsync<TranslationStringDto>($"api/projects/{projectId}/keys/{keyId}/strings/{localeId}", ct);

    public Task<Result<TranslationStringDto>> UpsertStringAsync(
        Guid projectId, Guid keyId, Guid localeId, UpsertTranslationStringRequest request, CancellationToken ct = default) =>
        SendAsync<TranslationStringDto>(HttpMethod.Put, $"api/projects/{projectId}/keys/{keyId}/strings/{localeId}", request, ct);

    public Task<Result<TranslationStringDto>> ReviewStringAsync(
        Guid projectId, Guid keyId, Guid localeId, ReviewRequest request, CancellationToken ct = default) =>
        SendAsync<TranslationStringDto>(
            HttpMethod.Post, $"api/projects/{projectId}/keys/{keyId}/strings/{localeId}/review", request, ct);

    // ---- transport --------------------------------------------------

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

    private async Task<Result<T>> SendAsync<T>(HttpMethod method, string uri, object body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri)
            {
                Content = JsonContent.Create(body, mediaType: null, JsonOptions),
            };
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
            if (response.IsSuccessStatusCode)
            {
                return Result.Success();
            }

            return Result.Failure(await ReadErrorAsync(response, ct));
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
                    problem.Detail,
                    problem.CurrentVersion);
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

        [JsonPropertyName("currentVersion")]
        public long? CurrentVersion { get; init; }
    }
}
