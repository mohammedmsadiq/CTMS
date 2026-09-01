// File download for the Admin UI. The bytes arrive over the Blazor circuit as a stream
// reference (see DownloadService) — no fetch, no Authorization header, no CORS: the download
// is same-origin with the Admin UI regardless of where the API lives.
window.ctmsDownload = {
    save: async function (fileName, contentType, streamRef) {
        const buffer = await streamRef.arrayBuffer();
        const blob = new Blob([buffer], { type: contentType || "application/octet-stream" });
        const objectUrl = URL.createObjectURL(blob);
        try {
            const anchor = document.createElement("a");
            anchor.href = objectUrl;
            anchor.download = fileName || "download";
            document.body.appendChild(anchor);
            anchor.click();
            anchor.remove();
        } finally {
            URL.revokeObjectURL(objectUrl);
        }
    }
};
