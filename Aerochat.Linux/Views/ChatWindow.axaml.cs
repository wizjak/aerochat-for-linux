using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Aerochat.Linux.Services;
using Aerochat.Linux.ViewModels;
using DSharpPlus.Entities;

// Avalonia's legacy IDataObject drag API is still the desktop-supported path.
#pragma warning disable CS0618

namespace Aerochat.Linux.Views;

public partial class ChatWindow : Window
{
    private readonly ChatViewModel _viewModel;

    public ChatWindow(ulong channelId, string title, string subtitle, string iconUrl, string fallbackGlyph,
        SceneViewModel scene, bool isDirectMessage)
    {
        InitializeComponent();

        _viewModel = new ChatViewModel(channelId, title, subtitle, iconUrl, fallbackGlyph, scene, isDirectMessage);
        DataContext = _viewModel;

        App.Discord.MessageCreated += OnMessageCreated;
        App.Discord.MessageUpdated += OnMessageUpdated;
        App.Discord.MessageDeleted += OnMessageDeleted;
        App.Discord.TypingStarted += OnTypingStarted;
        SceneService.CurrentChanged += OnSceneChanged;
        _viewModel.ScrollToBottomRequested += ScrollToBottom;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, Chat_DragOver);
        AddHandler(DragDrop.DropEvent, Chat_Drop);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        Opened += async (_, _) =>
        {
            await _viewModel.LoadAsync();
            InputBox.Focus();
        };

        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        App.Discord.MessageCreated -= OnMessageCreated;
        App.Discord.MessageUpdated -= OnMessageUpdated;
        App.Discord.MessageDeleted -= OnMessageDeleted;
        App.Discord.TypingStarted -= OnTypingStarted;
        SceneService.CurrentChanged -= OnSceneChanged;
        _viewModel.ScrollToBottomRequested -= ScrollToBottom;
    }

    private void OnSceneChanged(SceneViewModel scene)
    {
        // One-to-one chats show the other person's scene, so leave those alone.
        if (_viewModel.IsDirectMessage)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() => _viewModel.Scene = scene);
    }

    private void OnMessageCreated(DiscordMessage message)
    {
        if (message.ChannelId != _viewModel.ChannelId)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() => _viewModel.AddMessage(message));
    }

    private void OnMessageUpdated(DiscordMessage message)
    {
        if (message.ChannelId != _viewModel.ChannelId)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() => _viewModel.UpdateMessage(message));
    }

    private void OnMessageDeleted(DiscordMessage message)
    {
        if (message.ChannelId != _viewModel.ChannelId)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() => _viewModel.DeleteMessage(message));
    }

    private void OnTypingStarted(DiscordChannel channel, DiscordUser user)
    {
        if (channel.Id != _viewModel.ChannelId)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() => _viewModel.ShowTyping(user));
    }

    private void ScrollToBottom()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(ScrollToBottomCore,
            Avalonia.Threading.DispatcherPriority.Background);

        // The extent grows after the item container is measured, so nudge once more.
        Avalonia.Threading.Dispatcher.UIThread.Post(ScrollToBottomCore,
            Avalonia.Threading.DispatcherPriority.ContextIdle);
    }

    private void ScrollToBottomCore()
    {
        var max = Math.Max(0, MessageScroll.Extent.Height - MessageScroll.Viewport.Height);
        MessageScroll.Offset = new Vector(MessageScroll.Offset.X, max);
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

    private async void Send_Click(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SendAsync();
        InputBox.Focus();
    }

    private async void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        e.Handled = true;
        await _viewModel.SendAsync();
        InputBox.Focus();
    }

    private async void Attach_Click(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanAttach)
        {
            _viewModel.ReportError("You do not have permission to attach files in this channel.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Attach files",
            AllowMultiple = true,
            FileTypeFilter = new[] { FilePickerFileTypes.All }
        });

        if (files is null || files.Count == 0)
            return;

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
                _viewModel.AddFile(path);
        }

        InputBox.Focus();
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+V from anywhere in the chat should attach a clipboard image, matching the
        // original client. The TextBox path is handled by PastingFromClipboard instead.
        if (e.Key != Key.V || !e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
            !_viewModel.CanAttach)
            return;

        if (FocusManager?.GetFocusedElement() is TextBox)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;

        try
        {
            var bitmap = await clipboard.TryGetBitmapAsync();
            if (bitmap is null)
                return;

            _viewModel.AddBitmap(bitmap);
            e.Handled = true;
        }
        catch
        {
            // Ignore unsupported clipboard backends.
        }
    }

    private async void InputBox_PastingFromClipboard(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanAttach)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;

        try
        {
            var files = await clipboard.TryGetFilesAsync();
            if (files is { Length: > 0 })
            {
                foreach (var file in files)
                {
                    var path = file.TryGetLocalPath();
                    if (!string.IsNullOrWhiteSpace(path))
                        _viewModel.AddFile(path);
                }

                e.Handled = true;
                return;
            }

            var bitmap = await clipboard.TryGetBitmapAsync();
            if (bitmap is not null)
            {
                _viewModel.AddBitmap(bitmap);
                e.Handled = true;
            }
        }
        catch
        {
            // Clipboard formats are best-effort on X11/Wayland; fall back to a normal paste.
        }
    }

    private void RemoveAttachment_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: AttachmentViewModel attachment })
            _viewModel.RemoveAttachment(attachment);
    }

    private async void Attachment_Click(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (sender is not Control { DataContext: AttachmentViewModel attachment })
            return;

        if (attachment.HasBitmap)
        {
            var viewer = new ImageViewWindow(attachment);
            viewer.Show(this);
            return;
        }

        var opened = await attachment.OpenExternallyAsync();
        if (!opened)
            _viewModel.ReportError($"Could not open \"{attachment.FileName}\".");
    }

    private void MessageRow_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Control { DataContext: MessageViewModel message })
            return;

        var menu = new ContextMenu();
        var copyMessage = new MenuItem { Header = "Copy message" };
        copyMessage.Click += async (_, _) => await CopyTextAsync(message.Content);
        menu.Items.Add(copyMessage);

        var copyAuthor = new MenuItem { Header = "Copy author name" };
        copyAuthor.Click += async (_, _) => await CopyTextAsync(message.AuthorName);
        menu.Items.Add(copyAuthor);

        menu.Open(sender as Control);
        e.Handled = true;
    }

    private async void Attachment_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Control { DataContext: AttachmentViewModel attachment })
            return;

        var menu = new ContextMenu();

        var open = new MenuItem { Header = "Open" };
        open.Click += async (_, _) =>
        {
            if (attachment.HasBitmap)
                new ImageViewWindow(attachment).Show(this);
            else
                await attachment.OpenExternallyAsync();
        };
        menu.Items.Add(open);

        var copyLink = new MenuItem { Header = "Copy link" };
        copyLink.Click += async (_, _) =>
            await CopyTextAsync(attachment.Url.Length > 0 ? attachment.Url : attachment.ProxyUrl);
        menu.Items.Add(copyLink);

        var saveAs = new MenuItem { Header = "Save as..." };
        saveAs.Click += async (_, _) => await SaveAttachmentAsync(attachment);
        menu.Items.Add(saveAs);

        menu.Open(sender as Control);
        e.Handled = true;
    }

    private async Task SaveAttachmentAsync(AttachmentViewModel attachment)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save attachment",
            SuggestedFileName = attachment.FileName
        });

        if (file is null)
            return;

        var bytes = await MediaService.GetBytesAsync(
            attachment.Url.Length > 0 ? attachment.Url : attachment.ProxyUrl);

        if (bytes is null)
        {
            _viewModel.ReportError($"Could not download \"{attachment.FileName}\".");
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            _viewModel.ReportError($"Could not save \"{attachment.FileName}\": {ex.Message}");
        }
    }

    private async Task CopyTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }

    private void Chat_DragOver(object? sender, DragEventArgs e)
    {
        if (!_viewModel.CanAttach)
            return;

        if (e.Data.Contains(DataFormats.Files) || e.Data.Contains(DataFormats.FileNames))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Chat_Drop(object? sender, DragEventArgs e)
    {
        if (!_viewModel.CanAttach)
            return;

        var files = e.Data.GetFiles();
        if (files is not null)
        {
            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(path))
                    _viewModel.AddFile(path);
            }

            e.Handled = true;
            InputBox.Focus();
            return;
        }

        var names = e.Data.GetFileNames();
        if (names is not null)
        {
            foreach (var path in names)
                _viewModel.AddFile(path);

            e.Handled = true;
            InputBox.Focus();
        }
    }
}
