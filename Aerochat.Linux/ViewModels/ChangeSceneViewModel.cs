using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DSharpPlus.Entities;
using Aerochat.Linux.Services;

namespace Aerochat.Linux.ViewModels;

public sealed class ChangeSceneItemViewModel : ViewModelBase
{
    private static readonly IBrush SelectionBrushValue = new SolidColorBrush(Color.Parse("#2E8FD8"));

    private Bitmap? _thumbnail;
    private bool _isSelected;

    public ChangeSceneItemViewModel(SceneViewModel scene) => Scene = scene;

    public SceneViewModel Scene { get; }
    public string Name => Scene.DisplayName;
    public string Credit => string.IsNullOrWhiteSpace(Scene.Credit) ? " " : Scene.Credit;

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value))
                return;

            Raise(nameof(BorderBrush));
        }
    }

    public IBrush BorderBrush => _isSelected ? SelectionBrushValue : Brushes.Transparent;

    public async Task LoadAsync() => Thumbnail = await SceneService.GetThumbnailAsync(Scene);
}

public sealed class ChangeSceneViewModel : ViewModelBase
{
    private SceneViewModel _scene = SceneService.Current;
    private string _status = "";

    public SceneViewModel Scene
    {
        get => _scene;
        private set
        {
            if (!SetProperty(ref _scene, value))
                return;

            _ = value.LoadImageAsync();
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public ObservableCollection<ChangeSceneItemViewModel> Items { get; } = new();

    public void Load()
    {
        Items.Clear();
        Scene = SceneService.Current;
        _ = Scene.LoadImageAsync();
        Status = $"Current scene: {Scene.DisplayName}";

        foreach (var scene in SceneService.Scenes)
        {
            var item = new ChangeSceneItemViewModel(scene)
            {
                IsSelected = scene.Id == Scene.Id && scene.ColorHex == Scene.ColorHex
            };
            Items.Add(item);
        }

        _ = Task.WhenAll(Items.Select(item => item.LoadAsync()));
    }

    public void Select(ChangeSceneItemViewModel item)
    {
        foreach (var candidate in Items)
            candidate.IsSelected = false;

        item.IsSelected = true;
        SceneService.SetCurrent(item.Scene, persist: true);
        Scene = item.Scene;
        Status = $"Current scene: {Scene.DisplayName}";
    }

    public void UseProfileScene(DiscordUser? user)
    {
        SceneService.UseProfileScene(user);
        Scene = SceneService.Current;

        foreach (var item in Items)
            item.IsSelected = item.Scene.Id == Scene.Id && item.Scene.ColorHex == Scene.ColorHex;

        Status = $"Using your Discord profile color ({Scene.ColorHex})";
    }
}
