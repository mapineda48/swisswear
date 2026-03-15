using StackExchange.Redis;
using SwissWear.Domain.Contracts;

namespace SwissWear.Infrastructure.Services;

public class ValkeyPubSubService : IPubSubService
{
    private readonly ISubscriber _subscriber;

    public ValkeyPubSubService(IConnectionMultiplexer connection)
    {
        _subscriber = connection.GetSubscriber();
    }

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
