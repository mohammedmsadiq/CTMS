using CTMS.Application.Webhooks;

namespace CTMS.Application.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IWebhookPublisher"/> that records every enqueue as a flat list of
/// <c>(application, language)</c> pairs so a test can assert exactly what a publish would push.
/// </summary>
public sealed class RecordingWebhookPublisher : IWebhookPublisher
{
    private readonly List<(string Application, string Language)> _enqueued = [];

    public IReadOnlyList<(string Application, string Language)> Enqueued => _enqueued;

    public void Enqueue(string application, IEnumerable<string> languages)
    {
        foreach (var language in languages)
        {
            _enqueued.Add((application, language));
        }
    }
}
