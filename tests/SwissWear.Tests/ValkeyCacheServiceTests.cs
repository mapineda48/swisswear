using System.Text.Json;
using Moq;
using StackExchange.Redis;
using SwissWear.Domain.Contracts;
using SwissWear.Infrastructure.Services;

namespace SwissWear.Tests;

public class ValkeyCacheServiceTests
{
    private readonly Mock<IDatabase> _databaseMock;
    private readonly ValkeyCacheService _service;

    public ValkeyCacheServiceTests()
    {
        var connectionMock = new Mock<IConnectionMultiplexer>();
        _databaseMock = new Mock<IDatabase>();
        _databaseMock.DefaultValue = DefaultValue.Mock;

        connectionMock
            .Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_databaseMock.Object);

        _service = new ValkeyCacheService(connectionMock.Object);
    }

    // --- SetAsync ---

    [Fact]
    public async Task SetAsync_StoresSerializedValue()
    {
        var data = new TestData { Name = "Alice", Age = 30 };
        var expectedJson = JsonSerializer.Serialize(data);

        await _service.SetAsync("test-key", data);

        var invocation = Assert.Single(_databaseMock.Invocations,
            i => i.Method.Name == "StringSetAsync"
                 && i.Arguments[0].ToString() == "test-key");
        Assert.Equal(expectedJson, invocation.Arguments[1].ToString());
    }

    [Fact]
    public async Task SetAsync_WithExpiration_UsesExpirationOverload()
    {
        var expiration = TimeSpan.FromMinutes(5);

        await _service.SetAsync("key", "value", expiration);

        var invocation = Assert.Single(_databaseMock.Invocations,
            i => i.Method.Name == "StringSetAsync");
        Assert.Contains(invocation.Method.GetParameters(),
            p => p.ParameterType == typeof(Expiration));
    }

    [Fact]
    public async Task SetAsync_WithoutExpiration_CallsStringSet()
    {
        await _service.SetAsync("key", 42);

        var invocation = Assert.Single(_databaseMock.Invocations,
            i => i.Method.Name == "StringSetAsync");
        Assert.Equal("key", invocation.Arguments[0].ToString());
    }

    // --- GetAsync ---

    [Fact]
    public async Task GetAsync_ReturnsDeserializedValue_WhenKeyExists()
    {
        var data = new TestData { Name = "Bob", Age = 25 };
        var json = JsonSerializer.Serialize(data);

        _databaseMock
            .Setup(d => d.StringGetAsync("test-key", It.IsAny<CommandFlags>()))
            .ReturnsAsync(json);

        var result = await _service.GetAsync<TestData>("test-key");

        Assert.NotNull(result);
        Assert.Equal("Bob", result.Name);
        Assert.Equal(25, result.Age);
    }

    [Fact]
    public async Task GetAsync_ReturnsDefault_WhenKeyDoesNotExist()
    {
        _databaseMock
            .Setup(d => d.StringGetAsync("missing", It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var result = await _service.GetAsync<TestData>("missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ReturnsDefaultInt_WhenKeyDoesNotExist()
    {
        _databaseMock
            .Setup(d => d.StringGetAsync("missing", It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var result = await _service.GetAsync<int>("missing");

        Assert.Equal(0, result);
    }

    // --- RemoveAsync ---

    [Fact]
    public async Task RemoveAsync_DeletesKey()
    {
        _databaseMock.Setup(d => d.KeyDeleteAsync("k", It.IsAny<CommandFlags>())).ReturnsAsync(true);

        await _service.RemoveAsync("k");

        _databaseMock.Verify(d => d.KeyDeleteAsync("k", It.IsAny<CommandFlags>()), Times.Once);
    }

    // --- ExistsAsync ---

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenKeyExists()
    {
        _databaseMock.Setup(d => d.KeyExistsAsync("k", It.IsAny<CommandFlags>())).ReturnsAsync(true);

        Assert.True(await _service.ExistsAsync("k"));
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenKeyDoesNotExist()
    {
        _databaseMock.Setup(d => d.KeyExistsAsync("k", It.IsAny<CommandFlags>())).ReturnsAsync(false);

        Assert.False(await _service.ExistsAsync("k"));
    }

    // --- HashSetAsync ---

    [Fact]
    public async Task HashSetAsync_SetsFieldInHash()
    {
        await _service.HashSetAsync("myhash", "field1", "value1");

        _databaseMock.Verify(d => d.HashSetAsync(
            "myhash", (RedisValue)"field1", (RedisValue)"value1",
            It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    // --- HashGetAsync ---

    [Fact]
    public async Task HashGetAsync_ReturnsValue_WhenFieldExists()
    {
        _databaseMock.Setup(d => d.HashGetAsync("h", "f", It.IsAny<CommandFlags>())).ReturnsAsync("val");

        var result = await _service.HashGetAsync("h", "f");

        Assert.Equal("val", result);
    }

    [Fact]
    public async Task HashGetAsync_ReturnsNull_WhenFieldDoesNotExist()
    {
        _databaseMock.Setup(d => d.HashGetAsync("h", "f", It.IsAny<CommandFlags>())).ReturnsAsync(RedisValue.Null);

        var result = await _service.HashGetAsync("h", "f");

        Assert.Null(result);
    }

    // --- HashGetAllAsync ---

    [Fact]
    public async Task HashGetAllAsync_ReturnsDictionary()
    {
        _databaseMock.Setup(d => d.HashGetAllAsync("h", It.IsAny<CommandFlags>()))
            .ReturnsAsync([new HashEntry("a", "1"), new HashEntry("b", "2")]);

        var result = await _service.HashGetAllAsync("h");

        Assert.Equal(2, result.Count);
        Assert.Equal("1", result["a"]);
        Assert.Equal("2", result["b"]);
    }

    // --- HashRemoveAsync ---

    [Fact]
    public async Task HashRemoveAsync_DeletesField()
    {
        _databaseMock.Setup(d => d.HashDeleteAsync("h", "f", It.IsAny<CommandFlags>())).ReturnsAsync(true);

        await _service.HashRemoveAsync("h", "f");

        _databaseMock.Verify(d => d.HashDeleteAsync("h", (RedisValue)"f", It.IsAny<CommandFlags>()), Times.Once);
    }

    // --- ListPushAsync ---

    [Fact]
    public async Task ListPushAsync_PushesToList()
    {
        await _service.ListPushAsync("mylist", "item");

        _databaseMock.Verify(d => d.ListRightPushAsync(
            "mylist", (RedisValue)"item",
            It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    // --- ListRangeAsync ---

    [Fact]
    public async Task ListRangeAsync_ReturnsStrings()
    {
        _databaseMock.Setup(d => d.ListRangeAsync("l", 0, 9, It.IsAny<CommandFlags>()))
            .ReturnsAsync([(RedisValue)"a", (RedisValue)"b"]);

        var result = await _service.ListRangeAsync("l", 0, 9);

        Assert.Equal(2, result.Count);
        Assert.Equal("a", result[0]);
        Assert.Equal("b", result[1]);
    }

    // --- ListTrimAsync ---

    [Fact]
    public async Task ListTrimAsync_TrimsTheList()
    {
        await _service.ListTrimAsync("l", -200, -1);

        _databaseMock.Verify(d => d.ListTrimAsync("l", -200, -1, It.IsAny<CommandFlags>()), Times.Once);
    }

    // --- Round-trip ---

    [Fact]
    public async Task SetAndGet_RoundTripsComplexObject()
    {
        var data = new TestData { Name = "Charlie", Age = 40 };

        await _service.SetAsync("rt", data);

        var setInvocation = Assert.Single(_databaseMock.Invocations,
            i => i.Method.Name == "StringSetAsync");
        var storedJson = setInvocation.Arguments[1].ToString()!;

        _databaseMock.Setup(d => d.StringGetAsync("rt", It.IsAny<CommandFlags>())).ReturnsAsync(storedJson);

        var result = await _service.GetAsync<TestData>("rt");

        Assert.NotNull(result);
        Assert.Equal(data.Name, result.Name);
        Assert.Equal(data.Age, result.Age);
    }

    private class TestData
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }
}
