using System.Collections.Concurrent;
using System.Diagnostics;

namespace Aerochat.Linux.Services;

/// <summary>
/// Downloads, caches and opens chat attachments. Keeping this separate from
/// <see cref="AvatarService"/> lets attachments use their original bytes (needed to
/// save/open a file) without polluting the avatar bitmap cache.
/// </summary>
public static class MediaService
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly ConcurrentDictionary<string, Task<byte[]?>> Cache = new();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Aerochat/0.2");
        return client;
    }

    public static Task<byte[]?> GetBytesAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Task.FromResult<byte[]?>(null);

        return Cache.GetOrAdd(url, DownloadAsync);
    }

    private static async Task<byte[]?> DownloadAsync(string url)
    {
        try
        {
            return await Http.GetByteArrayAsync(url).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public static string CacheDirectory
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            var root = string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache")
                : xdg;
            return Path.Combine(root, "aerochat", "attachments");
        }
    }

    /// <summary>Ensures an attachment is on disk and returns its local path.</summary>
    public static async Task<string?> DownloadToCacheAsync(string? url, string fileName, ulong id = 0)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var safeName = SanitizeFileName(fileName);
        var folder = Path.Combine(CacheDirectory, id == 0 ? "shared" : id.ToString());
        var path = Path.Combine(folder, safeName);

        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return path;

            var bytes = await GetBytesAsync(url).ConfigureAwait(false);
            if (bytes is null)
                return null;

            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Opens a file or URL with the desktop's default handler.</summary>
    public static bool Open(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch
        {
            // UseShellExecute can be unavailable in minimal sessions; xdg-open almost
            // always is. The target is passed as a separate argument, never a shell string.
            try
            {
                Process.Start(new ProcessStartInfo("xdg-open", target) { UseShellExecute = false });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static string SanitizeFileName(string name)
    {
        var safe = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(safe))
            safe = "attachment";

        foreach (var invalid in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '_');

        return safe.Length > 120 ? safe[^120..] : safe;
    }
}
