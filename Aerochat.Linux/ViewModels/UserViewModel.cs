using Avalonia.Media;
using Avalonia.Media.Imaging;
using DSharpPlus;
using DSharpPlus.Entities;
using Aerochat.Linux.Services;

namespace Aerochat.Linux.ViewModels;

public sealed class UserViewModel : ViewModelBase
{
    private Bitmap? _avatar;
    private UserStatus _status;

    public ulong Id { get; init; }
    public string Username { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string AvatarUrl { get; init; } = "";
    public string Initials { get; init; } = "?";

    public Bitmap? Avatar
    {
        get => _avatar;
        private set => SetProperty(ref _avatar, value);
    }

    public UserStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                Raise(nameof(StatusLabel));
                Raise(nameof(StatusColor));
            }
        }
    }

    public string StatusLabel => StatusHelper.Label(Status);
    public string StatusColor => StatusHelper.Color(Status);
    public IBrush StatusBrush => new SolidColorBrush(Color.Parse(StatusColor));

    public static UserViewModel From(DiscordUser user)
    {
        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
        return new UserViewModel
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = name,
            AvatarUrl = user.GetAvatarUrl(ImageFormat.Png, 128),
            Initials = BuildInitials(name),
            Status = StatusHelper.Of(user)
        };
    }

    public async Task LoadAvatarAsync()
    {
        var bitmap = await AvatarService.GetAsync(AvatarUrl);
        if (bitmap is not null)
            Avatar = bitmap;
    }

    private static string BuildInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "?";
        if (parts.Length == 1)
            return parts[0][..Math.Min(1, parts[0].Length)].ToUpperInvariant();

        return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }
}
