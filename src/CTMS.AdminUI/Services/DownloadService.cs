using Microsoft.JSInterop;

namespace CTMS.AdminUI.Services;

/// <summary>
/// Hands a byte payload to the browser as a file download. The bytes are streamed over the
/// Blazor circuit to <c>wwwroot/js/download.js</c>, which wraps them in a <c>Blob</c> and clicks
/// a temporary <c>&lt;a download&gt;</c> — so the download is same-origin with the Admin UI
/// regardless of where the API lives, and needs no CORS entry or client-side token.
/// </summary>
public sealed class DownloadService(IJSRuntime jsRuntime)
{
    /// <summary>Save <paramref name="bytes"/> in the browser as <paramref name="fileName"/>.</summary>
    public async Task SaveAsync(
        byte[] bytes, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var streamRef = new DotNetStreamReference(stream);
        await jsRuntime.InvokeVoidAsync(
            "ctmsDownload.save", cancellationToken, fileName, contentType, streamRef);
    }
}
