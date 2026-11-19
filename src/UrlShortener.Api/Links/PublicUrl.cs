namespace UrlShortener.Api.Links;

/// <summary>
/// Public base URL used to build shortUrl, from App:PublicBaseUrl (falls back to the request's
/// scheme and host). Its host, plus the request host, count as "this service" for URL validation.
/// </summary>
public sealed class PublicUrl(IConfiguration configuration)
{
    private readonly Uri? _configured =
        Uri.TryCreate(configuration["App:PublicBaseUrl"], UriKind.Absolute, out var uri) ? uri : null;

    public string BaseUrl(HttpRequest request) =>
        _configured?.GetLeftPart(UriPartial.Authority) ?? $"{request.Scheme}://{request.Host}";

    public IEnumerable<string> OwnHosts(HttpRequest request)
    {
        if (_configured is not null)
        {
            yield return _configured.Host;
        }

        if (request.Host.HasValue)
        {
            yield return request.Host.Host;
        }
    }
}
