using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace Aerochat.Linux.Services;

public static class AvatarService
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Cache = new();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Aerochat/0.1");

        return client;
    }

    public static Task<Bitmap?> GetAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Task.FromResult<Bitmap?>(null);

        return Cache.GetOrAdd(url, DownloadAsync);
    }

    private static async Task<Bitmap?> DownloadAsync(string url)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}
