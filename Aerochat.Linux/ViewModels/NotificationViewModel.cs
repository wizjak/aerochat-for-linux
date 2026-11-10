using Avalonia.Media.Imaging;
using Aerochat.Linux.Services;

namespace Aerochat.Linux.ViewModels;

/// <summary>Data shown by a Windows Live Messenger style toast.</summary>
public sealed class NotificationViewModel : ViewModelBase
{
    private Bitmap? _avatar;

    public string AuthorName { get; init; } = "";
    public string Preview { get; init; } = "";
    public string ChannelTitle { get; init; } = "";
    public string AvatarUrl { get; init; } = "";
    public string Status { get; init; } = "Available";
    public bool IsSignOn { get; init; }
    public string HeaderText => "Windows Live Messenger";
    public string PreviewText => IsSignOn ? Preview : Preview.Length == 0 ? "(attachment)" : Preview;
    public string ContextText => IsSignOn
        ? "has just signed in."
        : string.IsNullOrWhiteSpace(ChannelTitle) ? "sent you a message" : $"in {ChannelTitle}";

    public Bitmap? Avatar
    {
        get => _avatar;
        private set => SetProperty(ref _avatar, value);
    }

    public async Task LoadAvatarAsync()
    {
        var bitmap = await AvatarService.GetAsync(AvatarUrl);
        if (bitmap is not null)
            Avatar = bitmap;
    }
}
