using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Aerochat.Linux.Services;
using Aerochat.Linux.ViewModels;
using Aerochat.Linux.Views;

namespace Aerochat.Linux;

public partial class App : Application
{
    private readonly Dictionary<ulong, ChatWindow> _openChats = new();

    /// <summary>Windows that the app itself is closing (sign out or session switch).</summary>
    private readonly HashSet<Window> _forceClose = new();

    private TrayIcon? _trayIcon;
    private bool _exiting;

    public static DiscordService Discord { get; private set; } = null!;
    public static App Instance => (App)Current!;

    public bool IsSignedIn { get; private set; }

    /// <summary>False when the desktop has no usable notification area.</summary>
    public bool HasTrayIcon => _trayIcon is not null;

    /// <summary>True once the user chose Exit; window close handlers stop hiding.</summary>
    public bool IsExiting => _exiting;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        SettingsService.Load();
        SceneService.Load();
        Discord = new DiscordService();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The app keeps running in the tray when the main window is hidden.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            CreateTrayIcon();

            var login = new LoginWindow();
            desktop.MainWindow = login;
            login.Show();

            if (SettingsService.Current.RememberMe &&
                !string.IsNullOrWhiteSpace(SettingsService.Current.Token))
            {
                _ = login.TryAutoLoginAsync();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTrayIcon()
    {
        try
        {
            var menu = new NativeMenu();

            var open = new NativeMenuItem("Open Windows Live Messenger");
            open.Click += (_, _) => ShowMainWindow();
            menu.Items.Add(open);

            menu.Items.Add(new NativeMenuItemSeparator());

            var exit = new NativeMenuItem("Exit Aerochat");
            exit.Click += (_, _) => Exit();
            menu.Items.Add(exit);

            _trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(
                    new Uri("avares://Aerochat/Assets/MainWnd.png"))),
                ToolTipText = "Aerochat - Windows Live Messenger",
                Menu = menu,
                IsVisible = true
            };

            _trayIcon.Clicked += (_, _) => ToggleMainWindow();
            TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
        }
        catch
        {
            // Some Linux desktops have no status-notifier host; the app still works
            // normally without the tray icon.
        }
    }

    /// <summary>Brings the current main window back from the tray.</summary>
    public void ShowMainWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var window = desktop.MainWindow;
        if (window is null)
            return;

        if (!window.IsVisible)
            window.Show();

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
    }

    private void ToggleMainWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var window = desktop.MainWindow;
        if (window is { IsVisible: true } && window.IsActive)
        {
            window.Hide();
            return;
        }

        ShowMainWindow();
    }

    /// <summary>Stops the application for real, ignoring the hide-to-tray behaviour.</summary>
    public void Exit()
    {
        if (_exiting)
            return;

        _exiting = true;
        _trayIcon?.Dispose();
        NotificationService.CloseAll();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    /// <summary>
    /// Window close handlers call this to know whether the close is a real one. The
    /// window is removed from the list so a later user close is hidden again.
    /// </summary>
    public bool CanClose(Window window) => _exiting || _forceClose.Remove(window);

    private void CloseForSwitch(Window? window)
    {
        if (window is null)
            return;

        _forceClose.Add(window);
        window.Close();
    }

    public void ShowHome()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        IsSignedIn = true;

        var old = desktop.MainWindow;
        var home = new HomeWindow();
        desktop.MainWindow = home;
        home.Show();
        CloseForSwitch(old);
    }

    public void ShowLogin()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        IsSignedIn = false;
        CloseAllChats();
        NotificationService.CloseAll();

        var old = desktop.MainWindow;
        var login = new LoginWindow();
        desktop.MainWindow = login;
        login.Show();
        CloseForSwitch(old);
    }

    public void OpenChat(ulong channelId, string title, string subtitle = "", string iconUrl = "",
        string fallbackGlyph = "#", SceneViewModel? scene = null, bool isDirectMessage = false)
    {
        if (_openChats.TryGetValue(channelId, out var existing))
        {
            existing.Activate();
            return;
        }

        var chat = new ChatWindow(channelId, title, subtitle, iconUrl, fallbackGlyph,
            scene ?? SceneService.Current, isDirectMessage);
        _openChats[channelId] = chat;
        chat.Closed += (_, _) => _openChats.Remove(channelId);
        chat.Show();
    }

    public bool IsChatOpen(ulong channelId) => _openChats.ContainsKey(channelId);

    public bool IsChatActive(ulong channelId) =>
        _openChats.TryGetValue(channelId, out var chat) && chat.IsActive;

    public void CloseAllChats()
    {
        foreach (var chat in _openChats.Values.ToList())
        {
            try
            {
                chat.Close();
            }
            catch
            {
                // Ignore windows that are already gone.
            }
        }

        _openChats.Clear();
    }
}
