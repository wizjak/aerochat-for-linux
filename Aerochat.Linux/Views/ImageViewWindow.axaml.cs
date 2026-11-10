using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Aerochat.Linux.ViewModels;

namespace Aerochat.Linux.Views;

public partial class ImageViewWindow : Window
{
    private readonly AttachmentViewModel _attachment;

    public ImageViewWindow(AttachmentViewModel attachment)
    {
        InitializeComponent();

        _attachment = attachment;
        DataContext = attachment;

        Width = Math.Clamp(attachment.ViewerWidth + 20, 320, 960);
        Height = Math.Clamp(attachment.ViewerHeight + 96, 240, 780);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    private void Titlebar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Control control &&
            control.FindAncestorOfType<Button>() is not null)
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async void Open_Click(object? sender, RoutedEventArgs e) =>
        await _attachment.OpenExternallyAsync();

    private async void CopyImage_Click(object? sender, RoutedEventArgs e)
    {
        if (_attachment.Bitmap is null)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetBitmapAsync(_attachment.Bitmap);
    }
}
