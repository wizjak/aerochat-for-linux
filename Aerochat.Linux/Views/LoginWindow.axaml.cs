using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Aerochat.Linux.Services;
using Aerochat.Linux.ViewModels;

namespace Aerochat.Linux.Views;

public partial class LoginWindow : Window
{
    private bool _busy;

    public SceneViewModel Scene { get; }

    public LoginWindow()
    {
        InitializeComponent();

        Scene = SceneService.Default;
        DataContext = this;
        _ = Scene.LoadImageAsync();

        StatusBox.ItemsSource = StatusHelper.Options.Select(option => option.Label).ToList();
        StatusBox.SelectedItem = StatusHelper.Label(StatusHelper.FromLabel(SettingsService.Current.LastStatus));

        if (!string.IsNullOrWhiteSpace(SettingsService.Current.Token))
            TokenBox.Text = SettingsService.Current.Token;

        RememberBox.IsChecked = SettingsService.Current.RememberMe;

        Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Without a signed-in session there is nothing useful in the tray, so closing
        // the login window quits Aerochat.
        if (App.Instance.CanClose(this))
            return;

        App.Instance.Exit();
    }

    public async Task TryAutoLoginAsync()
    {
        if (string.IsNullOrWhiteSpace(SettingsService.Current.Token))
            return;

        StatusBox.SelectedItem = StatusHelper.Label(StatusHelper.FromLabel(SettingsService.Current.LastStatus));
        await SignInAsync();
    }

    private void Titlebar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Control control && control.FindAncestorOfType<Button>() is not null)
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private async void SignIn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await SignInAsync();

    private async Task SignInAsync()
    {
        if (_busy)
            return;

        var token = (TokenBox.Text ?? "").Trim();
        if (token.Length == 0)
        {
            ShowError("Paste your Discord token to continue.");
            return;
        }

        _busy = true;
        SignInButton.IsEnabled = false;
        SignInButton.Content = "Signing in...";
        ErrorText.IsVisible = false;

        var statusLabel = StatusBox.SelectedItem as string ?? "Available";
        var result = await App.Discord.LoginAsync(token, StatusHelper.FromLabel(statusLabel));

        _busy = false;
        SignInButton.IsEnabled = true;
        SignInButton.Content = "Sign in";

        if (!result.Success)
        {
            ShowError(result.Error ?? "Could not sign in.");
            return;
        }

        var remember = RememberBox.IsChecked == true;
        SettingsService.Current.Token = remember ? token : "";
        SettingsService.Current.RememberMe = remember;
        SettingsService.Current.LastStatus = statusLabel;
        SettingsService.Save();

        App.Instance.ShowHome();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
