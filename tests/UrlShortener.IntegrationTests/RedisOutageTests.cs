using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Testcontainers.Redis;
using UrlShortener.Infrastructure.Caching;
using UrlShortener.IntegrationTests.Infrastructure;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// Own containers, because these tests stop Redis. Redis gets a fixed host port so the API's
/// multiplexer can reconnect to the same address after the container is started again.
/// </summary>
public sealed class RedisOutageFactory : ApiFactory
{
    private readonly int _port = FreePort();

    protected override RedisContainer CreateRedisContainer() =>
        new RedisBuilder().WithImage("redis:7.4-alpine").WithPortBinding(_port, 6379).Build();

    // 127.0.0.1, not localhost: on Windows localhost resolves to ::1 first and the short (1s)
    // connect timeout can expire before the client falls back to IPv4.
    protected override string RedisConnectionString => $"127.0.0.1:{_port}";

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>US-08: Redis down → redirects keep working from SQL; Redis back → cache is used again.</summary>
public class RedisOutageTests(RedisOutageFactory api) : IClassFixture<RedisOutageFactory>
{
    [Fact]
    public async Task Redirect_survives_redis_outage_and_cache_resumes_after_restart()
    {
        var (_, key) = await api.CreateClientAsync();
        var http = api.CreateClientWithKey(key);
        var anon = api.CreateClientWithKey(null);
        var cachedCode = await ApiFactory.CreateLinkAsync(http, "https://example.com/was-cached");
        await anon.GetAsync($"/{cachedCode}");
        Assert.True(await (await api.RedisDbAsync()).KeyExistsAsync(RedisLinkCache.Key(cachedCode)), "not cached before the outage");

        await api.Redis.StopAsync();
        try
        {
            // Fail-open: served from SQL. The first request may still see the dying connection; after
            // that, commands fail fast, so redirects must not wait on Redis at all.
            var during = await anon.GetAsync($"/{cachedCode}");
            Assert.Equal(HttpStatusCode.Found, during.StatusCode);
            Assert.Equal("https://example.com/was-cached", during.Headers.Location?.OriginalString);

            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 5; i++)
            {
                Assert.Equal(HttpStatusCode.Found, (await anon.GetAsync($"/{cachedCode}")).StatusCode);
            }
            watch.Stop();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1.5), $"5 redirects took {watch.Elapsed} while Redis was down");

            // Writes still work; invalidation failure is swallowed.
            var patch = await http.PatchAsync($"/api/links/{cachedCode}",
                JsonContent("{\"url\":\"https://example.com/changed-during-outage\"}"));
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

            // Readiness: Degraded, still 200, so traffic keeps flowing.
            var ready = await anon.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            Assert.Equal("Degraded", await ready.Content.ReadAsStringAsync());
        }
        finally
        {
            await api.Redis.StartAsync();
        }

        // Back up: without restarting the API, a miss writes to Redis again.
        var freshCode = await ApiFactory.CreateLinkAsync(http, "https://example.com/after-restart");
        var redis = await api.RedisDbAsync();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var cachedAgain = false;
        while (!cachedAgain && DateTime.UtcNow < deadline)
        {
            Assert.Equal(HttpStatusCode.Found, (await anon.GetAsync($"/{freshCode}")).StatusCode);
            cachedAgain = await redis.KeyExistsAsync(RedisLinkCache.Key(freshCode));
            if (!cachedAgain)
            {
                await Task.Delay(500);
            }
        }

        Assert.True(cachedAgain, "API did not start using Redis again after it came back");
        Assert.Equal("Healthy", await (await anon.GetAsync("/health/ready")).Content.ReadAsStringAsync());
    }

    private static StringContent JsonContent(string json) => new(json, System.Text.Encoding.UTF8, "application/json");
}
