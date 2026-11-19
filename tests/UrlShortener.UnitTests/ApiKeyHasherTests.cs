using System.Text;
using UrlShortener.Infrastructure.Auth;

namespace UrlShortener.UnitTests;

public class ApiKeyHasherTests
{
    [Fact]
    public void Same_key_gives_same_32_byte_hash()
    {
        var a = ApiKeyHasher.Hash("key-1");
        var b = ApiKeyHasher.Hash("key-1");

        Assert.Equal(32, a.Length);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Hash_is_not_the_raw_key()
    {
        const string key = "key-1";

        Assert.NotEqual(Encoding.UTF8.GetBytes(key), ApiKeyHasher.Hash(key));
        Assert.NotEqual(ApiKeyHasher.Hash("key-1"), ApiKeyHasher.Hash("key-2"));
    }

    [Fact]
    public void New_keys_are_random_and_header_safe()
    {
        var a = ApiKeyHasher.NewKey();
        var b = ApiKeyHasher.NewKey();

        Assert.NotEqual(a, b);
        Assert.Equal(43, a.Length); // 32 bytes, base64url without padding
        Assert.DoesNotContain(a, c => c is '+' or '/' or '=');
    }
}
