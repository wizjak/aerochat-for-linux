using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Aerochat.Linux.Controls;

/// <summary>
/// Draws a WLM-style nine-slice background. The source image is split into nine pieces;
/// the corners keep their size, while the edges and the centre stretch to fill the control.
/// Images can be tinted with the current scene colour using the same overlay blend that the
/// original WPF client uses, so the look stays close to Windows Live Messenger 2009.
/// </summary>
public class NineSlice : Control
{
    private static readonly Dictionary<string, Bitmap?> RawImages = new();
    private static readonly Dictionary<TintKey, Bitmap?> TintedImages = new();
    private static readonly Dictionary<SliceKey, CroppedBitmap[]?> SlicedImages = new();

    static NineSlice()
    {
        AffectsRender<NineSlice>(
            SourceProperty,
            HoverSourceProperty,
            SelectedSourceProperty,
            SelectedHoverSourceProperty,
            SliceProperty,
            TintProperty,
            DarkenProperty,
            IsHoveredProperty,
            IsSelectedProperty);
    }

    /// <summary>Asset URI of the normal background image.</summary>
    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<NineSlice, string?>(nameof(Source));

    /// <summary>Asset URI used while the pointer is over the control.</summary>
    public static readonly StyledProperty<string?> HoverSourceProperty =
        AvaloniaProperty.Register<NineSlice, string?>(nameof(HoverSource));

    /// <summary>Asset URI used while <see cref="IsSelected"/> is true.</summary>
    public static readonly StyledProperty<string?> SelectedSourceProperty =
        AvaloniaProperty.Register<NineSlice, string?>(nameof(SelectedSource));

    /// <summary>Asset URI used while the control is selected and hovered.</summary>
    public static readonly StyledProperty<string?> SelectedHoverSourceProperty =
        AvaloniaProperty.Register<NineSlice, string?>(nameof(SelectedHoverSource));

    /// <summary>
    /// Slice size. <see cref="Thickness.Left"/> and <see cref="Thickness.Top"/> are used as
    /// the horizontal and vertical slice size respectively, matching the original WLM
    /// "4,4" / "64,2" / "1,1" syntax.
    /// </summary>
    public static readonly StyledProperty<Thickness> SliceProperty =
        AvaloniaProperty.Register<NineSlice, Thickness>(nameof(Slice), new Thickness(8));

    /// <summary>Scene colour applied to the image. A transparent colour disables tinting.</summary>
    public static readonly StyledProperty<Color> TintProperty =
        AvaloniaProperty.Register<NineSlice, Color>(nameof(Tint), Colors.Transparent);

    /// <summary>Darkens the tint between 0 (none) and 1 (black).</summary>
    public static readonly StyledProperty<double> DarkenProperty =
        AvaloniaProperty.Register<NineSlice, double>(nameof(Darken));

    /// <summary>
    /// Optional externally driven hover state. Useful when the control sits below a
    /// TextBox or TreeView row and cannot observe the pointer itself.
    /// </summary>
    public static readonly StyledProperty<bool> IsHoveredProperty =
        AvaloniaProperty.Register<NineSlice, bool>(nameof(IsHovered));

    /// <summary>Drives the selected-state image.</summary>
    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<NineSlice, bool>(nameof(IsSelected));

    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string? HoverSource
    {
        get => GetValue(HoverSourceProperty);
        set => SetValue(HoverSourceProperty, value);
    }

    public string? SelectedSource
    {
        get => GetValue(SelectedSourceProperty);
        set => SetValue(SelectedSourceProperty, value);
    }

    public string? SelectedHoverSource
    {
        get => GetValue(SelectedHoverSourceProperty);
        set => SetValue(SelectedHoverSourceProperty, value);
    }

    public Thickness Slice
    {
        get => GetValue(SliceProperty);
        set => SetValue(SliceProperty, value);
    }

    public Color Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public double Darken
    {
        get => GetValue(DarkenProperty);
        set => SetValue(DarkenProperty, value);
    }

