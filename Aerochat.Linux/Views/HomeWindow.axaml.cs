using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Aerochat.Linux.Services;
using Aerochat.Linux.ViewModels;
using DSharpPlus;
using DSharpPlus.Entities;

namespace Aerochat.Linux.Views;

public partial class HomeWindow : Window
{
    private readonly HomeViewModel _viewModel = new();

    public HomeWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        Opened += async (_, _) => await LoadAsync();

        App.Discord.MessageCreated += OnMessageCreated;
        App.Discord.PresenceUpdated += OnPresenceUpdated;
        App.Discord.FriendCameOnline += OnFriendCameOnline;
        App.Discord.GuildsChanged += OnGuildsChanged;
        SceneService.CurrentChanged += OnSceneChanged;
        Closed += OnClosed;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Closing the contact list keeps Aerochat running in the notification area,
        // like Windows Live Messenger did.
        if (App.Instance.CanClose(this))
            return;

        if (!App.Instance.HasTrayIcon)
        {
            App.Instance.Exit();
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private async Task LoadAsync()
    {
        await _viewModel.RefreshAsync();

        if (_viewModel.Error is not null)
            FooterText.Text = _viewModel.Error;
        else if (_viewModel.CurrentUser is not null)
            FooterText.Text = $"Signed in as {_viewModel.CurrentUser.DisplayName}";
        else
            FooterText.Text = "Not connected";
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        App.Discord.MessageCreated -= OnMessageCreated;
        App.Discord.PresenceUpdated -= OnPresenceUpdated;
        App.Discord.FriendCameOnline -= OnFriendCameOnline;
        App.Discord.GuildsChanged -= OnGuildsChanged;
        SceneService.CurrentChanged -= OnSceneChanged;
    }

    private void OnSceneChanged(SceneViewModel scene)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _viewModel.ApplyScene(scene));
    }

    private void OnGuildsChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () => await LoadAsync());
    }

    private void OnMessageCreated(DiscordMessage message)
    {
        // Keep the footer useful without rebuilding the whole tree on every message.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel.CurrentUser is null)
                return;

            if (message.Channel is not null && message.Channel.IsPrivate)
            {
                var name = message.Author?.DisplayName ?? message.Author?.Username ?? "Someone";
                FooterText.Text = $"New message from {name}";
            }

            TryShowMessageNotification(message);
        });
    }

    private void TryShowMessageNotification(DiscordMessage message)
    {
        var current = App.Discord.CurrentUser;
        if (current is null || message.Author is null || message.Author.Id == current.Id)
            return;

        // The focused chat already shows the message; a toast there would be noise.
        if (App.Instance.IsChatActive(message.ChannelId))
            return;

        if (current.Presence?.Status == UserStatus.DoNotDisturb)
            return;

        var channel = message.Channel;
        if (channel is null)
            return;

        var isPrivate = channel.IsPrivate;
        var mentioned = message.MentionedUsers?.Any(user => user.Id == current.Id) == true ||
                        message.MentionEveryone;

        if (!mentioned && !isPrivate && message.MentionedRoles is { Count: > 0 })
        {
            var memberRoles = channel.Guild?.CurrentMember?.Roles;
            if (memberRoles is not null)
            {
                mentioned = message.MentionedRoles.Any(role =>
                    memberRoles.Any(memberRole => memberRole.Id == role.Id));
            }
        }

        var shouldNotify = (isPrivate && SettingsService.Current.NotifyDm) ||
                           (!isPrivate && mentioned && SettingsService.Current.NotifyMention);

        if (!shouldNotify)
            return;

        var authorName = string.IsNullOrWhiteSpace(message.Author.DisplayName)
            ? message.Author.Username
            : message.Author.DisplayName;

        var preview = BuildNotificationPreview(message);
        var channelTitle = isPrivate
            ? "Direct message"
            : "#" + (channel.Name ?? "channel");

        var item = _viewModel.FindChannel(message.ChannelId);
        Action open = () =>
        {
            if (item is not null)
            {
                App.Instance.OpenChat(item.ChannelId, item.Name, item.Subtitle, item.IconUrl,
                    item.FallbackGlyph, item.Scene, item.IsDirectMessage);
            }
            else
            {
                App.Instance.OpenChat(message.ChannelId, channelTitle);
            }
        };

        NotificationService.Show(new NotificationViewModel
        {
            AuthorName = authorName,
            Preview = preview,
            ChannelTitle = channelTitle,
            AvatarUrl = message.Author.GetAvatarUrl(ImageFormat.Png, 64),
            Status = StatusHelper.Label(StatusHelper.Of(message.Author))
        }, open);
    }

    private static string BuildNotificationPreview(DiscordMessage message)
    {
        var text = (message.Content ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();

        if (message.MentionedUsers is not null)
        {
            foreach (var user in message.MentionedUsers)
            {
                var name = string.IsNullOrWhiteSpace(user.DisplayName)
                    ? user.Username
                    : user.DisplayName;

                text = text
                    .Replace($"<@{user.Id}>", $"@{name}", StringComparison.Ordinal)
                    .Replace($"<@!{user.Id}>", $"@{name}", StringComparison.Ordinal);
            }
        }

        if (message.MentionedChannels is not null)
        {
            foreach (var channel in message.MentionedChannels)
                text = text.Replace($"<#{channel.Id}>", $"#{channel.Name}", StringComparison.Ordinal);
        }

        if (text.Length == 0 && message.Attachments is { Count: > 0 })
        {
            return message.Attachments.Count == 1
                ? "Sent an attachment."
                : $"Sent {message.Attachments.Count} attachments.";
        }

        if (text.Length > 150)
            text = text[..147] + "...";

        return text;
    }

    private void OnFriendCameOnline(DiscordUser user, UserStatus status)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!SettingsService.Current.NotifyFriendOnline)
                return;

            if (App.Discord.CurrentUser?.Presence?.Status == UserStatus.DoNotDisturb)
                return;

            var name = string.IsNullOrWhiteSpace(user.DisplayName)
                ? user.Username
                : user.DisplayName;

            var dm = App.Discord.GetDmChannels()
                .FirstOrDefault(c => c.Recipients?.Any(recipient => recipient.Id == user.Id) == true);

            Action? open = null;
            if (dm is not null)
            {
                var item = _viewModel.FindChannel(dm.Id);
                open = () => App.Instance.OpenChat(dm.Id,
                    item?.Name ?? name, item?.Subtitle ?? "", item?.IconUrl ?? "",
                    item?.FallbackGlyph ?? HomeItemViewModel.Initials(name),
                    item?.Scene, item?.IsDirectMessage ?? true);
            }

            NotificationService.Show(new NotificationViewModel
            {
                AuthorName = name,
                Preview = "has just signed in.",
                IsSignOn = true,
                AvatarUrl = user.GetAvatarUrl(ImageFormat.Png, 64),
                Status = StatusHelper.Label(status)
            }, open);
        });
    }

    private void OnPresenceUpdated(DiscordUser user, UserStatus status)
    {
        if (user.Id != App.Discord.CurrentUser?.Id)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel.CurrentUser is not null)
                _viewModel.CurrentUser.Status = status;
        });
    }

    private void Titlebar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Control control && control.FindAncestorOfType<Button>() is not null)
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Titlebar_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control control && control.FindAncestorOfType<Button>() is not null)
            return;

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Maximize_Click(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await LoadAsync();

    private void StatusMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string status })
            _viewModel.SelectedStatus = status;
    }

    private async void Scenes_Click(object? sender, RoutedEventArgs e)
    {
        var window = new ChangeSceneWindow();
        await window.ShowDialog(this);
    }

    private async void SignOut_Click(object? sender, RoutedEventArgs e)
    {
        await App.Discord.LogoutAsync();
        SettingsService.Current.Token = "";
        SettingsService.Save();
        App.Instance.ShowLogin();
    }

    private void ChatTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ChatTree.SelectedItem is not HomeItemViewModel { IsChannel: true } item)
            return;

        ChatTree.SelectedItem = null;
        App.Instance.OpenChat(item.ChannelId, item.Name, item.Subtitle, item.IconUrl,
            item.FallbackGlyph, item.Scene, item.IsDirectMessage);
    }
}
