using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.MsSql;
using Testcontainers.Redis;
using UrlShortener.Api.Auth;
using UrlShortener.Infrastructure;
using UrlShortener.Infrastructure.Auth;
using UrlShortener.Infrastructure.Caching;
using UrlShortener.Infrastructure.Data;
using UrlShortener.Infrastructure.Links;

namespace UrlShortener.IntegrationTests.Infrastructure;

/// <summary>
/// Real SQL Server 2022 and Redis 7.4 containers for the whole collection (needs a running Docker daemon).
/// Tests share the database and Redis, so each test creates its own client and its own codes.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private RedisContainer? _redis;

    public RedisContainer Redis => _redis ?? throw new InvalidOperationException("Not initialised.");

    /// <summary>Overridden by the outage tests to pin a host port that survives a container restart.</summary>
    protected virtual RedisContainer CreateRedisContainer() =>
        new RedisBuilder().WithImage("redis:7.4-alpine").Build();

    public async Task InitializeAsync()
    {
        _redis = CreateRedisContainer();
        await Task.WhenAll(_sql.StartAsync(), _redis.StartAsync());

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
        if (_redis is not null)
        {
            await _redis.DisposeAsync();
        }
    }

    protected virtual string RedisConnectionString => Redis.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Added after appsettings*.json, so these win over the empty/LocalDB connection strings there.
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{InfrastructureSetup.ConnectionStringName}"] = _sql.GetConnectionString(),
            [$"ConnectionStrings:{CacheOptions.RedisConnectionStringName}"] = RedisConnectionString,
            ["App:PublicBaseUrl"] = "https://sho.rt",
        }));
    }

    /// <summary>A separate Redis client for assertions (key exists, TTL), independent of the app's multiplexer.</summary>
    public async Task<IDatabase> RedisDbAsync()
    {
        _assertRedis ??= await ConnectionMultiplexer.ConnectAsync(RedisConnectionString + ",abortConnect=false");
        return _assertRedis.GetDatabase();
    }

    private IConnectionMultiplexer? _assertRedis;

    public WebApplicationFactory<Program> WithServices(Action<IServiceCollection> configure) =>
        WithWebHostBuilder(b => b.ConfigureTestServices(configure));

    /// <summary>A factory whose code generator returns the given codes in order (then repeats the last one).</summary>
    public WebApplicationFactory<Program> WithCodes(FakeCodeGenerator generator) =>
        WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IShortCodeGenerator>(generator)));

    public async Task<(int Id, string Key)> CreateClientAsync(string name = "test", bool disabled = false)
    {
        var key = ApiKeyHasher.NewKey();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
        var client = new ApiClient
        {
            Name = name,
            ApiKeyHash = ApiKeyHasher.Hash(key),
            CreatedAt = DateTime.UtcNow,
            DisabledAt = disabled ? DateTime.UtcNow : null,
        };
        db.ApiClients.Add(client);
        await db.SaveChangesAsync();
        return (client.Id, key);
    }

    public HttpClient CreateClientWithKey(string? key, WebApplicationFactory<Program>? factory = null)
    {
        var http = (factory ?? this).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (key is not null)
        {
            http.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, key);
        }

        return http;
    }

    public async Task<T> WithDbAsync<T>(Func<UrlShortenerDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>());
    }

    public static async Task<string> CreateLinkAsync(HttpClient http, string url = "https://example.com/")
    {
        var response = await http.PostAsJsonAsync("/api/links", new { url });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LinkDto>();
        return body!.Code;
    }
}

public sealed class FakeCodeGenerator(params string[] codes) : IShortCodeGenerator
{
    private int _calls;
    public int Calls => _calls;

    public string Generate()
    {
        var i = Interlocked.Increment(ref _calls) - 1;
        return codes[Math.Min(i, codes.Length - 1)];
    }

    /// <summary>A fresh random code, so tests sharing one database never collide by accident.</summary>
    public static string NewCode() => new RandomBase62CodeGenerator().Generate();
}

public sealed record LinkDto(
    string Code, string ShortUrl, string TargetUrl, string Status,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? ExpiresAt, DateTime? DisabledAt);

public sealed record LinkListDto(List<LinkDto> Items, int Page, int PageSize, int TotalCount);

[CollectionDefinition(Name)]
public sealed class SqlCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "sql";
}
