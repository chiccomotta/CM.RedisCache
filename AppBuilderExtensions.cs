using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace CM.RedisCache;

public static class AppBuilderExtensions
{
    public static IApplicationBuilder UseRedisCache(this IApplicationBuilder app)
    {
        // Setto il multiplexer nella classe statica RedisCache
        RedisCache.Multiplexer = app.ApplicationServices.GetService<IConnectionMultiplexer>() ?? throw new InvalidOperationException("Redis Multiplexer is not defined");
        return app;
    }
}