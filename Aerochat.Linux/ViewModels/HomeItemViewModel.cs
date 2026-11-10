using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Aerochat.Linux.Services;

namespace Aerochat.Linux.ViewModels;

public sealed class HomeItemViewModel : ViewModelBase
{
    private Bitmap? _icon;
    private SceneViewModel _scene = SceneService.Default;

    public string Name { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string FallbackGlyph { get; init; } = "";
    public bool IsChannel { get; init; }
    public bool IsHeader { get; init; }
    public bool IsGuild { get; init; }
    public ulong ChannelId { get; init; }
    public bool IsDirectMessage { get; init; }
    public SceneViewModel Scene
    {
        get => _scene;
        set => SetProperty(ref _scene, value);
    }
    public bool ShowStatusDot { get; init; }
    public string StatusColor { get; init; } = "Transparent";
    public IBrush StatusBrush => new SolidColorBrush(Color.Parse(StatusColor));
    public string Status { get; init; } = "Offline";
    public bool IsGroupChat { get; init; }
    public string LastActivity { get; init; } = "";
    public bool IsUnread { get; init; }

    public ObservableCollection<HomeItemViewModel> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;

    public Bitmap? Icon
    {
        get => _icon;
        set
        {
            if (SetProperty(ref _icon, value))
            {
                Raise(nameof(HasIcon));
                Raise(nameof(HasAvatar));
            }
        }
    }

    public bool HasIcon => _icon is not null || !string.IsNullOrWhiteSpace(FallbackGlyph);
    public bool HasAvatar => _icon is not null;

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
    public bool ShowStatusIcon => IsDirectMessage || IsGroupChat;
    public bool ShowGuildIcon => IsGuild;
    public bool ShowChannelGlyph => IsChannel && !IsDirectMessage && !IsGroupChat;
    public double RowHeight => IsHeader ? 24 : 22;
    public double NameSize => IsHeader ? 15 : 12;
    public IBrush NameBrush => new SolidColorBrush(Color.Parse(IsHeader ? "#355A88" : "#333333"));

    public void AddChild(HomeItemViewModel child)
    {
        Children.Add(child);
        Raise(nameof(HasChildren));
    }

    public async Task LoadIconAsync()
    {
        var bitmap = await AvatarService.GetAsync(IconUrl);
        if (bitmap is not null)
            Icon = bitmap;
    }

    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "?";
        if (parts.Length == 1)
            return parts[0][..Math.Min(1, parts[0].Length)].ToUpperInvariant();

        return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }
}
