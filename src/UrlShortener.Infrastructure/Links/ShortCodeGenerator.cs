using System.Security.Cryptography;

namespace UrlShortener.Infrastructure.Links;

/// <summary>Injectable so tests can force collisions (US-01). See ADR-001.</summary>
public interface IShortCodeGenerator
{
    string Generate();
}

public sealed class RandomBase62CodeGenerator : IShortCodeGenerator
{
    public const int CodeLength = 7;
    public const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public string Generate()
    {
        // GetInt32(62) is uniform; a random byte % 62 would favour the first 8 characters.
        Span<char> code = stackalloc char[CodeLength];
        for (var i = 0; i < code.Length; i++)
        {
            code[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(code);
    }
}
