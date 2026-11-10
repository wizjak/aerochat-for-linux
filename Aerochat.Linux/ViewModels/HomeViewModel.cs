using System.Collections.ObjectModel;
using DSharpPlus;
using DSharpPlus.Entities;
using Aerochat.Linux.Services;

namespace Aerochat.Linux.ViewModels;

public sealed class HomeViewModel : ViewModelBase
{
    private UserViewModel? _currentUser;
    private SceneViewModel _scene = SceneService.Default;
    private string _selectedStatus = "Available";
    private string _searchText = "";
    private bool _isBusy;
    private string? _error;
    private bool _initializing = true;

    private readonly List<HomeItemViewModel> _dmItems = new();
    private readonly List<HomeItemViewModel> _guildItems = new();

    public UserViewModel? CurrentUser
    {
        get => _currentUser;
        private set => SetProperty(ref _currentUser, value);
    }

    public SceneViewModel Scene
    {
        get => _scene;
        set
        {
            if (!SetProperty(ref _scene, value))
                return;

            _ = value.LoadImageAsync();
        }
    }

    public ObservableCollection<string> StatusOptions { get; } =
        new(StatusHelper.Options.Select(option => option.Label));

    public string SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (!SetProperty(ref _selectedStatus, value) || _initializing)
                return;

            var status = StatusHelper.FromLabel(value);
            if (CurrentUser is not null)
                CurrentUser.Status = status;

            SettingsService.Current.LastStatus = value;
            SettingsService.Save();
            _ = App.Discord.UpdateStatusAsync(status);
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public ObservableCollection<HomeItemViewModel> Groups { get; } = new();

    public bool NotifyDm
    {
        get => SettingsService.Current.NotifyDm;
        set
        {
            if (SettingsService.Current.NotifyDm == value)
                return;

            SettingsService.Current.NotifyDm = value;
            SettingsService.Save();
            Raise();
        }
    }

    public bool NotifyMention
    {
        get => SettingsService.Current.NotifyMention;
        set
        {
            if (SettingsService.Current.NotifyMention == value)
                return;

            SettingsService.Current.NotifyMention = value;
            SettingsService.Save();
            Raise();
        }
    }

    public bool NotifyFriendOnline
    {
        get => SettingsService.Current.NotifyFriendOnline;
        set
        {
            if (SettingsService.Current.NotifyFriendOnline == value)
                return;

            SettingsService.Current.NotifyFriendOnline = value;
            SettingsService.Save();
            Raise();
        }
    }

    public bool PlayNotificationSounds
    {
        get => SettingsService.Current.PlayNotificationSounds;
        set
        {
            if (SettingsService.Current.PlayNotificationSounds == value)
                return;

            SettingsService.Current.PlayNotificationSounds = value;
            SettingsService.Save();
            Raise();
        }
    }

    public HomeItemViewModel? FindChannel(ulong channelId)
    {
        foreach (var item in _dmItems)
        {
            if (item.ChannelId == channelId)
                return item;
        }

        foreach (var guild in _guildItems)
        {
            foreach (var channel in guild.Children)
            {
                if (channel.ChannelId == channelId)
                    return channel;
            }
        }

        return null;
    }

    /// <summary>
    /// Applies a new scene to everything that follows the signed-in user's scene.
    /// One-to-one chats keep the other person's scene.
    /// </summary>
    public void ApplyScene(SceneViewModel scene)
    {
        Scene = scene;

        foreach (var item in _dmItems)
        {
            if (!item.IsDirectMessage)
                item.Scene = scene;
        }

        foreach (var guild in _guildItems)
        {
            guild.Scene = scene;
            foreach (var channel in guild.Children)
                channel.Scene = scene;
        }
    }

