using System.Collections.Concurrent;

namespace SwissWear.Web.Services;

public enum MessageType { Text, Image, Audio }

public record ImageData(string Base64, string ContentType);

public record ChatMessage(
    string UserId,
    string UserName,
    string Text,
    DateTime Timestamp,
    MessageType Type = MessageType.Text,
    string? MediaData = null,
    string? MediaContentType = null,
    IReadOnlyList<ImageData>? Images = null);

public record ClientInfo(string IpAddress, string? UserAgent);

public class ChatService : IChatService
{
    private readonly ConcurrentQueue<ChatMessage> _messages = new();
    private const int MaxMessages = 200;
    private readonly IChatAuditService _auditService;
    private readonly ILogger<ChatService> _logger;

    public event Action<ChatMessage>? OnMessageReceived;
    public event Action<string, string>? OnUserJoined;
    public event Action<string>? OnUserLeft;
    public event Action<string, string, bool>? OnTypingChanged;

    private readonly ConcurrentDictionary<string, string> _activeUsers = new();
    private readonly ConcurrentDictionary<string, DateTime> _typingUsers = new();

    public ChatService(IChatAuditService auditService, ILogger<ChatService> logger)
    {
        _auditService = auditService;
        _logger = logger;
    }

    public string RegisterUser()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var name = $"User-{id}";
        _activeUsers[id] = name;
        RaiseEvent(OnUserJoined, id, name);
        return id;
    }

    public void UnregisterUser(string userId)
    {
        _activeUsers.TryRemove(userId, out _);
        RaiseEvent(OnUserLeft, userId);
    }

    public string GetUserName(string userId)
        => _activeUsers.TryGetValue(userId, out var name) ? name : "Unknown";

    public IReadOnlyList<(string Id, string Name)> GetActiveUsers()
        => _activeUsers.Select(kv => (kv.Key, kv.Value)).ToList();

    public void SetTyping(string userId, bool isTyping)
    {
        if (isTyping)
            _typingUsers[userId] = DateTime.UtcNow;
        else
            _typingUsers.TryRemove(userId, out _);

        RaiseEvent(OnTypingChanged, userId, GetUserName(userId), isTyping);
    }

    public IReadOnlyList<(string Id, string Name)> GetTypingUsers()
        => _typingUsers
            .Where(kv => (DateTime.UtcNow - kv.Value).TotalSeconds < 5)
            .Select(kv => (kv.Key, GetUserName(kv.Key)))
            .ToList();

    public void SendMessage(string userId, string text, ClientInfo? clientInfo = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        ClearTyping(userId);
        var message = new ChatMessage(userId, GetUserName(userId), text.Trim(), DateTime.UtcNow);
        EnqueueMessage(message, clientInfo);
    }

    public void SendImages(string userId, IReadOnlyList<ImageData> images, string? caption = null, ClientInfo? clientInfo = null)
    {
        if (images.Count == 0) return;

        ClearTyping(userId);
        var message = new ChatMessage(userId, GetUserName(userId), caption?.Trim() ?? "", DateTime.UtcNow, MessageType.Image, Images: images);
        EnqueueMessage(message, clientInfo);
    }

    public void SendMedia(string userId, MessageType type, string base64Data, string contentType, string? caption = null, ClientInfo? clientInfo = null)
    {
        if (string.IsNullOrEmpty(base64Data)) return;

        ClearTyping(userId);
        var message = new ChatMessage(userId, GetUserName(userId), caption?.Trim() ?? "", DateTime.UtcNow, type, base64Data, contentType);
        EnqueueMessage(message, clientInfo);
    }

    public IReadOnlyList<ChatMessage> GetRecentMessages()
        => _messages.ToArray();

    private void ClearTyping(string userId)
    {
        _typingUsers.TryRemove(userId, out _);
        RaiseEvent(OnTypingChanged, userId, GetUserName(userId), false);
    }

    private void EnqueueMessage(ChatMessage message, ClientInfo? clientInfo)
    {
        _messages.Enqueue(message);

        while (_messages.Count > MaxMessages)
            _messages.TryDequeue(out _);

        RaiseEvent(OnMessageReceived, message);

        if (clientInfo is not null)
        {
            _ = _auditService.LogMessageAsync(message, clientInfo.IpAddress, clientInfo.UserAgent);
        }
    }

    private void RaiseEvent<T>(Action<T>? handler, T arg)
    {
        if (handler is null) return;
        try { handler.Invoke(arg); }
        catch (Exception ex) { _logger.LogError(ex, "Chat event handler failed"); }
    }

    private void RaiseEvent<T1, T2>(Action<T1, T2>? handler, T1 arg1, T2 arg2)
    {
        if (handler is null) return;
        try { handler.Invoke(arg1, arg2); }
        catch (Exception ex) { _logger.LogError(ex, "Chat event handler failed"); }
    }

    private void RaiseEvent<T1, T2, T3>(Action<T1, T2, T3>? handler, T1 arg1, T2 arg2, T3 arg3)
    {
        if (handler is null) return;
        try { handler.Invoke(arg1, arg2, arg3); }
        catch (Exception ex) { _logger.LogError(ex, "Chat event handler failed"); }
    }
}
