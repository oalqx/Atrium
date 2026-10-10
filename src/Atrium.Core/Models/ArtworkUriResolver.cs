namespace Atrium.Core.Models;

public static class ArtworkUriResolver
{
    public static Uri? Resolve(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;

        try
        {
            if (Path.IsPathFullyQualified(location))
            {
                var path = Path.GetFullPath(location);
                return File.Exists(path) ? new Uri(path, UriKind.Absolute) : null;
            }

            if (!Uri.TryCreate(location, UriKind.Absolute, out var uri)) return null;
            if (uri.IsFile) return File.Exists(uri.LocalPath) ? uri : null;
            return uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host) ? uri : null;
        }
        catch (ArgumentException) { return null; }
        catch (IOException) { return null; }
        catch (UriFormatException) { return null; }
        catch (NotSupportedException) { return null; }
    }
}
