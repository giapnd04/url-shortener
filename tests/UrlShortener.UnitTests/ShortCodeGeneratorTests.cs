using UrlShortener.Infrastructure.Links;

namespace UrlShortener.UnitTests;

public class ShortCodeGeneratorTests
{
    private readonly RandomBase62CodeGenerator _generator = new();

    [Fact]
    public void Generates_seven_base62_characters()
    {
        for (var i = 0; i < 1_000; i++)
        {
            var code = _generator.Generate();

            Assert.Equal(RandomBase62CodeGenerator.CodeLength, code.Length);
            Assert.All(code, c => Assert.Contains(c, RandomBase62CodeGenerator.Alphabet));
        }
    }

    [Fact]
    public void Uses_both_upper_and_lower_case()
    {
        var chars = string.Concat(Enumerable.Range(0, 200).Select(_ => _generator.Generate()));

        Assert.Contains(chars, char.IsUpper);
        Assert.Contains(chars, char.IsLower);
        Assert.Contains(chars, char.IsDigit);
    }
}
