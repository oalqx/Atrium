using Atrium.Core.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Atrium.App;

internal sealed class ArtworkThemeService
{
    private readonly Dictionary<Uri, ArtworkPalette> _cache = [];

    public async Task<ArtworkPalette> GetPaletteAsync(Uri? uri, CancellationToken cancellationToken)
    {
        if (uri == null) return ArtworkPalette.Default;
        if (_cache.TryGetValue(uri, out var cached)) return cached;
        using var stream = uri.IsFile
            ? await (await StorageFile.GetFileFromPathAsync(uri.LocalPath).AsTask(cancellationToken)).OpenReadAsync().AsTask(cancellationToken)
            : await RandomAccessStreamReference.CreateFromUri(uri).OpenReadAsync().AsTask(cancellationToken);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            new BitmapTransform { ScaledWidth = 64, ScaledHeight = 64 },
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var palette = ArtworkPalette.FromBgra(pixels.DetachPixelData());
        if (_cache.Count >= 64) _cache.Remove(_cache.Keys.First());
        _cache[uri] = palette;
        return palette;
    }
}
