using System.Text.Json;
using Moq;
using StackExchange.Redis;
using SwissWear.Web.Services;

namespace SwissWear.Tests;

public class ValkeyCacheServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _connectionMock;
    private readonly Mock<IDatabase> _databaseMock;
    private readonly ValkeyCacheService _service;

    public ValkeyCacheServiceTests()
    {
        _connectionMock = new Mock<IConnectionMultiplexer>();
        _databaseMock = new Mock<IDatabase>();
        _databaseMock.DefaultValue = DefaultValue.Mock;

        _connectionMock
            .Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_databaseMock.Object);

        _service = new ValkeyCacheService(_connectionMock.Object);
    }

    // --- SetAsync ---

    [Fact]
    public async Task SetAsync_StoresSerializedValue()
    {
        var data = new TestData { Name = "Alice", Age = 30 };
        var expectedJson = JsonSerializer.Serialize(data);

        await _service.SetAsync("test-key", data);

        // Verify that some StringSetAsync overload was called with the correct key and serialized json
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

        // The Expiration overload has an Expiration parameter (not TimeSpan?)
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

    [Fact]
    public async Task GetAsync_HandlesStringValues()
    {
        var json = JsonSerializer.Serialize("hello world");

        _databaseMock
            .Setup(d => d.StringGetAsync("str-key", It.IsAny<CommandFlags>()))
            .ReturnsAsync(json);

        var result = await _service.GetAsync<string>("str-key");

        Assert.Equal("hello world", result);
    }

    // --- RemoveAsync ---

    [Fact]
    public async Task RemoveAsync_DeletesKey()
    {
        _databaseMock
            .Setup(d => d.KeyDeleteAsync("delete-me", It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        await _service.RemoveAsync("delete-me");

        _databaseMock.Verify(d => d.KeyDeleteAsync("delete-me", It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RemoveAsync_NonExistentKey_DoesNotThrow()
    {
        _databaseMock
            .Setup(d => d.KeyDeleteAsync("nope", It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        await _service.RemoveAsync("nope");

        _databaseMock.Verify(d => d.KeyDeleteAsync("nope", It.IsAny<CommandFlags>()), Times.Once);
    }

    // --- ExistsAsync ---

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenKeyExists()
    {
        _databaseMock
            .Setup(d => d.KeyExistsAsync("exists", It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var result = await _service.ExistsAsync("exists");

        Assert.True(result);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenKeyDoesNotExist()
    {
        _databaseMock
            .Setup(d => d.KeyExistsAsync("missing", It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var result = await _service.ExistsAsync("missing");

        Assert.False(result);
    }

    // --- Serialization round-trip ---

    [Fact]
    public async Task SetAndGet_RoundTripsComplexObject()
    {
        var data = new TestData { Name = "Charlie", Age = 40 };

        await _service.SetAsync("round-trip", data);

        // Capture what was stored
        var setInvocation = Assert.Single(_databaseMock.Invocations,
            i => i.Method.Name == "StringSetAsync");
        var storedJson = setInvocation.Arguments[1].ToString()!;

        // Setup get to return the stored value
        _databaseMock
            .Setup(d => d.StringGetAsync("round-trip", It.IsAny<CommandFlags>()))
            .ReturnsAsync(storedJson);

        var result = await _service.GetAsync<TestData>("round-trip");

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
