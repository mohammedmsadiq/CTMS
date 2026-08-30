namespace CTMS.Api.Webhooks;

/// <summary>Bound from the <c>Webhooks</c> configuration section.</summary>
public sealed class WebhookOptions
{
    public const string SectionName = "Webhooks";

    /// <summary>Master switch. When <c>false</c> nothing is enqueued and no dispatcher runs.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Per-attempt HTTP timeout for a webhook callout. Default 5s.</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>Total delivery attempts before giving up (1 = no retry). Default 3.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Back-off before each retry. Index 0 is the wait before attempt 2, index 1 before attempt 3,
    /// and so on; the last entry is reused if there are more retries than entries.
    /// </summary>
    public IReadOnlyList<TimeSpan> RetryBackoff { get; set; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];

    /// <summary>Bounded capacity of the in-process delivery channel. Oldest-drop when full.</summary>
    public int ChannelCapacity { get; set; } = 1000;

    public TimeSpan BackoffBeforeAttempt(int attemptNumber)
    {
        // attemptNumber is 2-based for the first retry.
        var index = Math.Max(0, attemptNumber - 2);
        if (RetryBackoff.Count == 0)
        {
            return TimeSpan.Zero;
        }

        return index < RetryBackoff.Count ? RetryBackoff[index] : RetryBackoff[^1];
    }
}
