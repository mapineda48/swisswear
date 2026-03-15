using Moq;
using StackExchange.Redis;
using SwissWear.Web.Services;

namespace SwissWear.Tests;

public class ValkeyPubSubServiceTests
{
    private readonly Mock<ISubscriber> _subscriberMock;
    private readonly ValkeyPubSubService _service;

    public ValkeyPubSubServiceTests()
    {
        _subscriberMock = new Mock<ISubscriber>();

        var connectionMock = new Mock<IConnectionMultiplexer>();
        connectionMock
            .Setup(c => c.GetSubscriber(It.IsAny<object>()))
            .Returns(_subscriberMock.Object);

        _service = new ValkeyPubSubService(connectionMock.Object);
    }

    [Fact]
    public async Task PublishAsync_PublishesToChannel()
    {
        await _service.PublishAsync("my-channel", "hello");

        _subscriberMock.Verify(s => s.PublishAsync(
            RedisChannel.Literal("my-channel"),
            (RedisValue)"hello",
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public void Subscribe_RegistersHandler()
    {
        _service.Subscribe("my-channel", _ => { });

        _subscriberMock.Verify(s => s.Subscribe(
            RedisChannel.Literal("my-channel"),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public void Subscribe_InvokesHandlerOnMessage()
    {
        Action<RedisChannel, RedisValue>? capturedHandler = null;

        _subscriberMock
            .Setup(s => s.Subscribe(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((_, handler, _) => capturedHandler = handler);

        string? received = null;
        _service.Subscribe("ch", v => received = v);

        Assert.NotNull(capturedHandler);
        capturedHandler!(RedisChannel.Literal("ch"), "test-message");

        Assert.Equal("test-message", received);
    }

    [Fact]
    public void UnsubscribeAll_CallsUnsubscribeAll()
    {
        _service.UnsubscribeAll();

        _subscriberMock.Verify(s => s.UnsubscribeAll(It.IsAny<CommandFlags>()), Times.Once);
    }
}
