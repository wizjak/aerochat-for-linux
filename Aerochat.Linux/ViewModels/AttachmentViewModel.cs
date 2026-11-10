using Avalonia.Media.Imaging;
using Aerochat.Linux.Services;
using DSharpPlus.Entities;

namespace Aerochat.Linux.ViewModels;

/// <summary>
/// One attachment in a message. The same type is also used for files queued in the
/// composer, which keeps the preview and the sent attachment visually identical.
/// </summary>
public sealed class AttachmentViewModel : ViewModelBase
{
    private const double PreviewMaxWidth = 300;
    private const double PreviewMaxHeight = 220;

    private static readonly string[] ImageExtensions =
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".avif", ".tif", ".tiff"
    };

    private Bitmap? _bitmap;
    private bool _bitmapRequested;

    public ulong Id { get; init; }
    public string FileName { get; init; } = "";
    public string Url { get; init; } = "";
    public string ProxyUrl { get; init; } = "";
    public long FileSize { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsImage { get; init; }
    public bool IsPending { get; init; }
    public int PendingIndex { get; set; }

    /// <summary>Bytes queued in the composer, kept in memory until the message is sent.</summary>
    public byte[]? PendingData { get; init; }

    public Bitmap? Bitmap
    {
        get => _bitmap;
        private set
        {
            if (SetProperty(ref _bitmap, value))
            {
                Raise(nameof(DisplayWidth));
                Raise(nameof(DisplayHeight));
                Raise(nameof(HasBitmap));
            }
        }
    }

    public bool HasBitmap => Bitmap is not null;
    public bool HasImage => IsImage || HasBitmap;
    public string Extension => Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();
    public string SizeText => FormatSize(FileSize);
    public string FileInfo => string.IsNullOrEmpty(Extension) ? SizeText : $"{Extension} · {SizeText}";
    public string Tooltip => FileName;

    public double DisplayWidth
    {
        get
        {
            var (width, _) = Fit();
            return width;
        }
    }

    public double DisplayHeight
    {
        get
        {
            var (_, height) = Fit();
            return height;
        }
    }

    /// <summary>Wide image used by the attachment viewer, preserving aspect ratio.</summary>
    public double ViewerWidth
    {
        get
        {
            var width = Width > 0 ? Width : Bitmap?.PixelSize.Width ?? 720;
            var height = Height > 0 ? Height : Bitmap?.PixelSize.Height ?? 540;
            var scale = Math.Min(920d / width, 680d / height);
            if (scale > 1 || scale <= 0)
                scale = 1;
            return Math.Max(240, width * scale);
        }
    }

    public double ViewerHeight
    {
        get
        {
            var width = Width > 0 ? Width : Bitmap?.PixelSize.Width ?? 720;
            var height = Height > 0 ? Height : Bitmap?.PixelSize.Height ?? 540;
            var scale = Math.Min(920d / width, 680d / height);
            if (scale > 1 || scale <= 0)
                scale = 1;
            return Math.Max(160, height * scale);
        }
    }

    private (double Width, double Height) Fit()
    {
        var width = Width > 0 ? (double)Width : Bitmap?.PixelSize.Width ?? 0;
        var height = Height > 0 ? (double)Height : Bitmap?.PixelSize.Height ?? 0;

        if (width <= 0 || height <= 0)
            return IsImage || HasBitmap ? (220, 150) : (0, 0);

        var scale = Math.Min(PreviewMaxWidth / width, PreviewMaxHeight / height);
        if (scale > 1)
            scale = 1;

        return (Math.Max(48, Math.Round(width * scale)), Math.Max(36, Math.Round(height * scale)));
    }

    public async Task LoadBitmapAsync()
    {
        if ((!IsImage && Bitmap is null) || _bitmapRequested)
            return;

        _bitmapRequested = true;
        var bytes = await MediaService.GetBytesAsync(ProxyUrl.Length > 0 ? ProxyUrl : Url);
        if (bytes is null)
            return;

        try
        {
            using var stream = new MemoryStream(bytes);
            Bitmap = new Bitmap(stream);
            Raise(nameof(ViewerWidth));
            Raise(nameof(ViewerHeight));
        }
        catch
        {
            // A corrupt or unsupported image (for example AVIF on older Avalonia) falls
            // back to the file card instead of breaking the whole message list.
        }
    }

    public async Task<string?> EnsureLocalAsync()
    {
        if (IsPending && PendingData is not null)
        {
            var path = Path.Combine(MediaService.CacheDirectory, "outgoing",
                MediaService.SanitizeFileName(FileName));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, PendingData);
                return path;
            }
            catch
            {
                return null;
            }
        }

        return await MediaService.DownloadToCacheAsync(
            ProxyUrl.Length > 0 ? ProxyUrl : Url, FileName, Id);
    }

    public async Task<bool> OpenExternallyAsync()
    {
        var path = await EnsureLocalAsync();
        if (path is not null)
            return MediaService.Open(path);

        return Url.Length > 0 && MediaService.Open(Url);
    }

    public static AttachmentViewModel From(DiscordAttachment attachment)
    {
        var fileName = attachment.FileName ?? "attachment";
        var mediaType = attachment.MediaType ?? "";
        var isImage = mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                      ImageExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

        return new AttachmentViewModel
        {
            Id = attachment.Id,
            FileName = fileName,
            Url = attachment.Url ?? "",
            ProxyUrl = attachment.ProxyUrl ?? "",
            FileSize = attachment.FileSize,
            Width = attachment.Width ?? 0,
            Height = attachment.Height ?? 0,
            IsImage = isImage
        };
    }

    public static AttachmentViewModel Pending(string fileName, byte[] data, Bitmap? preview, int index)
    {
        var isImage = preview is not null ||
                      ImageExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

        return new AttachmentViewModel
        {
            FileName = fileName,
            FileSize = data.LongLength,
            IsImage = isImage,
            IsPending = true,
            PendingIndex = index,
            PendingData = data,
            Bitmap = preview
        };
    }

    public static bool LooksLikeImage(string fileName) =>
        ImageExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    private static string FormatSize(long size) => size switch
    {
        < 0 => "",
        < 1024 => $"{size} B",
        < 1024 * 1024 => $"{Math.Max(1, size / 1024)} KB",
        < 1024L * 1024 * 1024 => $"{size / 1024d / 1024d:0.#} MB",
        _ => $"{size / 1024d / 1024d / 1024d:0.#} GB"
    };
}
