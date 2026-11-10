using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DSharpPlus;
using DSharpPlus.Entities;
using Aerochat.Linux.Services;

namespace Aerochat.Linux.ViewModels;

public sealed class MessageViewModel : ViewModelBase
{
    private static readonly string[] AuthorColors =
    {
        "#1F6FB2", "#A64B00", "#5C2D91", "#0B6E4F", "#B02A37", "#006D77", "#7A4E00"
    };

    private Bitmap? _avatar;

    public ulong Id { get; init; }
    public ulong AuthorId { get; init; }
    public string AuthorName { get; init; } = "";
    public string AuthorColor { get; init; } = "#1F6FB2";
    public IBrush AuthorBrush => new SolidColorBrush(Color.Parse(AuthorColor));
    public IBrush BubbleBrush { get; set; } = Brushes.Transparent;
    public string Content { get; init; } = "";
    public string Timestamp { get; init; } = "";
    public string FullTimestamp { get; init; } = "";
    public bool IsOwn { get; init; }
    public bool IsEdited { get; init; }
    public bool IsMentioned { get; init; }
    public string EditedSuffix => IsEdited ? " (edited)" : "";
    public string AvatarUrl { get; init; } = "";
    public string Initials { get; init; } = "?";
    public string Status { get; init; } = "Offline";
    public ObservableCollection<AttachmentViewModel> Attachments { get; } = new();
    public bool HasAttachments => Attachments.Count > 0;
    public bool HasContent => Content.Length > 0;

    public Bitmap? Avatar
    {
        get => _avatar;
        private set => SetProperty(ref _avatar, value);
    }

    public static MessageViewModel From(DiscordMessage message, ulong currentUserId)
    {
        var author = message.Author;
        var name = author is null
            ? "Unknown"
            : string.IsNullOrWhiteSpace(author.DisplayName) ? author.Username : author.DisplayName;

        var color = author is null
            ? AuthorColors[0]
            : AuthorColors[(int)(author.Id % (ulong)AuthorColors.Length)];

        var content = message.Content ?? "";

        if (message.MentionedUsers is not null)
        {
            foreach (var user in message.MentionedUsers)
                content = content
                    .Replace($"<@{user.Id}>", $"@{user.DisplayName}", StringComparison.Ordinal)
                    .Replace($"<@!{user.Id}>", $"@{user.DisplayName}", StringComparison.Ordinal);
        }

        if (message.MentionedChannels is not null)
        {
            foreach (var channel in message.MentionedChannels)
                content = content.Replace($"<#{channel.Id}>", $"#{channel.Name}", StringComparison.Ordinal);
        }

        var timestamp = message.Timestamp.ToLocalTime();

        var viewModel = new MessageViewModel
        {
            Id = message.Id,
            AuthorId = author?.Id ?? 0,
            AuthorName = name,
            AuthorColor = color,
            Content = content,
            Timestamp = timestamp.ToString("HH:mm"),
            FullTimestamp = timestamp.ToString("dddd, d MMMM yyyy HH:mm"),
            IsOwn = author?.Id == currentUserId,
            IsEdited = message.IsEdited,
            IsMentioned = message.MentionedUsers?.Any(user => user.Id == currentUserId) == true,
            AvatarUrl = author?.GetAvatarUrl(ImageFormat.Png, 64) ?? "",
            Initials = BuildInitials(name),
            Status = StatusHelper.Label(StatusHelper.Of(author))
        };

        if (message.Attachments is { Count: > 0 })
        {
            foreach (var attachment in message.Attachments)
                viewModel.Attachments.Add(AttachmentViewModel.From(attachment));
        }

        return viewModel;
    }

    public async Task LoadAttachmentsAsync()
    {
        foreach (var attachment in Attachments)
            await attachment.LoadBitmapAsync();
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
