using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Aerochat.Linux.Controls;

public enum ProfileFrameSize
{
    ExtraSmall,
    Small,
    Medium,
    Large,
    ExtraLarge
}

/// <summary>
/// Renders one of the original WLM profile picture frames, with the avatar underneath.
/// </summary>
public class ProfilePictureFrame : Control
{
    private static readonly Dictionary<string, Bitmap?> Images = new();

    static ProfilePictureFrame()
    {
        AffectsRender<ProfilePictureFrame>(
            AvatarProperty,
            StatusProperty,
            FrameSizeProperty,
            IsGroupProperty);
        AffectsMeasure<ProfilePictureFrame>(FrameSizeProperty);
    }

    public static readonly StyledProperty<IImage?> AvatarProperty =
        AvaloniaProperty.Register<ProfilePictureFrame, IImage?>(nameof(Avatar));

    /// <summary>Available / Busy / Away / Appear offline, or a DSharpPlus status name.</summary>
    public static readonly StyledProperty<string?> StatusProperty =
        AvaloniaProperty.Register<ProfilePictureFrame, string?>(nameof(Status), "Offline");

    public static readonly StyledProperty<ProfileFrameSize> FrameSizeProperty =
        AvaloniaProperty.Register<ProfilePictureFrame, ProfileFrameSize>(nameof(FrameSize), ProfileFrameSize.Large);

    /// <summary>Uses the "M" (group) variant of the 16x16 status frame.</summary>
    public static readonly StyledProperty<bool> IsGroupProperty =
        AvaloniaProperty.Register<ProfilePictureFrame, bool>(nameof(IsGroup));

    public ProfilePictureFrame()
    {
        Opacity = 0.55;
    }

    public IImage? Avatar
    {
        get => GetValue(AvatarProperty);
        set => SetValue(AvatarProperty, value);
    }

    public string? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public ProfileFrameSize FrameSize
    {
        get => GetValue(FrameSizeProperty);
        set => SetValue(FrameSizeProperty, value);
    }

    public bool IsGroup
    {
        get => GetValue(IsGroupProperty);
        set => SetValue(IsGroupProperty, value);
    }

    private int Pixels => FrameSize switch
    {
        ProfileFrameSize.ExtraSmall => 16,
        ProfileFrameSize.Small => 45,
        ProfileFrameSize.Medium => 59,
        ProfileFrameSize.Large => 79,
        ProfileFrameSize.ExtraLarge => 139,
        _ => 45
    };

    private (double X, double Y, double Size) AvatarRect => FrameSize switch
    {
        ProfileFrameSize.ExtraSmall => (0, 0, 0),
        ProfileFrameSize.Small => (11, 10, 24),
        ProfileFrameSize.Medium => (15, 12, 32),
        ProfileFrameSize.Large => (18, 14, 48),
        ProfileFrameSize.ExtraLarge => (24, 19, 96),
        _ => (11, 10, 24)
    };

    protected override Size MeasureOverride(Size availableSize) => new(Pixels, Pixels);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StatusProperty)
            Opacity = IsOffline(Status) ? 0.55 : 1.0;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var size = Pixels;
        var rect = AvatarRect;

        if (rect.Size > 0)
        {
            if (Avatar is { } avatar)
            {
                context.DrawImage(avatar, new Rect(rect.X, rect.Y, rect.Size, rect.Size));
            }
            else
            {
                var placeholder = GetImage("avares://Aerochat/Assets/Resources/Frames/PlaceholderPfp.png");
                if (placeholder is not null)
                    context.DrawImage(placeholder, new Rect(rect.X, rect.Y, rect.Size, rect.Size));
            }
        }

        var frame = GetImage(FrameAssetPath());
        if (frame is not null)
            context.DrawImage(frame, new Rect(0, 0, size, size));
    }

    private string FrameAssetPath()
    {
        var status = IsOffline(Status) ? "Offline" : StatusName(Status);
        var size = FrameSize switch
        {
            ProfileFrameSize.ExtraSmall => "XS",
            ProfileFrameSize.Small => "Small",
            ProfileFrameSize.Medium => "Medium",
            ProfileFrameSize.Large => "Large",
            ProfileFrameSize.ExtraLarge => "XL",
            _ => "Small"
        };

        var group = FrameSize == ProfileFrameSize.ExtraSmall && IsGroup && status != "Offline" ? "M" : "";
        return $"avares://Aerochat/Assets/Resources/Frames/{size}Frame{status}{group}.png";
    }

    private static string StatusName(string? status) => status switch
    {
        "Available" or "Online" => "Active",
        "Busy" or "DoNotDisturb" or "Do Not Disturb" or "Dnd" => "Dnd",
        "Away" or "Idle" => "Idle",
        _ => "Offline"
    };

    private static bool IsOffline(string? status) =>
        status is null or "" or "Offline" or "Invisible" or "Appear offline" or "Offline ";

    private static Bitmap? GetImage(string path)
    {
        if (Images.TryGetValue(path, out var image))
            return image;

        try
        {
            using var stream = AssetLoader.Open(new Uri(path));
            image = new Bitmap(stream);
        }
        catch
        {
            image = null;
        }

        Images[path] = image;
        return image;
    }
}
