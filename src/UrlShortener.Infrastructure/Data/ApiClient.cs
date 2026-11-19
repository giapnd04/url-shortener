namespace UrlShortener.Infrastructure.Data;

/// <summary>A caller of the management API. Only the SHA-256 of its key is stored.</summary>
public sealed class ApiClient
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required byte[] ApiKeyHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DisabledAt { get; set; }
}
