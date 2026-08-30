using System.Threading.Channels;
using CTMS.Application.Webhooks;

namespace CTMS.Api.Webhooks;

/// <summary>
/// The bounded in-process queue between a publish request and the background dispatcher. Full
/// means a burst of publishes outran delivery; the oldest queued signal is dropped so the
/// request path never blocks.
/// </summary>
public sealed class WebhookChannel
{
    private readonly Channel<WebhookDelivery> _channel;

    public WebhookChannel(int capacity)
    {
        _channel = Channel.CreateBounded<WebhookDelivery>(new BoundedChannelOptions(Math.Max(1, capacity))
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    }

    public ChannelReader<WebhookDelivery> Reader => _channel.Reader;

    /// <summary>Non-blocking write. Returns <c>false</c> only if the channel is completed.</summary>
    public bool TryWrite(WebhookDelivery delivery) => _channel.Writer.TryWrite(delivery);
}
