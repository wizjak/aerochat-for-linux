using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DSharpPlus.Entities;
using Aerochat.Linux.Services;

namespace Aerochat.Linux.ViewModels;

public sealed class ChatViewModel : ViewModelBase
{
    private const int MaxAttachments = 10;
    private const long MaxAttachmentBytes = 25L * 1024 * 1024;

    private readonly DispatcherTimer _typingTimer;
    private DateTimeOffset _lastTypingSent = DateTimeOffset.MinValue;
    private string _inputText = "";
    private string _typingText = "";
    private bool _isSending;
    private string? _error;
    private bool _loaded;
    private Bitmap? _icon;
    private SceneViewModel _scene = SceneService.Current;
    private bool _canSend = true;
    private bool _canAttach = true;
    private bool _canReadHistory = true;
    private string _composerHint = "Enter sends";
    private string _attachmentError = "";

    public ChatViewModel(ulong channelId, string title, string subtitle = "", string iconUrl = "",
        string fallbackGlyph = "#", SceneViewModel? scene = null, bool isDirectMessage = false)
    {
        ChannelId = channelId;
        Title = title;
        Subtitle = subtitle;
        IconUrl = iconUrl;
        FallbackGlyph = fallbackGlyph;
        IsDirectMessage = isDirectMessage;
        Scene = scene ?? SceneService.Current;
        _ = Scene.LoadImageAsync();

        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _typingTimer.Tick += (_, _) =>
        {
            _typingTimer.Stop();
            TypingText = "";
        };

        if (!string.IsNullOrWhiteSpace(iconUrl))
            _ = LoadIconAsync();
    }

    public ulong ChannelId { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string IconUrl { get; }
    public string FallbackGlyph { get; }
    public bool IsDirectMessage { get; }
    public string RecipientStatus { get; init; } = "Available";

    public SceneViewModel Scene
    {
        get => _scene;
        set
        {
            if (!SetProperty(ref _scene, value))
                return;

            foreach (var message in Messages)
                message.BubbleBrush = message.IsOwn ? value.OwnMessageBrush : Avalonia.Media.Brushes.Transparent;

            _ = value.LoadImageAsync();
        }
    }

    public Bitmap? Icon
    {
        get => _icon;
        private set => SetProperty(ref _icon, value);
    }

    public async Task LoadIconAsync()
    {
        var bitmap = await AvatarService.GetAsync(IconUrl);
        if (bitmap is not null)
            Icon = bitmap;
    }

    public ObservableCollection<MessageViewModel> Messages { get; } = new();
    public ObservableCollection<AttachmentViewModel> PendingAttachments { get; } = new();

    public bool HasAttachments => PendingAttachments.Count > 0;

    public string InputText
    {
        get => _inputText;
        set
        {
            if (!SetProperty(ref _inputText, value))
                return;

            if (string.IsNullOrWhiteSpace(value) || !CanSend)
                return;

            var now = DateTimeOffset.UtcNow;
            if (now - _lastTypingSent < TimeSpan.FromSeconds(5))
                return;

            _lastTypingSent = now;
            _ = App.Discord.TriggerTypingAsync(ChannelId);
        }
    }

    public string TypingText
    {
        get => _typingText;
        private set => SetProperty(ref _typingText, value);
    }

    public bool IsSending
    {
        get => _isSending;
        private set => SetProperty(ref _isSending, value);
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public string AttachmentError
    {
        get => _attachmentError;
        private set => SetProperty(ref _attachmentError, value);
    }

    public bool IsLoaded
    {
        get => _loaded;
        private set => SetProperty(ref _loaded, value);
    }

    public bool CanSend
    {
        get => _canSend;
        private set
        {
            if (!SetProperty(ref _canSend, value))
                return;

            Raise(nameof(IsComposerEnabled));
        }
    }

    public bool CanAttach
    {
        get => _canAttach;
        private set => SetProperty(ref _canAttach, value);
    }

    public bool CanReadHistory
    {
        get => _canReadHistory;
        private set => SetProperty(ref _canReadHistory, value);
    }

    public bool IsComposerEnabled => CanSend;

    public string ComposerHint
    {
        get => _composerHint;
        private set => SetProperty(ref _composerHint, value);
    }

    public event Action? ScrollToBottomRequested;

    public async Task LoadAsync()
    {
        if (IsLoaded)
            return;

        IsLoaded = true;
        await RefreshPermissionsAsync();

        if (!CanReadHistory)
        {
            Error = ComposerHint;
            return;
        }

        var messages = await App.Discord.GetMessagesAsync(ChannelId, 100);

        foreach (var message in messages.OrderBy(message => message.Timestamp))
            AddMessage(message);

        ScrollToBottomRequested?.Invoke();
    }

    public async Task RefreshPermissionsAsync()
    {
        var permissions = await App.Discord.GetChannelPermissionsAsync(ChannelId);
        CanSend = permissions.CanSend;
        CanAttach = permissions.CanAttach;
        CanReadHistory = permissions.CanReadHistory;

        if (!permissions.CanSend)
            ComposerHint = permissions.SendBlockReason ?? "You cannot send messages here.";
        else if (!permissions.CanReadHistory)
            ComposerHint = permissions.SendBlockReason ?? "You cannot read this channel's history.";
        else
            ComposerHint = "Enter sends";
    }

    public async Task SendAsync()
    {
        var text = InputText.Trim();
        if ((text.Length == 0 && PendingAttachments.Count == 0) || IsSending)
            return;

        if (!CanSend)
        {
            Error = ComposerHint;
            return;
        }

        if (PendingAttachments.Count > 0 && !CanAttach)
        {
            Error = "You do not have permission to attach files in this channel.";
            return;
        }

        var snapshot = PendingAttachments.ToList();
        var files = snapshot
            .Where(attachment => attachment.PendingData is not null)
            .Select(attachment => new OutgoingFile(attachment.FileName, attachment.PendingData!))
            .ToList();

        IsSending = true;
        Error = null;
        AttachmentError = "";
        InputText = "";
        PendingAttachments.Clear();
        Raise(nameof(HasAttachments));

        var result = await App.Discord.SendMessageAsync(ChannelId, text, files);
        IsSending = false;

        if (result.Message is null)
        {
            Error = result.Error ?? "The message could not be sent.";
            InputText = text;
            foreach (var attachment in snapshot)
                PendingAttachments.Add(attachment);
            Raise(nameof(HasAttachments));
            return;
        }

        AddMessage(result.Message);
    }

    public bool AddFile(string path)
    {
        if (!CanAttach)
        {
            AttachmentError = "You do not have permission to attach files in this channel.";
            return false;
        }

        if (PendingAttachments.Count >= MaxAttachments)
        {
            AttachmentError = $"You may only attach up to {MaxAttachments} files to one message.";
            return false;
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                AttachmentError = $"Could not find \"{Path.GetFileName(path)}\".";
                return false;
            }

            if (info.Length > MaxAttachmentBytes)
            {
                AttachmentError = $"\"{info.Name}\" is larger than 25 MB.";
                return false;
            }

            var bytes = File.ReadAllBytes(path);
            Bitmap? preview = null;
            if (AttachmentViewModel.LooksLikeImage(path))
            {
                try
                {
                    using var stream = new MemoryStream(bytes);
                    preview = new Bitmap(stream);
                }
                catch
                {
                    preview = null;
                }
            }

            PendingAttachments.Add(AttachmentViewModel.Pending(info.Name, bytes, preview,
                PendingAttachments.Count));
            AttachmentError = "";
            Raise(nameof(HasAttachments));
            return true;
        }
        catch (Exception ex)
        {
            AttachmentError = $"Could not attach \"{Path.GetFileName(path)}\": {ex.Message}";
            return false;
        }
    }

    public bool AddBitmap(Bitmap bitmap, string? name = null)
    {
        if (!CanAttach)
        {
            AttachmentError = "You do not have permission to attach files in this channel.";
            return false;
        }

        if (PendingAttachments.Count >= MaxAttachments)
        {
            AttachmentError = $"You may only attach up to {MaxAttachments} files to one message.";
            return false;
        }

        try
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            var bytes = stream.ToArray();
            var fileName = name ?? $"image-{DateTime.Now:yyyyMMdd-HHmmss}.png";

            PendingAttachments.Add(AttachmentViewModel.Pending(fileName, bytes, bitmap,
                PendingAttachments.Count));
            AttachmentError = "";
            Raise(nameof(HasAttachments));
            return true;
        }
        catch (Exception ex)
        {
            AttachmentError = $"Could not paste that image: {ex.Message}";
            return false;
        }
    }

