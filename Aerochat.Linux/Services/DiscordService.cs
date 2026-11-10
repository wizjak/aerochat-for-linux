using System.Collections.Concurrent;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Enums;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;

namespace Aerochat.Linux.Services;

public sealed record LoginResult(bool Success, string? Error = null);

public sealed record ChannelPermissions(
    bool CanSend,
    bool CanAttach,
    bool CanReadHistory,
    string? SendBlockReason = null);

public sealed record OutgoingFile(string FileName, byte[] Data);

public sealed record SendResult(DiscordMessage? Message, string? Error = null);

public sealed class DiscordService
{
    private TaskCompletionSource<bool> _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<bool> _guildsTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<ulong, Task<DiscordUser?>> _profileTasks = new();

    public DiscordClient? Client { get; private set; }
    public DiscordUser? CurrentUser { get; private set; }
    public bool IsReady { get; private set; }

    public event Action? Ready;
    public event Action? GuildsChanged;
    public event Action<DiscordMessage>? MessageCreated;
    public event Action<DiscordMessage>? MessageUpdated;
    public event Action<DiscordMessage>? MessageDeleted;
    public event Action<DiscordChannel, DiscordUser>? TypingStarted;
    public event Action<DiscordUser, UserStatus>? PresenceUpdated;
    public event Action<DiscordUser, UserStatus>? FriendCameOnline;

