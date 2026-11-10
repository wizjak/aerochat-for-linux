using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Aerochat.Linux.Services;

namespace Aerochat.Linux.ViewModels;

/// <summary>
/// One of the WLM scenes from Scenes.xml, plus the brushes derived from its accent color.
/// </summary>
public sealed class SceneViewModel : ViewModelBase
{
    private Bitmap? _image;

    public SceneViewModel(int id, string file, string displayName, string credit, bool isDefault, string colorHex)
    {
        Id = id;
        File = file;
        DisplayName = displayName;
        Credit = credit;
        IsDefault = isDefault;

        Color = ParseColor(colorHex);
        ColorHex = $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";

        var luminance = (0.299 * Color.R + 0.587 * Color.G + 0.114 * Color.B) / 255.0;
        IsDark = luminance < 0.72;
        var textColor = IsDark ? Colors.White : Color.FromRgb(0x1B, 0x1B, 0x1B);

        SceneBrush = new SolidColorBrush(Color);
        AccentBrush = new SolidColorBrush(Color);
        AccentDarkBrush = new SolidColorBrush(Mix(Color, Colors.Black, 0.3));

        TitlebarBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Mix(Color, Colors.White, 0.18), 0),
                new GradientStop(Color, 1)
            }
        };

        TitlebarTextBrush = new SolidColorBrush(textColor);
        TitlebarMutedBrush = new SolidColorBrush(WithAlpha(textColor, 180));
        TitlebarBorderBrush = new SolidColorBrush(Mix(Color, Colors.Black, 0.22));

        HeaderTextBrush = new SolidColorBrush(textColor);
        HeaderMutedBrush = new SolidColorBrush(WithAlpha(textColor, 185));
        HeaderBorderBrush = new SolidColorBrush(Mix(Color, Colors.White, 0.45));
        HeaderScrimBrush = new SolidColorBrush(IsDark
            ? Color.FromArgb(72, 0, 0, 0)
            : Color.FromArgb(54, 255, 255, 255));
        // The speech-bubble background is a white nine-slice for every scene, so the text
        // inside it must always stay dark regardless of the scene accent colour.
        InputTextBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));

        WindowBorderBrush = new SolidColorBrush(Mix(Color, Colors.Black, 0.5));
        BodyBrush = new SolidColorBrush(Mix(Color, Colors.White, 0.94));
        ContentBrush = new SolidColorBrush(Mix(Color, Colors.White, 0.97));
        FooterBrush = new SolidColorBrush(Mix(Color, Colors.White, 0.86));
        SeparatorBrush = new SolidColorBrush(Mix(Color, Colors.White, 0.62));
        HoverBrush = new SolidColorBrush(Mix(Color, Colors.White, 0.82));
        SelectedBrush = new SolidColorBrush(Mix(Color, Colors.White, 0.7));
        OwnMessageBrush = new SolidColorBrush(Mix(Color, Colors.White, 0.91));
        TreeBrush = new SolidColorBrush(Mix(Color, Colors.White, 0.985));
    }

    public int Id { get; }
    public string File { get; }
    public string DisplayName { get; }
    public string Credit { get; }
    public bool IsDefault { get; }
    public Color Color { get; }
    public string ColorHex { get; }
    public bool IsDark { get; }

    public IBrush SceneBrush { get; }
    public IBrush AccentBrush { get; }
    public IBrush AccentDarkBrush { get; }
    public IBrush TitlebarBrush { get; }
    public IBrush TitlebarTextBrush { get; }
    public IBrush TitlebarMutedBrush { get; }
    public IBrush TitlebarBorderBrush { get; }
    public IBrush HeaderTextBrush { get; }
    public IBrush HeaderMutedBrush { get; }
    public IBrush HeaderBorderBrush { get; }
    public IBrush HeaderScrimBrush { get; }
    public IBrush InputTextBrush { get; }
    public IBrush WindowBorderBrush { get; }
    public IBrush BodyBrush { get; }
    public IBrush ContentBrush { get; }
    public IBrush FooterBrush { get; }
    public IBrush SeparatorBrush { get; }
    public IBrush HoverBrush { get; }
    public IBrush SelectedBrush { get; }
    public IBrush OwnMessageBrush { get; }
    public IBrush TreeBrush { get; }

    public Bitmap? Image
    {
        get => _image;
        private set => SetProperty(ref _image, value);
    }

    public async Task LoadImageAsync()
    {
        if (_image is not null)
            return;

        var image = await SceneService.GetImageAsync(this);
        if (image is not null)
            Image = image;
    }

    public SceneViewModel WithColor(Color color) =>
        new(Id, File, DisplayName, Credit, IsDefault, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

    private static Color ParseColor(string hex)
    {
        if (Color.TryParse(hex, out var color))
            return color;

        return Color.FromRgb(0x3B, 0xB2, 0xEA);
    }

    private static Color Mix(Color a, Color b, double amount) =>
        Color.FromArgb(
            255,
            (byte)Math.Round(a.R + (b.R - a.R) * amount),
            (byte)Math.Round(a.G + (b.G - a.G) * amount),
            (byte)Math.Round(a.B + (b.B - a.B) * amount));

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);
}
