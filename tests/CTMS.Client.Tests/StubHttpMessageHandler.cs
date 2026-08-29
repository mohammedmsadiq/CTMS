using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CTMS.Client.Tests;

/// <summary>
/// Deterministic <see cref="HttpMessageHandler"/> for the SDK tests: a queue of per-call
/// responders plus a record of every request (method, URI, and the <c>If-None-Match</c> values as
/// they were at send time).
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();

    public List<RecordedRequest> Requests { get; } = new();

    public int CallCount => Requests.Count;

    public Func<HttpRequestMessage, HttpResponseMessage>? Fallback { get; set; }

    public StubHttpMessageHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responders.Enqueue(responder);
        return this;
    }

    public StubHttpMessageHandler EnqueueThrow(Exception exception) =>
        Enqueue(_ => throw exception);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.IfNoneMatch.Select(t => t.Tag ?? string.Empty).ToArray()));

        var responder = _responders.Count > 0
            ? _responders.Dequeue()
            : Fallback ?? throw new InvalidOperationException($"No stub responder for {request.RequestUri}");

        return Task.FromResult(responder(request));
    }

    // --- response builders -------------------------------------------------

    public static string ComputeEtag(IReadOnlyDictionary<string, string> entries)
    {
        var builder = new StringBuilder();
        foreach (var pair in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key).Append('\n').Append(pair.Value).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    public static HttpResponseMessage BundleOk(
        Guid projectId,
        string localeCode,
        int version,
        IReadOnlyDictionary<string, string> entries,
        string? etag = null)
    {
        etag ??= ComputeEtag(entries);
        var payload = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid(),
            projectId,
            localeCode,
            version,
            entries,
            etag,
            createdBy = "tester",
            createdAt = DateTime.UtcNow,
        });

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        response.Headers.ETag = new EntityTagHeaderValue($"\"{etag}\"");
        response.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return response;
    }

    public static HttpResponseMessage NotModified(string etag)
    {
        // A body that would blow up System.Text.Json if the SDK tried to parse it.
        var response = new HttpResponseMessage(HttpStatusCode.NotModified)
        {
            Content = new StringContent("<<not json>>", Encoding.UTF8, "application/json"),
        };
        response.Headers.ETag = new EntityTagHeaderValue($"\"{etag}\"");
        return response;
    }

    public static HttpResponseMessage Problem(HttpStatusCode status, string title, string detail)
    {
        var payload = JsonSerializer.Serialize(new { title, detail, status = (int)status });
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/problem+json"),
        };
    }

    internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string[] IfNoneMatch);
}
