using Microsoft.Extensions.Logging;
using Moq;
using SwissWear.Web.Services;

namespace SwissWear.Tests;

public class ChatServiceTests
{
    private readonly Mock<IChatAuditService> _auditMock;
    private readonly ChatService _service;

    public ChatServiceTests()
    {
        _auditMock = new Mock<IChatAuditService>();
        _auditMock.Setup(a => a.LogMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        var logger = new Mock<ILogger<ChatService>>();
        _service = new ChatService(_auditMock.Object, logger.Object);
    }

    // --- RegisterUser / UnregisterUser ---

    [Fact]
    public void RegisterUser_ReturnsUniqueId()
    {
        var id1 = _service.RegisterUser();
        var id2 = _service.RegisterUser();

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void RegisterUser_AddsToActiveUsers()
    {
        var id = _service.RegisterUser();

        var users = _service.GetActiveUsers();
        Assert.Single(users);
        Assert.Equal(id, users[0].Id);
    }

    [Fact]
    public void RegisterUser_RaisesOnUserJoined()
    {
        string? joinedId = null;
        _service.OnUserJoined += (id, _) => joinedId = id;

        var id = _service.RegisterUser();

        Assert.Equal(id, joinedId);
    }

    [Fact]
    public void UnregisterUser_RemovesFromActiveUsers()
    {
        var id = _service.RegisterUser();

        _service.UnregisterUser(id);

        Assert.Empty(_service.GetActiveUsers());
    }

    [Fact]
    public void UnregisterUser_RaisesOnUserLeft()
    {
        var id = _service.RegisterUser();
        string? leftId = null;
        _service.OnUserLeft += uid => leftId = uid;

        _service.UnregisterUser(id);

        Assert.Equal(id, leftId);
    }

    // --- GetUserName ---

    [Fact]
    public void GetUserName_ReturnsName_WhenUserExists()
    {
        var id = _service.RegisterUser();
        var name = _service.GetUserName(id);

        Assert.StartsWith("User-", name);
    }

    [Fact]
    public void GetUserName_ReturnsUnknown_WhenUserDoesNotExist()
    {
        var name = _service.GetUserName("nonexistent");

        Assert.Equal("Unknown", name);
    }

    // --- SendMessage ---

    [Fact]
    public void SendMessage_AddsToRecentMessages()
    {
        var id = _service.RegisterUser();

        _service.SendMessage(id, "Hello");

        var messages = _service.GetRecentMessages();
        Assert.Single(messages);
        Assert.Equal("Hello", messages[0].Text);
        Assert.Equal(MessageType.Text, messages[0].Type);
    }

    [Fact]
    public void SendMessage_IgnoresWhitespace()
    {
        var id = _service.RegisterUser();

        _service.SendMessage(id, "   ");

        Assert.Empty(_service.GetRecentMessages());
    }

    [Fact]
    public void SendMessage_TrimsText()
    {
        var id = _service.RegisterUser();

        _service.SendMessage(id, "  Hello  ");

        Assert.Equal("Hello", _service.GetRecentMessages()[0].Text);
    }

    [Fact]
    public void SendMessage_RaisesOnMessageReceived()
    {
        var id = _service.RegisterUser();
        ChatMessage? received = null;
        _service.OnMessageReceived += msg => received = msg;

        _service.SendMessage(id, "Test");

        Assert.NotNull(received);
        Assert.Equal("Test", received.Text);
    }

    [Fact]
    public void SendMessage_WithClientInfo_CallsAuditService()
    {
        var id = _service.RegisterUser();
        var clientInfo = new ClientInfo("127.0.0.1", "TestAgent");

        _service.SendMessage(id, "Hello", clientInfo);

        _auditMock.Verify(a => a.LogMessageAsync(
            It.Is<ChatMessage>(m => m.Text == "Hello"),
            "127.0.0.1",
            "TestAgent"), Times.Once);
    }

    [Fact]
    public void SendMessage_WithoutClientInfo_DoesNotCallAuditService()
    {
        var id = _service.RegisterUser();

        _service.SendMessage(id, "Hello");

        _auditMock.Verify(a => a.LogMessageAsync(
            It.IsAny<ChatMessage>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void SendMessage_ClearsTypingStatus()
    {
        var id = _service.RegisterUser();
        _service.SetTyping(id, true);

        bool? typingChanged = null;
        _service.OnTypingChanged += (uid, _, isTyping) =>
        {
            if (uid == id) typingChanged = isTyping;
        };

        _service.SendMessage(id, "Hello");

        Assert.False(typingChanged);
    }

    // --- SendImages ---

    [Fact]
    public void SendImages_AddsImageMessage()
    {
        var id = _service.RegisterUser();
        var images = new List<ImageData> { new("base64data", "image/png") };

        _service.SendImages(id, images, "caption");

        var messages = _service.GetRecentMessages();
        Assert.Single(messages);
        Assert.Equal(MessageType.Image, messages[0].Type);
        Assert.Equal("caption", messages[0].Text);
        Assert.Single(messages[0].Images!);
    }

    [Fact]
    public void SendImages_IgnoresEmptyList()
    {
        var id = _service.RegisterUser();

        _service.SendImages(id, new List<ImageData>());

        Assert.Empty(_service.GetRecentMessages());
    }

    // --- SendMedia ---

    [Fact]
    public void SendMedia_AddsAudioMessage()
    {
        var id = _service.RegisterUser();

        _service.SendMedia(id, MessageType.Audio, "audiodata", "audio/webm", "note");

        var messages = _service.GetRecentMessages();
        Assert.Single(messages);
        Assert.Equal(MessageType.Audio, messages[0].Type);
        Assert.Equal("audiodata", messages[0].MediaData);
        Assert.Equal("audio/webm", messages[0].MediaContentType);
    }

    [Fact]
    public void SendMedia_IgnoresEmptyData()
    {
        var id = _service.RegisterUser();

        _service.SendMedia(id, MessageType.Audio, "", "audio/webm");

        Assert.Empty(_service.GetRecentMessages());
    }

    // --- SetTyping ---

    [Fact]
    public void SetTyping_RaisesOnTypingChanged()
    {
        var id = _service.RegisterUser();
        string? typingUserId = null;
        bool? isTyping = null;
        _service.OnTypingChanged += (uid, _, typing) =>
        {
            typingUserId = uid;
            isTyping = typing;
        };

        _service.SetTyping(id, true);

        Assert.Equal(id, typingUserId);
        Assert.True(isTyping);
    }

    [Fact]
    public void GetTypingUsers_ReturnsCurrentlyTyping()
    {
        var id = _service.RegisterUser();
        _service.SetTyping(id, true);

        var typing = _service.GetTypingUsers();

        Assert.Single(typing);
        Assert.Equal(id, typing[0].Id);
    }

    [Fact]
    public void GetTypingUsers_ExcludesStoppedTyping()
    {
        var id = _service.RegisterUser();
        _service.SetTyping(id, true);
        _service.SetTyping(id, false);

        Assert.Empty(_service.GetTypingUsers());
    }

    // --- Message queue limit ---

    [Fact]
    public void EnqueueMessage_RespectMaxMessages()
    {
        var id = _service.RegisterUser();

        for (var i = 0; i < 210; i++)
            _service.SendMessage(id, $"msg-{i}");

        var messages = _service.GetRecentMessages();
        Assert.Equal(200, messages.Count);
        Assert.Equal("msg-10", messages[0].Text);
    }

    // --- Event safety ---

    [Fact]
    public void EventHandler_Exception_DoesNotCrash()
    {
        _service.OnMessageReceived += _ => throw new InvalidOperationException("Boom");

        var id = _service.RegisterUser();

        // Should not throw
        _service.SendMessage(id, "Hello");

        Assert.Single(_service.GetRecentMessages());
    }
}
