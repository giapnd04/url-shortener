using UrlShortener.Infrastructure.Links;

namespace UrlShortener.UnitTests;

public class UrlValidatorTests
{
    private static readonly string[] OwnHosts = ["sho.rt"];

    [Theory]
    [InlineData("https://example.com/a/b?x=1")]
    [InlineData("http://example.com")]
    [InlineData("HTTPS://Example.com/path")]
    public void Accepts_absolute_http_and_https(string url)
    {
        Assert.Null(UrlValidator.Validate(url, OwnHosts));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/relative/path")]
    [InlineData("example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///etc/passwd")]
    public void Rejects_non_http_urls(string? url)
    {
        Assert.NotNull(UrlValidator.Validate(url, OwnHosts));
    }

    [Fact]
    public void Rejects_urls_longer_than_2048()
    {
        var url = "https://example.com/" + new string('a', UrlValidator.MaxLength);

        Assert.NotNull(UrlValidator.Validate(url, OwnHosts));
    }

    [Fact]
    public void Accepts_url_of_exactly_2048()
    {
        const string prefix = "https://example.com/";
        var url = prefix + new string('a', UrlValidator.MaxLength - prefix.Length);

        Assert.Null(UrlValidator.Validate(url, OwnHosts));
    }

    [Theory]
    [InlineData("https://sho.rt/abc1234")]
    [InlineData("http://SHO.RT/x")]
    public void Rejects_links_to_this_service(string url)
    {
        Assert.NotNull(UrlValidator.Validate(url, OwnHosts));
    }
}
