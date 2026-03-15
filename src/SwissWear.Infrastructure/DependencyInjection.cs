using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using SwissWear.Domain.Contracts;
using SwissWear.Infrastructure.Data;
using SwissWear.Infrastructure.Services;

namespace SwissWear.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSwissWearInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Database
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        // Storage
        services.AddSingleton<IStorageService, AzureBlobStorageService>();

        // Cache (Valkey/Redis)
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(configuration["Valkey:ConnectionString"]
                ?? throw new InvalidOperationException("Valkey:ConnectionString is not configured.")));
        services.AddSingleton<ICacheService, ValkeyCacheService>();
        services.AddSingleton<IPubSubService, ValkeyPubSubService>();

        // Application services
        services.AddScoped<PersonService>();
        services.AddSingleton<IChatAuditService, ChatAuditService>();
        services.AddSingleton<IChatService, ChatService>();

        return services;
    }
}
