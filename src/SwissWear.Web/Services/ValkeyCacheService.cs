using System.Text.Json;
using StackExchange.Redis;

namespace SwissWear.Web.Services;

public class ValkeyCacheService : ICacheService
{
    private readonly IDatabase _database;
    private readonly ISubscriber _subscriber;

    public ValkeyCacheService(IConnectionMultiplexer connection)
    {
        _database = connection.GetDatabase();
        _subscriber = connection.GetSubscriber();
    }

    // --- Key-value ---

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

    // --- Hash ---

    public async Task HashSetAsync(string key, string field, string value, CancellationToken cancellationToken = default)
    {
        await _database.HashSetAsync(key, field, value);
    }

    public async Task<string?> HashGetAsync(string key, string field, CancellationToken cancellationToken = default)
    {
        var value = await _database.HashGetAsync(key, field);
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    public async Task<IReadOnlyDictionary<string, string>> HashGetAllAsync(string key, CancellationToken cancellationToken = default)
    {
        var entries = await _database.HashGetAllAsync(key);
        return entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
    }

    public async Task HashRemoveAsync(string key, string field, CancellationToken cancellationToken = default)
    {
        await _database.HashDeleteAsync(key, field);
    }

    // --- List ---

    public async Task ListPushAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await _database.ListRightPushAsync(key, value);
    }

    public async Task<IReadOnlyList<string>> ListRangeAsync(string key, long start, long stop, CancellationToken cancellationToken = default)
    {
        var values = await _database.ListRangeAsync(key, start, stop);
        return values.Select(v => v.ToString()).ToList();
    }

    public async Task ListTrimAsync(string key, long start, long stop, CancellationToken cancellationToken = default)
    {
        await _database.ListTrimAsync(key, start, stop);
    }

    // --- Pub/Sub ---

    public async Task PublishAsync(string channel, string message, CancellationToken cancellationToken = default)
    {
        await _subscriber.PublishAsync(RedisChannel.Literal(channel), message);
    }

    public void Subscribe(string channel, Action<string> handler)
    {
        _subscriber.Subscribe(RedisChannel.Literal(channel), (_, value) =>
        {
            handler(value.ToString());
        });
    }

    public void UnsubscribeAll()
    {
        _subscriber.UnsubscribeAll();
    }
}
