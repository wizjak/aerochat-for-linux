using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Aerochat.Linux.Services;
using Aerochat.Linux.ViewModels;

namespace Aerochat.Linux.Views;

public partial class NotificationWindow : Window
{
    private readonly Action? _onActivate;
    private DispatcherTimer? _animationTimer;
    private DispatcherTimer? _waitTimer;
    private PixelPoint _targetPosition;
    private double _startY;

    public NotificationWindow(NotificationViewModel viewModel, Action? onActivate)
    {
        InitializeComponent();

        DataContext = viewModel;
        _onActivate = onActivate;

        Opened += OnOpened;
        Closed += (_, _) => StopTimers();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var screen = Screens.Primary;
        if (screen is null)
            return;

        var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var width = (int)Math.Round(Width * scaling);
        var height = (int)Math.Round(Height * scaling);
        var area = screen.WorkingArea;

        var slot = NotificationService.GetSlot(this);
        _targetPosition = new PixelPoint(
            area.Right - width - (int)Math.Round(12 * scaling),
            area.Bottom - height - (int)Math.Round(12 * scaling) -
            slot * (height + (int)Math.Round(9 * scaling)));

        _startY = _targetPosition.Y + Math.Round(28 * scaling);
        Position = new PixelPoint(_targetPosition.X, (int)_startY);
        Opacity = 0;

        var step = 0;
        const int steps = 12;
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _animationTimer.Tick += (_, _) =>
        {
            step++;
            var t = Math.Min(1, step / (double)steps);
            // Ease-out cubic so the toast settles like the original WPF animation.
            var eased = 1 - Math.Pow(1 - t, 3);
            Opacity = eased;
            Position = new PixelPoint(_targetPosition.X,
                (int)Math.Round(_startY + (_targetPosition.Y - _startY) * eased));

            if (step < steps)
                return;

            _animationTimer?.Stop();
            _animationTimer = null;
            Opacity = 1;
            Position = _targetPosition;

            _waitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
            _waitTimer.Tick += (_, _) =>
            {
                _waitTimer?.Stop();
                _waitTimer = null;
                Close();
            };
            _waitTimer.Start();
        };
        _animationTimer.Start();
    }

    internal void MoveToSlot(int slot)
    {
        var screen = Screens.Primary;
        if (screen is null)
            return;

        var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var width = (int)Math.Round(Width * scaling);
        var height = (int)Math.Round(Height * scaling);
        var area = screen.WorkingArea;

        _targetPosition = new PixelPoint(
            area.Right - width - (int)Math.Round(12 * scaling),
            area.Bottom - height - (int)Math.Round(12 * scaling) -
            slot * (height + (int)Math.Round(9 * scaling)));

        if (_animationTimer is null)
            Position = _targetPosition;
    }

    private void Body_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;
        StopTimers();
        Close();
        _onActivate?.Invoke();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        StopTimers();
        Close();
    }

    private void StopTimers()
    {
        _animationTimer?.Stop();
        _animationTimer = null;
        _waitTimer?.Stop();
        _waitTimer = null;
    }
}