    public bool IsHovered
    {
        get => GetValue(IsHoveredProperty);
        set => SetValue(IsHoveredProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var source = EffectiveSource();
        if (string.IsNullOrWhiteSpace(source))
            return;

        var tint = Tint;
        var darken = Math.Clamp(Darken, 0, 1);
        var slice = Slice;

        var key = new SliceKey(
            source,
            tint.A == 0 ? 0u : tint.ToUInt32(),
            (int)Math.Round(darken * 100),
            Math.Max(1, (int)Math.Round(slice.Left)),
            Math.Max(1, (int)Math.Round(slice.Top)));

        if (!SlicedImages.TryGetValue(key, out var parts))
        {
            parts = BuildParts(key);
            SlicedImages[key] = parts;
        }

        if (parts is null)
            return;

        var width = Bounds.Width;
        var height = Bounds.Height;
        var sw = Math.Min(key.SliceWidth, width / 2);
        var sh = Math.Min(key.SliceHeight, height / 2);

        Draw(context, parts[0], 0, 0, sw, sh);
        Draw(context, parts[1], sw, 0, width - sw * 2, sh);
        Draw(context, parts[2], width - sw, 0, sw, sh);
        Draw(context, parts[3], 0, sh, sw, height - sh * 2);
        Draw(context, parts[4], sw, sh, width - sw * 2, height - sh * 2);
        Draw(context, parts[5], width - sw, sh, sw, height - sh * 2);
        Draw(context, parts[6], 0, height - sh, sw, sh);
        Draw(context, parts[7], sw, height - sh, width - sw * 2, sh);
        Draw(context, parts[8], width - sw, height - sh, sw, sh);
    }

    private static void Draw(DrawingContext context, CroppedBitmap image, double x, double y,
        double width, double height)
    {
        if (width <= 0 || height <= 0)
            return;

        context.DrawImage(image, new Rect(x, y, width, height));
    }

    private string? EffectiveSource()
    {
        var hovered = IsHovered || IsPointerOver;

        if (IsSelected)
        {
            if (hovered && !string.IsNullOrWhiteSpace(SelectedHoverSource))
                return SelectedHoverSource;

            return SelectedSource ?? Source;
        }

        if (hovered && !string.IsNullOrWhiteSpace(HoverSource))
            return HoverSource;

        return Source;
    }

    private static CroppedBitmap[]? BuildParts(SliceKey key)
    {
        var tintKey = new TintKey(key.Source, key.Tint, key.Darken);

        if (!TintedImages.TryGetValue(tintKey, out var bitmap))
        {
            bitmap = LoadTinted(key.Source, key.Tint, key.Darken / 100.0);
            TintedImages[tintKey] = bitmap;
        }

        if (bitmap is null)
            return null;

        var imageWidth = bitmap.PixelSize.Width;
        var imageHeight = bitmap.PixelSize.Height;
        if (imageWidth <= 0 || imageHeight <= 0)
            return null;

        var sw = Math.Clamp(key.SliceWidth, 1, Math.Max(1, imageWidth / 2));
        var sh = Math.Clamp(key.SliceHeight, 1, Math.Max(1, imageHeight / 2));
        var cw = Math.Max(1, imageWidth - sw * 2);
        var ch = Math.Max(1, imageHeight - sh * 2);

        return
        [
            Crop(bitmap, 0, 0, sw, sh),
            Crop(bitmap, sw, 0, cw, sh),
            Crop(bitmap, imageWidth - sw, 0, sw, sh),
            Crop(bitmap, 0, sh, sw, ch),
            Crop(bitmap, sw, sh, cw, ch),
            Crop(bitmap, imageWidth - sw, sh, sw, ch),
            Crop(bitmap, 0, imageHeight - sh, sw, sh),
            Crop(bitmap, sw, imageHeight - sh, cw, sh),
            Crop(bitmap, imageWidth - sw, imageHeight - sh, sw, sh)
        ];
    }

    private static CroppedBitmap Crop(Bitmap bitmap, int x, int y, int width, int height) =>
        new(bitmap, new PixelRect(x, y, width, height));

    private static Bitmap? LoadTinted(string source, uint tintValue, double darken)
    {
        if (!RawImages.TryGetValue(source, out var raw))
        {
            raw = LoadAsset(source);
            RawImages[source] = raw;
        }

        if (raw is null)
            return null;

        var tint = tintValue == 0 ? Colors.Transparent : Color.FromUInt32(tintValue);
        if (tint.A == 0 && darken <= 0)
            return raw;

        var width = raw.PixelSize.Width;
        var height = raw.PixelSize.Height;

        var tinted = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        using (var buffer = tinted.Lock())
        {
            raw.CopyPixels(buffer, AlphaFormat.Unpremul);

            var length = buffer.RowBytes * height;
            var pixels = new byte[length];
            Marshal.Copy(buffer.Address, pixels, 0, length);

            var tintR = DarkenChannel(tint.R, darken);
            var tintG = DarkenChannel(tint.G, darken);
            var tintB = DarkenChannel(tint.B, darken);

            for (var y = 0; y < height; y++)
            {
                var offset = y * buffer.RowBytes;
                for (var x = 0; x < width; x++, offset += 4)
                {
                    if (pixels[offset + 3] == 0)
                        continue;

                    // BGRA, straight alpha.
                    pixels[offset] = Overlay(pixels[offset], tintB);
                    pixels[offset + 1] = Overlay(pixels[offset + 1], tintG);
                    pixels[offset + 2] = Overlay(pixels[offset + 2], tintR);
                }
            }

            Marshal.Copy(pixels, 0, buffer.Address, length);
        }

        return tinted;
    }

    private static Bitmap? LoadAsset(string source)
    {
        try
        {
            var uri = source.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(source)
                : source.StartsWith('/')
                    ? new Uri($"avares://Aerochat{source}")
                    : new Uri($"avares://Aerochat/{source.TrimStart('/')}");

            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static byte DarkenChannel(byte value, double darken) =>
        (byte)Math.Clamp(value * (1 - darken), 0, 255);

    private static byte Overlay(byte baseChannel, byte tintChannel)
    {
        var b = baseChannel / 255.0;
        var t = tintChannel / 255.0;
        var result = b < 0.5 ? 2 * b * t : 1 - 2 * (1 - b) * (1 - t);
        return (byte)Math.Clamp(result * 255, 0, 255);
    }

    private readonly record struct TintKey(string Source, uint Tint, int Darken);
    private readonly record struct SliceKey(string Source, uint Tint, int Darken, int SliceWidth, int SliceHeight);
}
