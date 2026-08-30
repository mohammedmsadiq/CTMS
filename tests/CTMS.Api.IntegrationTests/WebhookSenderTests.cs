using System.Net;
using CTMS.Api.Webhooks;
using CTMS.Application.Webhooks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CTMS.Api.IntegrationTests;

/// <summary>
/// <see cref="WebhookSender"/> retry/backoff contract: a non-2xx or a transport error is a
/// failure, attempts are capped at <see cref="WebhookOptions.MaxAttempts"/>, and a give-up never
/// throws. Also checks the signed request shape.
/// </summary>
public sealed class WebhookSenderTests
{
    private const string Url = "https://example.test/hook";
    private const string Secret = "shh";
    private const string Body = """{"event":"published","application":"icoach","language":"fr-FR","etag":"abc","publishedAt":"2026-08-30T00:00:00.0000000Z"}""";

    private static WebhookSender SenderWith(CountingHandler handler, int maxAttempts = 3)
    {
        var options = Options.Create(new WebhookOptions
        {
            MaxAttempts = maxAttempts,
            RetryBackoff = [TimeSpan.Zero, TimeSpan.Zero],
        });
        return new WebhookSender(new HttpClient(handler), options, NullLogger<WebhookSender>.Instance);
    }

    [Fact]
    public async Task A_persistently_failing_endpoint_is_retried_then_dropped()
    {
        var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sender = SenderWith(handler);

        var delivered = await sender.SendAsync(Guid.NewGuid(), Url, Secret, Body, CancellationToken.None);

        Assert.False(delivered);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task A_transport_error_counts_as_a_failed_attempt_and_never_throws()
    {
        var handler = new CountingHandler(_ => throw new HttpRequestException("connection refused"));
        var sender = SenderWith(handler);

        var delivered = await sender.SendAsync(Guid.NewGuid(), Url, Secret, Body, CancellationToken.None);

        Assert.False(delivered);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task It_succeeds_as_soon_as_the_endpoint_returns_2xx()
    {
        var handler = new CountingHandler(call => new HttpResponseMessage(
            call < 3 ? HttpStatusCode.BadGateway : HttpStatusCode.NoContent));
        var sender = SenderWith(handler);

        var delivered = await sender.SendAsync(Guid.NewGuid(), Url, Secret, Body, CancellationToken.None);

        Assert.True(delivered);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task The_request_carries_the_signature_header_and_json_content_type()
    {
        HttpRequestMessage? seen = null;
        string? seenBody = null;
        var handler = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))
        {
            OnRequest = async req =>
            {
                seen = req;
                seenBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            },
        };
        var sender = SenderWith(handler);

        await sender.SendAsync(Guid.NewGuid(), Url, Secret, Body, CancellationToken.None);

        Assert.NotNull(seen);
        Assert.Equal(Body, seenBody);
        Assert.Equal("application/json", seen!.Content!.Headers.ContentType!.MediaType);
        Assert.True(seen.Headers.TryGetValues(WebhookSignature.HeaderName, out var sig));
        Assert.Equal(WebhookSignature.Compute(Secret, Body), Assert.Single(sig!));
    }

    private sealed class CountingHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public Func<HttpRequestMessage, Task>? OnRequest { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (OnRequest is not null)
            {
                await OnRequest(request);
            }

            return respond(Calls);
        }
    }
}
