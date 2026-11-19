using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UrlShortener.Infrastructure.Data;
using UrlShortener.Infrastructure.Data.Configurations;

namespace UrlShortener.Infrastructure.Links;

public sealed record LinkView(
    string Code,
    string TargetUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ExpiresAt,
    DateTime? DisabledAt);

/// <summary>Only the columns redirect needs (projection keeps the read narrow).</summary>
public sealed record RedirectTarget(string TargetUrl, DateTime? ExpiresAt, DateTime? DisabledAt);

public sealed record PagedLinks(IReadOnlyList<LinkView> Items, int Page, int PageSize, int TotalCount);

/// <summary>Raised when every attempt hit an existing code. Almost certainly a generator bug or a nearly full code space.</summary>
public sealed class CodeGenerationExhaustedException(int attempts)
    : Exception($"Could not generate a unique short code after {attempts} attempts.")
{
    public int Attempts { get; } = attempts;
}

public sealed class LinkService(
    UrlShortenerDbContext db,
    IShortCodeGenerator generator,
    TimeProvider time,
    ILogger<LinkService> logger)
{
    public const int MaxCreateAttempts = 5;

    public async Task<LinkView> CreateAsync(int clientId, string targetUrl, DateTime? expiresAt, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxCreateAttempts; attempt++)
        {
            var now = time.GetUtcNow().UtcDateTime;
            var link = new ShortLink
            {
                Code = generator.Generate(),
                TargetUrl = targetUrl,
                ClientId = clientId,
                CreatedAt = now,
                UpdatedAt = now,
                ExpiresAt = expiresAt,
            };
            db.ShortLinks.Add(link);

            try
            {
                // No SELECT-then-INSERT: two concurrent requests could both see "free" and both insert.
                // The unique index decides atomically; we react to its error instead.
                await db.SaveChangesAsync(ct);
                return ToView(link);
            }
            catch (DbUpdateException ex) when (IsDuplicateCode(ex))
            {
                // Forget the failed entity, otherwise the next SaveChanges would try to insert it again.
                db.Entry(link).State = EntityState.Detached;
                logger.LogWarning("Short code collision on attempt {Attempt}/{MaxAttempts}", attempt, MaxCreateAttempts);
            }
        }

        logger.LogError("Short code generation exhausted after {MaxAttempts} attempts", MaxCreateAttempts);
        throw new CodeGenerationExhaustedException(MaxCreateAttempts);
    }

    /// <summary>Single query: index seek on UX_short_links_code, no tracking, three columns.</summary>
    public Task<RedirectTarget?> GetRedirectTargetAsync(string code, CancellationToken ct) =>
        db.ShortLinks
            .AsNoTracking()
            .Where(l => l.Code == code)
            .Select(l => new RedirectTarget(l.TargetUrl, l.ExpiresAt, l.DisabledAt))
            .FirstOrDefaultAsync(ct);

    public async Task<PagedLinks> ListAsync(int clientId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ShortLinks.AsNoTracking().Where(l => l.ClientId == clientId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LinkView(l.Code, l.TargetUrl, l.CreatedAt, l.UpdatedAt, l.ExpiresAt, l.DisabledAt))
            .ToListAsync(ct);

        return new PagedLinks(items, page, pageSize, total);
    }

    /// <summary>Owner-scoped: another client's link looks exactly like a missing one (caller returns 404).</summary>
    public Task<LinkView?> GetAsync(int clientId, string code, CancellationToken ct) =>
        db.ShortLinks
            .AsNoTracking()
            .Where(l => l.Code == code && l.ClientId == clientId)
            .Select(l => new LinkView(l.Code, l.TargetUrl, l.CreatedAt, l.UpdatedAt, l.ExpiresAt, l.DisabledAt))
            .FirstOrDefaultAsync(ct);

    /// <summary>Null arguments mean "leave unchanged".</summary>
    public async Task<LinkView?> UpdateAsync(int clientId, string code, string? targetUrl, DateTime? expiresAt, CancellationToken ct)
    {
        var link = await FindOwnedAsync(clientId, code, ct);
        if (link is null)
        {
            return null;
        }

        if (targetUrl is not null)
        {
            link.TargetUrl = targetUrl;
        }

        if (expiresAt is not null)
        {
            link.ExpiresAt = expiresAt;
        }

        link.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return ToView(link);
    }

    /// <summary>Idempotent: disabling an already disabled link succeeds and keeps the first timestamp.</summary>
    public async Task<bool> DisableAsync(int clientId, string code, CancellationToken ct)
    {
        var link = await FindOwnedAsync(clientId, code, ct);
        if (link is null)
        {
            return false;
        }

        if (link.DisabledAt is null)
        {
            var now = time.GetUtcNow().UtcDateTime;
            link.DisabledAt = now;
            link.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    private Task<ShortLink?> FindOwnedAsync(int clientId, string code, CancellationToken ct) =>
        db.ShortLinks.FirstOrDefaultAsync(l => l.Code == code && l.ClientId == clientId, ct);

    private static LinkView ToView(ShortLink l) =>
        new(l.Code, l.TargetUrl, l.CreatedAt, l.UpdatedAt, l.ExpiresAt, l.DisabledAt);

    /// <summary>2601 = duplicate key in unique index, 2627 = unique constraint. Only retry for the code index.</summary>
    private static bool IsDuplicateCode(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sql
        && sql.Message.Contains(ShortLinkConfiguration.UniqueCodeIndex, StringComparison.Ordinal);
}
