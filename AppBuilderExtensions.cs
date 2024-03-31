using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace CM.RedisCache;

public static class AppBuilderExtensions
{
    public static void UseRedisCache(this IApplicationBuilder app)
    {
        // Set multiplexer in the static class RedisCache
        RedisCache.Multiplexer = app.ApplicationServices.GetService<IConnectionMultiplexer>() ?? throw new InvalidOperationException("Redis Multiplexer is not defined");
    }
}