using System.Diagnostics;
using Avalonia.Platform;

namespace Aerochat.Linux.Services;

/// <summary>
/// Plays the Aerochat notification sound without pulling in a media framework. The MP3 is
/// extracted from the single-file assets once and handed to the first available system
/// player. If none exists the app simply stays silent.
/// </summary>
public static class SoundService
{
    private static readonly SemaphoreSlim ExtractLock = new(1, 1);
    private static string? _extractedPath;

    public static void PlayNotification()
    {
        if (!SettingsService.Current.PlayNotificationSounds)
            return;

        _ = Task.Run(PlayNotificationCore);
    }

    private static async Task PlayNotificationCore()
    {
        try
        {
            var path = await EnsureSoundAsync().ConfigureAwait(false);
            if (path is null)
                return;

            var players = new (string Command, string[] Arguments)[]
            {
                ("mpv", new[] { "--no-video", "--really-quiet", path }),
                ("ffplay", new[] { "-nodisp", "-autoexit", "-loglevel", "quiet", path }),
                ("paplay", new[] { path }),
                ("pw-play", new[] { path }),
                ("cvlc", new[] { "--play-and-exit", "--intf", "dummy", path }),
                ("gst-play-1.0", new[] { "--quiet", path }),
                ("mpg123", new[] { "-q", path }),
                ("aplay", new[] { path })
            };

            foreach (var (command, arguments) in players)
            {
                var executable = Which(command);
                if (executable is null)
                    continue;

                try
                {
                    var info = new ProcessStartInfo(executable)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    foreach (var argument in arguments)
                        info.ArgumentList.Add(argument);

                    using var process = Process.Start(info);
                    return;
                }
                catch
                {
                    // Try the next player.
                }
            }
        }
        catch
        {
            // A missing sound is never worth interrupting chat.
        }
    }

    private static async Task<string?> EnsureSoundAsync()
    {
        if (_extractedPath is not null && File.Exists(_extractedPath))
            return _extractedPath;

        await ExtractLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_extractedPath is not null && File.Exists(_extractedPath))
                return _extractedPath;

            var cacheRoot = Path.GetDirectoryName(MediaService.CacheDirectory) ??
                            MediaService.CacheDirectory;
            var directory = Path.Combine(cacheRoot, "sounds");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "notify.mp3");

            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                await using var asset = AssetLoader.Open(
                    new Uri("avares://Aerochat/Assets/notify.mp3"));
                await using var file = File.Create(path);
                await asset.CopyToAsync(file).ConfigureAwait(false);
            }

            _extractedPath = path;
            return path;
        }
        catch
        {
            return null;
        }
        finally
        {
            ExtractLock.Release();
        }
    }

    private static string? Which(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, command);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }
}