    public async Task RefreshAsync()
    {
        if (!App.Discord.IsReady || App.Discord.CurrentUser is null)
            return;

        IsBusy = true;
        Error = null;

        try
        {
            CurrentUser = UserViewModel.From(App.Discord.CurrentUser);
            _ = CurrentUser.LoadAvatarAsync();

            SceneService.InitializeForUser(App.Discord.CurrentUser);
            Scene = SceneService.Current;
            _ = Scene.LoadImageAsync();

            _initializing = true;
            _selectedStatus = StatusHelper.Label(StatusHelper.Of(App.Discord.CurrentUser));
            if (_selectedStatus == "Offline")
                _selectedStatus = SettingsService.Current.LastStatus;
            Raise(nameof(SelectedStatus));
            _initializing = false;

            BuildDmItems();
            BuildGuildItems();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BuildDmItems()
    {
        _dmItems.Clear();

        foreach (var channel in App.Discord.GetDmChannels())
        {
            var recipients = channel.Recipients?
                .Where(user => user.Id != App.Discord.CurrentUser?.Id)
                .ToList();
            var recipient = recipients?.FirstOrDefault();
            var isGroup = recipients is { Count: > 1 };

            string name;
            if (isGroup)
            {
                var names = recipients!.Take(3).Select(DisplayNameOf).ToList();
                name = string.Join(", ", names);
                if (recipients!.Count > 3)
                    name += $" +{recipients.Count - 3}";
            }
            else if (recipient is not null)
            {
                name = DisplayNameOf(recipient);
            }
            else
            {
                name = string.IsNullOrWhiteSpace(channel.Name) ? "Direct message" : channel.Name;
            }

            var item = new HomeItemViewModel
            {
                Name = name,
                Subtitle = isGroup ? "Group chat" : StatusHelper.Label(StatusHelper.Of(recipient)),
                IconUrl = recipient?.GetAvatarUrl(ImageFormat.Png, 64) ?? "",
                FallbackGlyph = HomeItemViewModel.Initials(name),
                IsChannel = true,
                ChannelId = channel.Id,
                IsDirectMessage = !isGroup && recipient is not null,
                IsGroupChat = isGroup,
                Status = isGroup ? "Available" : StatusHelper.Label(StatusHelper.Of(recipient)),
                Scene = isGroup ? SceneService.Current : SceneService.FromUser(recipient),
                ShowStatusDot = recipient is not null,
                StatusColor = StatusHelper.Color(StatusHelper.Of(recipient)),
                LastActivity = TimeAgo(channel.LastMessageId)
            };

            _ = item.LoadIconAsync();
            _dmItems.Add(item);

            if (item.IsDirectMessage && recipient is not null && recipient.BannerColor is null)
                _ = ResolveDirectMessageSceneAsync(item, recipient);
        }
    }

    private async Task ResolveDirectMessageSceneAsync(HomeItemViewModel item, DiscordUser recipient)
    {
        var profile = await App.Discord.GetUserProfileAsync(recipient.Id);
        if (profile?.BannerColor is null)
            return;

        var scene = SceneService.FromUser(profile);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!item.IsDirectMessage)
                return;

            item.Scene = scene;
        });
    }

    private void BuildGuildItems()
    {
        _guildItems.Clear();

        foreach (var guild in App.Discord.GetGuilds())
        {
            var channels = guild.Channels.Values
                .Where(channel => channel.Type is ChannelType.Text or ChannelType.Announcement)
                .OrderBy(channel => channel.Position)
                .ThenBy(channel => channel.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (channels.Count == 0)
                continue;

            var guildItem = new HomeItemViewModel
            {
                Name = guild.Name,
                Subtitle = $"{channels.Count} chat{(channels.Count == 1 ? "" : "s")}",
                IconUrl = guild.GetIconUrl(ImageFormat.Png, 64) ?? "",
                FallbackGlyph = HomeItemViewModel.Initials(guild.Name),
                IsChannel = false,
                IsGuild = true,
                Status = "Available",
                Scene = SceneService.Current
            };

            _ = guildItem.LoadIconAsync();

            foreach (var channel in channels)
            {
                guildItem.AddChild(new HomeItemViewModel
                {
                    Name = "#" + channel.Name,
                    Subtitle = "",
                    FallbackGlyph = "#",
                    IsChannel = true,
                    ChannelId = channel.Id,
                    Status = "Offline",
                    Scene = SceneService.Current,
                    StatusColor = "Transparent"
                });
            }

            _guildItems.Add(guildItem);
        }
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        Groups.Clear();

        var dmMatches = string.IsNullOrEmpty(query)
            ? _dmItems
            : _dmItems.Where(item =>
                item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        if (dmMatches.Count > 0)
        {
            var chats = new HomeItemViewModel { Name = "Chats", Subtitle = "", IsHeader = true };
            foreach (var item in dmMatches)
                chats.AddChild(item);
            Groups.Add(chats);
        }

        var guildMatches = new List<HomeItemViewModel>();
        foreach (var guild in _guildItems)
        {
            if (string.IsNullOrEmpty(query) ||
                guild.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                guildMatches.Add(guild);
                continue;
            }

            var channels = guild.Children
                .Where(channel => channel.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

            if (channels.Count == 0)
                continue;

            var filteredGuild = new HomeItemViewModel
            {
                Name = guild.Name,
                Subtitle = guild.Subtitle,
                IconUrl = guild.IconUrl,
                FallbackGlyph = guild.FallbackGlyph,
                Icon = guild.Icon,
                IsChannel = false,
                IsGuild = true,
                Status = guild.Status,
                Scene = guild.Scene
            };

            foreach (var channel in channels)
                filteredGuild.AddChild(channel);

            guildMatches.Add(filteredGuild);
        }

        if (guildMatches.Count > 0)
        {
            var servers = new HomeItemViewModel { Name = "Servers", Subtitle = "", IsHeader = true };
            foreach (var item in guildMatches)
                servers.AddChild(item);
            Groups.Add(servers);
        }
    }

    private static string DisplayNameOf(DiscordUser user) =>
        string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;

    private static string TimeAgo(ulong? snowflake)
    {
        if (snowflake is null or 0)
            return "";

        try
        {
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(
                (long)((snowflake.Value >> 22) + 1420070400000UL));
            var delta = DateTimeOffset.UtcNow - timestamp;

            if (delta < TimeSpan.FromMinutes(1)) return "now";
            if (delta < TimeSpan.FromHours(1)) return $"{delta.Minutes}m";
            if (delta < TimeSpan.FromDays(1)) return $"{delta.Hours}h";
            if (delta < TimeSpan.FromDays(7)) return $"{delta.Days}d";

            return timestamp.ToLocalTime().ToString("d MMM");
        }
        catch
        {
            return "";
        }
    }
}
