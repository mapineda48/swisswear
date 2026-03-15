using System.Text.Json;
using StackExchange.Redis;

namespace SwissWear.Web.Services;

public class ValkeyCacheService : ICacheService
{
    private readonly IDatabase _database;

    public ValkeyCacheService(IConnectionMultiplexer connection)
    {
        _database = connection.GetDatabase();
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value);

        if (expiration.HasValue)
            await _database.StringSetAsync(key, json, new Expiration(expiration.Value));
        else
            await _database.StringSetAsync(key, json);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var value = await _database.StringGetAsync(key);

        if (value.IsNullOrEmpty)
            return default;

        return JsonSerializer.Deserialize<T>(value.ToString());
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await _database.KeyDeleteAsync(key);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return await _database.KeyExistsAsync(key);
    }
}