    public void ReportError(string message) => Error = message;

    public void RemoveAttachment(AttachmentViewModel attachment)
    {
        if (!PendingAttachments.Remove(attachment))
            return;

        for (var i = 0; i < PendingAttachments.Count; i++)
            PendingAttachments[i].PendingIndex = i;

        AttachmentError = "";
        Raise(nameof(HasAttachments));
    }

    public void ClearAttachments()
    {
        PendingAttachments.Clear();
        AttachmentError = "";
        Raise(nameof(HasAttachments));
    }

    public void AddMessage(DiscordMessage message)
    {
        if (Messages.Any(existing => existing.Id == message.Id))
            return;

        var viewModel = MessageViewModel.From(message, App.Discord.CurrentUser?.Id ?? 0);
        PrepareMessage(viewModel);
        Messages.Add(viewModel);

        while (Messages.Count > 250)
            Messages.RemoveAt(0);

        ScrollToBottomRequested?.Invoke();
    }

    public void UpdateMessage(DiscordMessage message)
    {
        var index = -1;
        for (var i = 0; i < Messages.Count; i++)
        {
            if (Messages[i].Id != message.Id)
                continue;

            index = i;
            break;
        }

        if (index < 0)
            return;

        var viewModel = MessageViewModel.From(message, App.Discord.CurrentUser?.Id ?? 0);
        PrepareMessage(viewModel);
        Messages[index] = viewModel;
    }

    private void PrepareMessage(MessageViewModel viewModel)
    {
        viewModel.BubbleBrush = viewModel.IsOwn ? Scene.OwnMessageBrush : Avalonia.Media.Brushes.Transparent;
        _ = viewModel.LoadAvatarAsync();
        _ = viewModel.LoadAttachmentsAsync();
    }

    public void DeleteMessage(DiscordMessage message)
    {
        for (var i = 0; i < Messages.Count; i++)
        {
            if (Messages[i].Id != message.Id)
                continue;

            Messages.RemoveAt(i);
            return;
        }
    }

    public void ShowTyping(DiscordUser user)
    {
        if (user.Id == App.Discord.CurrentUser?.Id || !CanSend)
            return;

        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
        TypingText = $"{name} is typing...";
        _typingTimer.Stop();
        _typingTimer.Start();
    }
}
