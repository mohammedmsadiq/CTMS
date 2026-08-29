using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CTMS.Client.Internal;

namespace CTMS.Client.Caching;

/// <summary>
/// On-disk bundle cache: one JSON file per bundle at
/// <c>{root}/{projectId}/{cacheKey}.json</c>. Writes are atomic (temp file in the same directory
/// then a move/replace), and any unreadable or malformed file is treated as a miss.
/// </summary>
public sealed class FileBundleStore : IBundleStore
{
    private readonly string _root;

    public FileBundleStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A cache directory is required.", nameof(rootDirectory));
        }

        _root = Path.GetFullPath(rootDirectory);
    }

    /// <summary>Absolute root directory this store writes under.</summary>
    public string RootDirectory => _root;

    public Task<StoredBundle?> GetAsync(Guid projectId, string cacheKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = PathFor(projectId, cacheKey);

        try
        {
            if (!File.Exists(path))
            {
                return Task.FromResult<StoredBundle?>(null);
            }

            var bytes = File.ReadAllBytes(path);
            var stored = JsonSerializer.Deserialize<StoredBundle>(bytes, CtmsJson.Options);
            if (stored is null || string.IsNullOrEmpty(stored.Etag))
            {
                return Task.FromResult<StoredBundle?>(null);
            }

            return Task.FromResult<StoredBundle?>(stored);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // Corrupt / partially written / unreadable -> cache miss.
            return Task.FromResult<StoredBundle?>(null);
        }
    }

    public Task SetAsync(Guid projectId, string cacheKey, StoredBundle bundle, CancellationToken cancellationToken = default)
    {
        if (bundle is null)
        {
            throw new ArgumentNullException(nameof(bundle));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var path = PathFor(projectId, cacheKey);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory, string.Concat(Path.GetFileName(path), ".", Guid.NewGuid().ToString("N"), ".tmp"));
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(bundle, CtmsJson.Options);
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

        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid projectId, string cacheKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryDelete(PathFor(projectId, cacheKey));
        return Task.CompletedTask;
    }

    private string PathFor(Guid projectId, string cacheKey) =>
        Path.Combine(_root, projectId.ToString("D"), Sanitize(cacheKey) + ".json");

    private static string Sanitize(string cacheKey)
    {
        var chars = cacheKey.ToCharArray();
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
}
