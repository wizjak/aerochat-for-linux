using System.Text.Json;

namespace Aerochat.Linux.Services;

public sealed class AppSettings
{
    public string Token { get; set; } = "";
    public bool RememberMe { get; set; } = true;
    public string LastStatus { get; set; } = "Available";

    /// <summary>Scene chosen by the user. 0 means "use the Discord profile banner color".</summary>
    public int SceneId { get; set; }

    public bool NotifyDm { get; set; } = true;
    public bool NotifyMention { get; set; } = true;
    public bool NotifyFriendOnline { get; set; } = true;
    public bool PlayNotificationSounds { get; set; } = true;
}

public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings Current { get; private set; } = new();

    public static string ConfigDirectory
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdg))
                return Path.Combine(xdg, "aerochat");

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "aerochat");
        }
    }

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return;

            var json = File.ReadAllText(ConfigPath);
            Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(ConfigPath, json);

            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch
                {
                    // Best effort only.
                }
            }
        }
        catch
        {
            // Settings are a convenience; never crash the app over them.
        }
    }
}
