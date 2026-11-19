using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using UrlShortener.Api.Auth;
using UrlShortener.Infrastructure.Links;

namespace UrlShortener.Api.Links;

public static class LinkEndpoints
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>
    /// Exactly Base62 and exactly the ADR-001 length, so /health, /api, /favicon.ico or long junk
    /// never reach this handler (404 from routing, no DB hit). {{7}} escapes braces in route templates.
    /// </summary>
    public const string RedirectRoute = "/{code:regex(^[0-9A-Za-z]{{7}}$)}";

    public static IEndpointRouteBuilder MapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var links = app.MapGroup("/api/links").RequireAuthorization();

        links.MapPost("/", CreateAsync);
        links.MapGet("/", ListAsync);
        links.MapGet("/{code}", GetAsync);
        links.MapPatch("/{code}", UpdateAsync);
        links.MapPost("/{code}/disable", DisableAsync);

        app.MapGet(RedirectRoute, RedirectAsync);

        return app;
    }

    private static async Task<Results<Created<LinkResponse>, ValidationProblem>> CreateAsync(
        CreateLinkRequest request, ClaimsPrincipal user, HttpRequest http,
        LinkService service, PublicUrl publicUrl, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var errors = new Dictionary<string, string[]>();
        AddError(errors, "url", UrlValidator.Validate(request.Url, publicUrl.OwnHosts(http)));
        AddError(errors, "expiresAt", ValidateExpiry(request.ExpiresAt, now));
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var link = await service.CreateAsync(user.GetClientId(), request.Url!, request.ExpiresAt?.UtcDateTime, ct);
        var body = LinkResponse.From(link, publicUrl.BaseUrl(http), now.UtcDateTime);
        return TypedResults.Created($"/api/links/{link.Code}", body);
    }

    private static async Task<Results<Ok<LinkListResponse>, ValidationProblem>> ListAsync(
        ClaimsPrincipal user, HttpRequest http, LinkService service, PublicUrl publicUrl, TimeProvider time,
        CancellationToken ct, int page = 1, int pageSize = DefaultPageSize)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) AddError(errors, "page", "page must be 1 or greater.");
        if (pageSize is < 1 or > MaxPageSize) AddError(errors, "pageSize", $"pageSize must be between 1 and {MaxPageSize}.");
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await service.ListAsync(user.GetClientId(), page, pageSize, ct);
        var baseUrl = publicUrl.BaseUrl(http);
        var now = time.GetUtcNow().UtcDateTime;
        return TypedResults.Ok(new LinkListResponse(
            result.Items.Select(v => LinkResponse.From(v, baseUrl, now)).ToList(),
            result.Page, result.PageSize, result.TotalCount));
    }

    private static async Task<Results<Ok<LinkResponse>, NotFound>> GetAsync(
        string code, ClaimsPrincipal user, HttpRequest http, LinkService service, PublicUrl publicUrl,
        TimeProvider time, CancellationToken ct)
    {
        var link = await service.GetAsync(user.GetClientId(), code, ct);
        return link is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(LinkResponse.From(link, publicUrl.BaseUrl(http), time.GetUtcNow().UtcDateTime));
    }

    private static async Task<Results<Ok<LinkResponse>, NotFound, ValidationProblem>> UpdateAsync(
        string code, UpdateLinkRequest request, ClaimsPrincipal user, HttpRequest http,
        LinkService service, PublicUrl publicUrl, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var errors = new Dictionary<string, string[]>();
        if (request.Url is not null)
        {
            AddError(errors, "url", UrlValidator.Validate(request.Url, publicUrl.OwnHosts(http)));
        }
        AddError(errors, "expiresAt", ValidateExpiry(request.ExpiresAt, now));
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var link = await service.UpdateAsync(user.GetClientId(), code, request.Url, request.ExpiresAt?.UtcDateTime, ct);
        return link is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(LinkResponse.From(link, publicUrl.BaseUrl(http), now.UtcDateTime));
    }

    private static async Task<Results<NoContent, NotFound>> DisableAsync(
        string code, ClaimsPrincipal user, LinkService service, CancellationToken ct)
    {
        return await service.DisableAsync(user.GetClientId(), code, ct)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    /// <summary>302, not 301: browsers cache 301 forever, so later edits/disables and click counting would be lost (ADR-002).</summary>
    private static async Task<IResult> RedirectAsync(
        string code, LinkService service, TimeProvider time, CancellationToken ct)
    {
        var target = await service.GetRedirectTargetAsync(code, ct);
        if (target is null)
        {
            return TypedResults.NotFound();
        }

        var status = LinkStatus.Of(target.DisabledAt, target.ExpiresAt, time.GetUtcNow().UtcDateTime);
        return status == LinkStatus.Active
            ? TypedResults.Redirect(target.TargetUrl, permanent: false)
            : TypedResults.StatusCode(StatusCodes.Status410Gone);
    }

    private static string? ValidateExpiry(DateTimeOffset? expiresAt, DateTimeOffset now) =>
        expiresAt is not null && expiresAt <= now ? "expiresAt must be in the future." : null;

    private static void AddError(Dictionary<string, string[]> errors, string field, string? message)
    {
        if (message is not null)
        {
            errors[field] = [message];
        }
    }
}
