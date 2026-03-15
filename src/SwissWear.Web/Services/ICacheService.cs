namespace SwissWear.Web.Services;

public interface ICacheService
{
    // Key-value
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    // Hash
    Task HashSetAsync(string key, string field, string value, CancellationToken cancellationToken = default);
    Task<string?> HashGetAsync(string key, string field, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> HashGetAllAsync(string key, CancellationToken cancellationToken = default);
    Task HashRemoveAsync(string key, string field, CancellationToken cancellationToken = default);

    // List
    Task ListPushAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListRangeAsync(string key, long start, long stop, CancellationToken cancellationToken = default);
    Task ListTrimAsync(string key, long start, long stop, CancellationToken cancellationToken = default);

    // Pub/Sub
    Task PublishAsync(string channel, string message, CancellationToken cancellationToken = default);
    void Subscribe(string channel, Action<string> handler);
    void UnsubscribeAll();
}
