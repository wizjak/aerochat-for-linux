using System.Collections.Concurrent;
using System.Xml.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DSharpPlus.Entities;
using Aerochat.Linux.ViewModels;

namespace Aerochat.Linux.Services;

/// <summary>
/// Loads the WLM scene list and keeps track of the scene currently in use.
/// </summary>
public static class SceneService
{
    private const string ScenesXmlUri = "avares://Aerochat/Assets/Scenes/Scenes.xml";
    private const string ScenesBaseUri = "avares://Aerochat/Assets/Scenes/";

    private static readonly List<SceneViewModel> SceneList = new();
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> ImageCache = new();
    private static readonly ConcurrentDictionary<int, Task<Bitmap?>> ThumbnailCache = new();
    private static readonly SemaphoreSlim ThumbnailGate = new(4);

    private static readonly SceneViewModel Fallback = new(
        1, "default.png", "Default", "Windows Live Messenger Team", true, "#3bb2ea");

    private static bool _loaded;

    public static IReadOnlyList<SceneViewModel> Scenes => SceneList;
    public static SceneViewModel Default { get; private set; } = Fallback;
    public static SceneViewModel Current { get; private set; } = Fallback;

    public static event Action<SceneViewModel>? CurrentChanged;

    public static void Load()
    {
        if (_loaded)
            return;

        _loaded = true;

        try
        {
            using var stream = AssetLoader.Open(new Uri(ScenesXmlUri));
            var document = XDocument.Load(stream);

            foreach (var item in document.Descendants("item"))
            {
                var idText = item.Attribute("id")?.Value;
                var file = item.Attribute("file")?.Value;
                var name = item.Attribute("displayname")?.Value;
                var color = item.Attribute("color")?.Value;

                if (!int.TryParse(idText, out var id) ||
                    string.IsNullOrWhiteSpace(file) ||
                    string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(color))
                    continue;

                SceneList.Add(new SceneViewModel(
                    id,
                    file,
                    name,
                    item.Attribute("credit")?.Value ?? "",
                    bool.TryParse(item.Attribute("default")?.Value, out var isDefault) && isDefault,
                    color));
            }
        }
        catch
        {
            // Fall through to the built-in default scene.
        }

        if (SceneList.Count == 0)
            SceneList.Add(Fallback);

        Default = SceneList.FirstOrDefault(scene => scene.IsDefault) ?? SceneList[0];
        Current = Default;
    }

    public static SceneViewModel? ById(int id) =>
        SceneList.FirstOrDefault(scene => scene.Id == id);

    public static SceneViewModel FromUser(DiscordUser? user)
    {
        if (user?.BannerColor is { } banner)
        {
            var color = Color.FromRgb(banner.R, banner.G, banner.B);
            var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

            var match = SceneList.FirstOrDefault(scene =>
                scene.ColorHex.Equals(hex, StringComparison.OrdinalIgnoreCase));

            // Custom banner colors reuse the default scene artwork, tinted to the profile color.
            return match ?? Default.WithColor(color);
        }

        return Default;
    }

    /// <summary>
    /// Resolves the scene to use for the signed-in account. A manually chosen scene wins;
    /// otherwise the scene matching the Discord profile banner color is used.
    /// </summary>
    public static void InitializeForUser(DiscordUser? user)
    {
        var scene = ById(SettingsService.Current.SceneId) ?? FromUser(user);
        SetCurrent(scene);
    }

    public static void SetCurrent(SceneViewModel scene, bool persist = false)
    {
        Current = scene;

        if (persist)
        {
            SettingsService.Current.SceneId = scene.Id;
            SettingsService.Save();
        }

        CurrentChanged?.Invoke(scene);
    }

    public static void UseProfileScene(DiscordUser? user)
    {
        SettingsService.Current.SceneId = 0;
        SettingsService.Save();
        SetCurrent(FromUser(user));
    }

    public static Task<Bitmap?> GetImageAsync(SceneViewModel scene) =>
        ImageCache.GetOrAdd(scene.File, file => Task.Run(() => Decode(file, 0)));

    public static Task<Bitmap?> GetThumbnailAsync(SceneViewModel scene) =>
        ThumbnailCache.GetOrAdd(scene.Id, _ => Task.Run(async () =>
        {
            await ThumbnailGate.WaitAsync().ConfigureAwait(false);
            try
            {
                return Decode(scene.File, 160);
            }
            finally
            {
                ThumbnailGate.Release();
            }
        }));

    private static Bitmap? Decode(string file, int width)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(ScenesBaseUri + file));
            return width > 0 ? Bitmap.DecodeToWidth(stream, width) : new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}
