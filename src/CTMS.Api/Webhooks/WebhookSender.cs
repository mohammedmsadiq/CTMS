using System.Net.Http.Headers;
using System.Text;
using CTMS.Application.Webhooks;
using Microsoft.Extensions.Options;

namespace CTMS.Api.Webhooks;

/// <summary>
/// POSTs a signed webhook body with bounded retry. A non-2xx response or a timeout is a failure;
/// after <see cref="WebhookOptions.MaxAttempts"/> attempts (with
/// <see cref="WebhookOptions.RetryBackoff"/> waits between) it gives up and returns <c>false</c>.
/// Never throws for a delivery failure.
/// </summary>
public sealed class WebhookSender
{
    private readonly HttpClient _http;
    private readonly WebhookOptions _options;
    private readonly ILogger<WebhookSender> _logger;

    public WebhookSender(HttpClient http, IOptions<WebhookOptions> options, ILogger<WebhookSender> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> SendAsync(
        Guid webhookId,
        string url,
        string secret,
        string rawBody,
        CancellationToken cancellationToken)
    {
        var signature = WebhookSignature.Compute(secret, rawBody);
        var attempts = Math.Max(1, _options.MaxAttempts);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var content = new StringContent(rawBody, Encoding.UTF8);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                request.Headers.TryAddWithoutValidation(WebhookSignature.HeaderName, signature);

                using var response = await _http.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                _logger.LogWarning(
                    "Webhook {WebhookId} attempt {Attempt}/{Attempts} to {Url} returned {StatusCode}.",
                    webhookId, attempt, attempts, url, (int)response.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                      && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Webhook {WebhookId} attempt {Attempt}/{Attempts} to {Url} failed.",
                    webhookId, attempt, attempts, url);
            }

            if (attempt < attempts)
            {
                try
                {
                    await Task.Delay(_options.BackoffBeforeAttempt(attempt + 1), cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    return false;
                }
            }
        }

        _logger.LogWarning(
            "Webhook {WebhookId} to {Url} gave up after {Attempts} attempts.",
            webhookId, url, attempts);
        return false;
    }
}