    public async Task<LoginResult> LoginAsync(string token, UserStatus status)
    {
        await LogoutAsync();

        token = token.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return new LoginResult(false, "Paste your Discord token to continue.");

        var client = new DiscordClient(new DiscordConfiguration
        {
            Token = token,
            TokenType = TokenType.User,
            AutoReconnect = true
        });

        Client = client;
        _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _guildsTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Ready += OnReady;
        client.GuildDownloadCompleted += OnGuildsDownloaded;
        client.GuildCreated += OnGuildChanged;
        client.GuildAvailable += OnGuildChanged;
        client.GuildDeleted += OnGuildChanged;
        client.MessageCreated += OnMessageCreated;
        client.MessageUpdated += OnMessageUpdated;
        client.MessageDeleted += OnMessageDeleted;
        client.TypingStarted += OnTypingStarted;
        client.PresenceUpdated += OnPresenceUpdated;

        try
        {
            await client.ConnectAsync(status: status).WaitAsync(TimeSpan.FromSeconds(45));
            await _readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(20));

            // Channels and members arrive with GUILD_CREATE events after READY. Give them a
            // moment so the contact list is complete, but never block login on a slow guild.
            try
            {
                await _guildsTcs.Task.WaitAsync(TimeSpan.FromSeconds(8));
            }
            catch (TimeoutException)
            {
                // The tree will refresh again when the guild download completes.
            }

            return new LoginResult(true);
        }
        catch (UnauthorizedException)
        {
            await LogoutAsync();
            return new LoginResult(false, "Discord rejected that token. Make sure you copied it completely.");
        }
        catch (TimeoutException)
        {
            await LogoutAsync();
            return new LoginResult(false, "Discord took too long to answer. Check your connection and try again.");
        }
        catch (Exception ex)
        {
            await LogoutAsync();
            return new LoginResult(false, FriendlyError(ex));
        }
    }

    private static string FriendlyError(Exception ex)
    {
        var message = ex.Message;
        if (message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            return "Discord rejected that token.";

        if (message.Contains("403", StringComparison.OrdinalIgnoreCase))
            return "Discord refused the connection (403).";

        if (message.Contains("captcha", StringComparison.OrdinalIgnoreCase))
            return "Discord is asking for a captcha. Log in with the official client or browser first, then try again.";

        return string.IsNullOrWhiteSpace(message)
            ? "Could not connect to Discord."
            : message;
    }

    private bool IsCurrent(DiscordClient sender) => ReferenceEquals(sender, Client);

    private Task OnReady(DiscordClient sender, ReadyEventArgs args)
    {
        if (!IsCurrent(sender))
            return Task.CompletedTask;

        CurrentUser = sender.CurrentUser;
        IsReady = true;
        _readyTcs.TrySetResult(true);

        // The ready payload can arrive before the task above is awaited; events are
        // deliberately raised from the caller's thread and the UI marshals them.
        Ready?.Invoke();
        return Task.CompletedTask;
    }

    private Task OnGuildsDownloaded(DiscordClient sender, GuildDownloadCompletedEventArgs args)
    {
        if (!IsCurrent(sender))
            return Task.CompletedTask;

        _guildsTcs.TrySetResult(true);
        GuildsChanged?.Invoke();
        return Task.CompletedTask;
    }

    private Task OnGuildChanged(DiscordClient sender, GuildCreateEventArgs args)
    {
        if (IsCurrent(sender))
            GuildsChanged?.Invoke();
        return Task.CompletedTask;
    }

    private Task OnGuildChanged(DiscordClient sender, GuildDeleteEventArgs args)
    {
        if (IsCurrent(sender))
            GuildsChanged?.Invoke();
        return Task.CompletedTask;
    }

    private Task OnMessageCreated(DiscordClient sender, MessageCreateEventArgs args)
    {
        if (IsCurrent(sender))
            MessageCreated?.Invoke(args.Message);
        return Task.CompletedTask;
    }

    private Task OnMessageUpdated(DiscordClient sender, MessageUpdateEventArgs args)
    {
        if (IsCurrent(sender))
            MessageUpdated?.Invoke(args.Message);
        return Task.CompletedTask;
    }

    private Task OnMessageDeleted(DiscordClient sender, MessageDeleteEventArgs args)
    {
        if (IsCurrent(sender) && args.Message is not null)
            MessageDeleted?.Invoke(args.Message);
        return Task.CompletedTask;
    }

    private Task OnTypingStarted(DiscordClient sender, TypingStartEventArgs args)
    {
        if (IsCurrent(sender))
            TypingStarted?.Invoke(args.Channel, args.User);
        return Task.CompletedTask;
    }

    private Task OnPresenceUpdated(DiscordClient sender, PresenceUpdateEventArgs args)
    {
        if (!IsCurrent(sender))
            return Task.CompletedTask;

        PresenceUpdated?.Invoke(args.User, args.Status);

        var wasOffline = args.PresenceBefore is null || args.PresenceBefore.Status == UserStatus.Offline;
        if (wasOffline && args.Status != UserStatus.Offline &&
            sender.Relationships.TryGetValue(args.User.Id, out var relationship) &&
            relationship.RelationshipType == DiscordRelationshipType.Friend)
        {
            FriendCameOnline?.Invoke(args.User, args.Status);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<DiscordDmChannel> GetDmChannels()
    {
        if (Client is null)
            return Array.Empty<DiscordDmChannel>();

        return Client.PrivateChannels.Values
            .OrderByDescending(c => c.LastMessageId ?? 0)
            .ToList();
    }

    public IReadOnlyList<DiscordGuild> GetGuilds()
    {
        if (Client is null)
            return Array.Empty<DiscordGuild>();

        return Client.Guilds.Values
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<DiscordChannel?> GetChannelAsync(ulong channelId)
    {
        if (Client is null)
            return null;

        try
        {
            return await Client.GetChannelAsync(channelId);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<DiscordMessage>> GetMessagesAsync(ulong channelId, int limit = 50)
    {
        var channel = await GetChannelAsync(channelId);
        if (channel is null)
            return Array.Empty<DiscordMessage>();

        try
        {
            return await channel.GetMessagesAsync(limit);
        }
        catch
        {
            return Array.Empty<DiscordMessage>();
        }
    }

    /// <summary>
    /// Calculates what the signed-in user may do in a channel. Private channels use the
    /// same send/attach rules but Discord does not expose guild permission overwrites there.
    /// </summary>
    public async Task<ChannelPermissions> GetChannelPermissionsAsync(ulong channelId)
    {
        var channel = await GetChannelAsync(channelId);
        if (channel is null)
            return new ChannelPermissions(false, false, false, "This chat is no longer available.");

        if (channel.IsPrivate || channel.Guild is null)
            return new ChannelPermissions(true, true, true);

        var member = channel.Guild.CurrentMember;
        if (member is null)
            return new ChannelPermissions(false, false, false, "You are not a member of this server.");

        try
        {
            var permissions = channel.PermissionsFor(member);
            var sendPermission = channel.IsThread
                ? Permissions.SendMessagesInThreads
                : Permissions.SendMessages;

            var canSend = permissions.HasPermission(sendPermission);
            var canRead = permissions.HasPermission(Permissions.ReadMessageHistory);
            var canAttach = canSend && permissions.HasPermission(Permissions.AttachFiles);

            string? reason = null;
            if (!canSend)
                reason = "You do not have permission to send messages in this channel.";
            else if (!canRead)
                reason = "You do not have permission to read this channel's history.";

            return new ChannelPermissions(canSend, canAttach, canRead, reason);
        }
        catch
        {
            return new ChannelPermissions(false, false, false,
                "Aerochat could not verify your permissions for this channel.");
        }
    }

    public async Task<SendResult> SendMessageAsync(ulong channelId, string content,
        IReadOnlyCollection<OutgoingFile>? files = null)
    {
        var channel = await GetChannelAsync(channelId);
        if (channel is null)
            return new SendResult(null, "This chat is no longer available.");

        var streams = new List<MemoryStream>();

        try
        {
            var builder = new DiscordMessageBuilder().WithContent(content);

            if (files is { Count: > 0 })
            {
                foreach (var file in files)
                {
                    var stream = new MemoryStream(file.Data, writable: false);
                    streams.Add(stream);
                    builder.AddFile(file.FileName, stream);
                }
            }

            var message = await channel.SendMessageAsync(builder);
            return new SendResult(message);
        }
        catch (UnauthorizedException)
        {
            return new SendResult(null, "You do not have permission to send that message here.");
        }
        catch (RequestSizeException)
        {
            return new SendResult(null, "That attachment is too large for this channel.");
        }
        catch (BadRequestException ex)
        {
            return new SendResult(null, string.IsNullOrWhiteSpace(ex.JsonMessage)
                ? "Discord rejected the message."
                : ex.JsonMessage);
        }
        catch (Exception ex)
        {
            return new SendResult(null, FriendlyError(ex));
        }
        finally
        {
            foreach (var stream in streams)
                stream.Dispose();
        }
    }

    public async Task TriggerTypingAsync(ulong channelId)
    {
        var channel = await GetChannelAsync(channelId);
        if (channel is null)
            return;

        try
        {
            await channel.TriggerTypingAsync();
        }
        catch
        {
            // Typing indicators are cosmetic.
        }
    }

    public async Task UpdateStatusAsync(UserStatus status)
    {
        if (Client is null)
            return;

        try
        {
            await Client.UpdateStatusAsync(userStatus: status);
        }
        catch
        {
            // The next successful connection will resync the status.
        }
    }

    /// <summary>
    /// Fetches and caches a user profile so scenes can follow profile banner colors.
    /// </summary>
    public Task<DiscordUser?> GetUserProfileAsync(ulong userId)
    {
        if (Client is null)
            return Task.FromResult<DiscordUser?>(null);

        return _profileTasks.GetOrAdd(userId, FetchProfileAsync);
    }

    private async Task<DiscordUser?> FetchProfileAsync(ulong userId)
    {
        try
        {
            var client = Client;
            if (client is null)
                return null;

            var profile = await client.GetUserProfileAsync(userId).ConfigureAwait(false);
            return profile?.User;
        }
        catch
        {
            return null;
        }
    }

    public async Task LogoutAsync()
    {
        IsReady = false;
        CurrentUser = null;
        _profileTasks.Clear();

        var client = Client;
        Client = null;

        if (client is not null)
        {
            try
            {
                await client.DisconnectAsync();
            }
            catch
            {
                // Ignore teardown failures.
            }

            try
            {
                client.Dispose();
            }
            catch
            {
                // Ignore teardown failures.
            }
        }
    }
}
