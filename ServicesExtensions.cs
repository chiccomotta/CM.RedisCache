using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace CM.RedisCache;

public static class ServicesExtensions
{
    public static void AddRedisService(this IServiceCollection services, string redisConnection)
    {
        services.AddSingleton<IConnectionMultiplexer>(opt => ConnectionMultiplexer.Connect(redisConnection));
    }
}