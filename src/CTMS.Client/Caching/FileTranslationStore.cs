using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CTMS.Client.Internal;

namespace CTMS.Client.Caching;

/// <summary>
/// On-disk translation cache. Per <c>(application, language)</c> it writes two files under
/// <c>{root}/{application}/</c>:
/// <list type="bullet">
/// <item><c>{language}.json</c> — the flat <c>{ "key": "value" }</c> map, directly consumable.</item>
/// <item><c>{language}.meta.json</c> — a sibling blob holding the <c>etag</c> and the
/// <c>retrievedAt</c> / <c>lastValidatedAt</c> timestamps.</item>
/// </list>
/// Both writes are atomic (temp file in the same directory then a move/replace). A miss, an
/// unreadable file, a malformed file or a missing sibling is treated as a cache miss, never an
/// exception; a write failure never breaks the caller.
/// </summary>
public sealed class FileTranslationStore : ITranslationStore
{
    private readonly string _root;

    public FileTranslationStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A cache directory is required.", nameof(rootDirectory));
        }

        _root = Path.GetFullPath(rootDirectory);
    }

    /// <summary>Absolute root directory this store writes under.</summary>
    public string RootDirectory => _root;

    public Task<StoredTranslations?> GetAsync(string application, string language, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dataPath = DataPath(application, language);
        var metaPath = MetaPath(application, language);

        try
        {
            if (!File.Exists(dataPath) || !File.Exists(metaPath))
            {
                return Task.FromResult<StoredTranslations?>(null);
            }

            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(dataPath), CtmsJson.Options);
            var meta = JsonSerializer.Deserialize<CacheMeta>(File.ReadAllBytes(metaPath), CtmsJson.Options);

            if (entries is null || meta is null || string.IsNullOrEmpty(meta.Etag))
            {
                return Task.FromResult<StoredTranslations?>(null);
            }

            var stored = new StoredTranslations
            {
                Application = string.IsNullOrEmpty(meta.Application) ? application : meta.Application!,
                Language = string.IsNullOrEmpty(meta.Language) ? language : meta.Language!,
                Entries = new Dictionary<string, string>(entries, StringComparer.Ordinal),
                Etag = meta.Etag!,
                RetrievedAt = meta.RetrievedAt,
                LastValidatedAt = meta.LastValidatedAt,
            };

            return Task.FromResult<StoredTranslations?>(stored);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // Corrupt / partially written / unreadable -> cache miss.
            return Task.FromResult<StoredTranslations?>(null);
        }
    }

    public Task SetAsync(string application, string language, StoredTranslations value, CancellationToken cancellationToken = default)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var dataPath = DataPath(application, language);
        var directory = Path.GetDirectoryName(dataPath)!;
        Directory.CreateDirectory(directory);

        var meta = new CacheMeta
        {
            Application = value.Application,
            Language = value.Language,
            Etag = value.Etag,
            RetrievedAt = value.RetrievedAt,
            LastValidatedAt = value.LastValidatedAt,
        };

        // Data first, then the sibling meta: a crash between the two leaves the pair incomplete,
        // which GetAsync reports as a miss.
        AtomicWrite(dataPath, JsonSerializer.SerializeToUtf8Bytes(value.Entries, CtmsJson.Options));
        AtomicWrite(MetaPath(application, language), JsonSerializer.SerializeToUtf8Bytes(meta, CtmsJson.Options));

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string application, string language, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryDelete(DataPath(application, language));
        TryDelete(MetaPath(application, language));
        return Task.CompletedTask;
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temp = Path.Combine(directory, string.Concat(Path.GetFileName(path), ".", Guid.NewGuid().ToString("N"), ".tmp"));
        try
        {
            File.WriteAllBytes(temp, bytes);

#if NET5_0_OR_GREATER
            File.Move(temp, path, overwrite: true);
#else
            if (File.Exists(path))
            {
                File.Replace(temp, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, path);
            }
#endif
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A cache write failure must never break the caller.
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private string DataPath(string application, string language) =>
        Path.Combine(_root, Sanitize(application), Sanitize(language) + ".json");

    private string MetaPath(string application, string language) =>
        Path.Combine(_root, Sanitize(application), Sanitize(language) + ".meta.json");

    private static string Sanitize(string segment)
    {
        var chars = segment.Trim().ToLowerInvariant().ToCharArray();
        var invalid = Path.GetInvalidFileNameChars();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private sealed class CacheMeta
    {
        public string? Application { get; set; }

        public string? Language { get; set; }

        public string? Etag { get; set; }

        public DateTimeOffset RetrievedAt { get; set; }

        public DateTimeOffset LastValidatedAt { get; set; }
    }
}
