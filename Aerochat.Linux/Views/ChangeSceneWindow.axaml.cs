using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Aerochat.Linux.ViewModels;

namespace Aerochat.Linux.Views;

public partial class ChangeSceneWindow : Window
{
    private readonly ChangeSceneViewModel _viewModel = new();

    public ChangeSceneWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Opened += (_, _) =>
        {
            _viewModel.Load();
            ScrollToSelection();
        };
    }

    private void ScrollToSelection()
    {
        var index = 0;
        for (var i = 0; i < _viewModel.Items.Count; i++)
        {
            if (!_viewModel.Items[i].IsSelected)
                continue;

            index = i;
            break;
        }

        SceneScroll.Offset = new Vector(0, index / 4 * 88);
    }

    private void Titlebar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Control control && control.FindAncestorOfType<Button>() is not null)
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Scene_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (sender is Control { DataContext: ChangeSceneItemViewModel item })
            _viewModel.Select(item);
    }

    private void ProfileColor_Click(object? sender, RoutedEventArgs e) =>
        _viewModel.UseProfileScene(App.Discord.CurrentUser);

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
