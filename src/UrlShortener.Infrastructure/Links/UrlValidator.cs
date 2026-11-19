namespace UrlShortener.Infrastructure.Links;

/// <summary>Rules for a target URL (US-01). Returns an error message, or null when valid.</summary>
public static class UrlValidator
{
    public const int MaxLength = 2048;

    /// <param name="url">The target URL from the request.</param>
    /// <param name="ownHosts">Hosts of this service; links pointing back here would create redirect loops.</param>
    public static string? Validate(string? url, IEnumerable<string> ownHosts)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "URL is required.";
        }

        if (url.Length > MaxLength)
        {
            return $"URL must be at most {MaxLength} characters.";
        }

        // Allow-list the scheme instead of block-listing javascript:/data:/file:/vbscript:...
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            return "Only absolute http and https URLs are allowed.";
        }

        if (ownHosts.Any(h => string.Equals(h, uri.Host, StringComparison.OrdinalIgnoreCase)))
        {
            return "URL must not point to this service.";
        }

        return null;
    }
}
