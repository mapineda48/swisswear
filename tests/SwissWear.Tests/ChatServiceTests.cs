using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using SwissWear.Domain.Contracts;
using SwissWear.Infrastructure.Services;

namespace SwissWear.Tests;

public class ChatServiceTests : IDisposable
{
    private readonly Mock<IChatAuditService> _auditMock;
    private readonly Mock<ICacheService> _cacheMock;
    private readonly Mock<IPubSubService> _pubSubMock;
    private readonly ChatService _service;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Capture pub/sub subscription callbacks by channel
    private readonly Dictionary<string, Action<string>> _subscriptions = new();

    public ChatServiceTests()
    {
        _auditMock = new Mock<IChatAuditService>();
        _auditMock.Setup(a => a.LogMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _cacheMock = new Mock<ICacheService>();
        _pubSubMock = new Mock<IPubSubService>();

        // Capture Subscribe calls
        _pubSubMock
            .Setup(p => p.Subscribe(It.IsAny<string>(), It.IsAny<Action<string>>()))
            .Callback<string, Action<string>>((channel, handler) => _subscriptions[channel] = handler);

        // Make Publish trigger the local subscription (simulates single-node pub/sub)
        _pubSubMock
            .Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((channel, message, _) =>
            {
                if (_subscriptions.TryGetValue(channel, out var handler))
                    handler(message);
                return Task.CompletedTask;
            });

        // Default async returns
        _cacheMock.Setup(c => c.HashSetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _cacheMock.Setup(c => c.HashRemoveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _cacheMock.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _cacheMock.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _cacheMock.Setup(c => c.ListPushAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _cacheMock.Setup(c => c.ListTrimAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _cacheMock.Setup(c => c.HashGetAsync("chat:users", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var logger = new Mock<ILogger<ChatService>>();
        _service = new ChatService(_cacheMock.Object, _pubSubMock.Object, _auditMock.Object, logger.Object);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    // --- RegisterUser / UnregisterUser ---

    [Fact]
    public async Task RegisterUserAsync_ReturnsUniqueId()
    {
        var id1 = await _service.RegisterUserAsync();
        var id2 = await _service.RegisterUserAsync();

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public async Task RegisterUserAsync_StoresInCache()
    {
        var id = await _service.RegisterUserAsync();

        _cacheMock.Verify(c => c.HashSetAsync(
            "chat:users",
            id,
            It.Is<string>(v => v.StartsWith("User-")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterUserAsync_RaisesOnUserJoined()
    {
        string? joinedId = null;
        _service.OnUserJoined += (id, _) => joinedId = id;

        var id = await _service.RegisterUserAsync();

        Assert.Equal(id, joinedId);
    }

    [Fact]
    public async Task RegisterUserAsync_PublishesToChannel()
    {
        await _service.RegisterUserAsync();

        _pubSubMock.Verify(p => p.PublishAsync(
            "chat:events:user_joined",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnregisterUserAsync_RemovesFromCache()
    {
        var id = await _service.RegisterUserAsync();

        await _service.UnregisterUserAsync(id);

        _cacheMock.Verify(c => c.HashRemoveAsync("chat:users", id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnregisterUserAsync_RaisesOnUserLeft()
    {
        var id = await _service.RegisterUserAsync();
        string? leftId = null;
        _service.OnUserLeft += uid => leftId = uid;

        await _service.UnregisterUserAsync(id);

        Assert.Equal(id, leftId);
    }

    [Fact]
    public async Task UnregisterUserAsync_CleansUpTypingKey()
    {
        var id = await _service.RegisterUserAsync();

        await _service.UnregisterUserAsync(id);

        _cacheMock.Verify(c => c.RemoveAsync($"chat:typing:{id}", It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // --- GetUserNameAsync ---

    [Fact]
    public async Task GetUserNameAsync_ReturnsName_WhenUserExists()
    {
        _cacheMock.Setup(c => c.HashGetAsync("chat:users", "user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("User-abc");

        var name = await _service.GetUserNameAsync("user1");

        Assert.Equal("User-abc", name);
    }

    [Fact]
    public async Task GetUserNameAsync_ReturnsUnknown_WhenUserDoesNotExist()
    {
        _cacheMock.Setup(c => c.HashGetAsync("chat:users", "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var name = await _service.GetUserNameAsync("nonexistent");

        Assert.Equal("Unknown", name);
    }

    // --- GetActiveUsersAsync ---

    [Fact]
    public async Task GetActiveUsersAsync_ReturnsAllUsersFromCache()
    {
        _cacheMock.Setup(c => c.HashGetAllAsync("chat:users", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                ["id1"] = "User-1",
                ["id2"] = "User-2"
            });

        var users = await _service.GetActiveUsersAsync();

        Assert.Equal(2, users.Count);
        Assert.Contains(users, u => u.Id == "id1" && u.Name == "User-1");
        Assert.Contains(users, u => u.Id == "id2" && u.Name == "User-2");
    }

    // --- SendMessageAsync ---

    [Fact]
    public async Task SendMessageAsync_StoresInCacheList()
    {
        SetupUserInHash("user1", "User-1");

        await _service.SendMessageAsync("user1", "Hello");

        _cacheMock.Verify(c => c.ListPushAsync(
            "chat:messages",
            It.Is<string>(v => v.Contains("Hello")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_TrimsListToMaxMessages()
    {
        SetupUserInHash("user1", "User-1");

        await _service.SendMessageAsync("user1", "Hello");

        _cacheMock.Verify(c => c.ListTrimAsync("chat:messages", -200, -1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_IgnoresWhitespace()
    {
        await _service.SendMessageAsync("user1", "   ");

        _cacheMock.Verify(c => c.ListPushAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendMessageAsync_TrimsText()
    {
        SetupUserInHash("user1", "User-1");
        ChatMessage? received = null;
        _service.OnMessageReceived += msg => received = msg;

        await _service.SendMessageAsync("user1", "  Hello  ");

        Assert.NotNull(received);
        Assert.Equal("Hello", received.Text);
    }

    [Fact]
    public async Task SendMessageAsync_RaisesOnMessageReceived()
    {
        SetupUserInHash("user1", "User-1");
        ChatMessage? received = null;
        _service.OnMessageReceived += msg => received = msg;

        await _service.SendMessageAsync("user1", "Test");

        Assert.NotNull(received);
        Assert.Equal("Test", received.Text);
    }

    [Fact]
    public async Task SendMessageAsync_PublishesToChannel()
    {
        SetupUserInHash("user1", "User-1");

        await _service.SendMessageAsync("user1", "Hello");

        _pubSubMock.Verify(p => p.PublishAsync(
            "chat:events:message",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_WithClientInfo_CallsAuditService()
    {
        SetupUserInHash("user1", "User-1");
        var clientInfo = new ClientInfo("127.0.0.1", "TestAgent");

        await _service.SendMessageAsync("user1", "Hello", clientInfo);

        _auditMock.Verify(a => a.LogMessageAsync(
            It.Is<ChatMessage>(m => m.Text == "Hello"),
            "127.0.0.1",
            "TestAgent"), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_WithoutClientInfo_DoesNotCallAuditService()
    {
        SetupUserInHash("user1", "User-1");

        await _service.SendMessageAsync("user1", "Hello");

        _auditMock.Verify(a => a.LogMessageAsync(
            It.IsAny<ChatMessage>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task SendMessageAsync_ClearsTypingKey()
    {
        SetupUserInHash("user1", "User-1");

        await _service.SendMessageAsync("user1", "Hello");

        _cacheMock.Verify(c => c.RemoveAsync($"chat:typing:user1", It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- SendImagesAsync ---

    [Fact]
    public async Task SendImagesAsync_StoresImageMessage()
    {
        SetupUserInHash("user1", "User-1");
        ChatMessage? received = null;
        _service.OnMessageReceived += msg => received = msg;
        var images = new List<ImageData> { new("base64data", "image/png") };

        await _service.SendImagesAsync("user1", images, "caption");

        Assert.NotNull(received);
        Assert.Equal(MessageType.Image, received.Type);
        Assert.Equal("caption", received.Text);
    }

    [Fact]
    public async Task SendImagesAsync_IgnoresEmptyList()
    {
        await _service.SendImagesAsync("user1", new List<ImageData>());

        _cacheMock.Verify(c => c.ListPushAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- SendMediaAsync ---

    [Fact]
    public async Task SendMediaAsync_StoresAudioMessage()
    {
        SetupUserInHash("user1", "User-1");
        ChatMessage? received = null;
        _service.OnMessageReceived += msg => received = msg;

        await _service.SendMediaAsync("user1", MessageType.Audio, "audiodata", "audio/webm", "note");

        Assert.NotNull(received);
        Assert.Equal(MessageType.Audio, received.Type);
        Assert.Equal("audiodata", received.MediaData);
        Assert.Equal("audio/webm", received.MediaContentType);
    }

    [Fact]
    public async Task SendMediaAsync_IgnoresEmptyData()
    {
        await _service.SendMediaAsync("user1", MessageType.Audio, "", "audio/webm");

        _cacheMock.Verify(c => c.ListPushAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- SetTypingAsync ---

    [Fact]
    public async Task SetTypingAsync_True_SetsKeyWithExpiry()
    {
        SetupUserInHash("user1", "User-1");

        await _service.SetTypingAsync("user1", true);

        _cacheMock.Verify(c => c.SetAsync(
            "chat:typing:user1",
            "1",
            TimeSpan.FromSeconds(5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetTypingAsync_False_DeletesKey()
    {
        SetupUserInHash("user1", "User-1");

        await _service.SetTypingAsync("user1", false);

        _cacheMock.Verify(c => c.RemoveAsync("chat:typing:user1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetTypingAsync_PublishesToChannel()
    {
        SetupUserInHash("user1", "User-1");

        await _service.SetTypingAsync("user1", true);

        _pubSubMock.Verify(p => p.PublishAsync(
            "chat:events:typing",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetTypingAsync_RaisesOnTypingChanged()
    {
        SetupUserInHash("user1", "User-1");
        string? typingUserId = null;
        bool? isTyping = null;
        _service.OnTypingChanged += (uid, _, typing) =>
        {
            typingUserId = uid;
            isTyping = typing;
        };

        await _service.SetTypingAsync("user1", true);

        Assert.Equal("user1", typingUserId);
        Assert.True(isTyping);
    }

    // --- GetTypingUsersAsync ---

    [Fact]
    public async Task GetTypingUsersAsync_ReturnsOnlyUsersWithActiveTypingKey()
    {
        _cacheMock.Setup(c => c.HashGetAllAsync("chat:users", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                ["id1"] = "User-1",
                ["id2"] = "User-2"
            });

        _cacheMock.Setup(c => c.ExistsAsync("chat:typing:id1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _cacheMock.Setup(c => c.ExistsAsync("chat:typing:id2", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var typing = await _service.GetTypingUsersAsync();

        Assert.Single(typing);
        Assert.Equal("id1", typing[0].Id);
    }

    // --- GetRecentMessagesAsync ---

    [Fact]
    public async Task GetRecentMessagesAsync_ReturnsMessagesFromCache()
    {
        var msg = new ChatMessage("u1", "User-1", "Hello", DateTime.UtcNow);
        var json = JsonSerializer.Serialize(msg, JsonOptions);

        _cacheMock.Setup(c => c.ListRangeAsync("chat:messages", 0, 199, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { json });

        var messages = await _service.GetRecentMessagesAsync();

        Assert.Single(messages);
        Assert.Equal("Hello", messages[0].Text);
    }

    // --- Event safety ---

    [Fact]
    public async Task EventHandler_Exception_DoesNotCrash()
    {
        SetupUserInHash("user1", "User-1");
        _service.OnMessageReceived += _ => throw new InvalidOperationException("Boom");

        await _service.SendMessageAsync("user1", "Hello");

        // Should not throw - verify message was still stored
        _cacheMock.Verify(c => c.ListPushAsync(
            "chat:messages",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- Helpers ---

    private void SetupUserInHash(string userId, string userName)
    {
        _cacheMock.Setup(c => c.HashGetAsync("chat:users", userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userName);
    }
}
