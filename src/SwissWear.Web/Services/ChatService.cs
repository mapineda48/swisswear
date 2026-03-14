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

public class ChatService
{
    private readonly ConcurrentQueue<ChatMessage> _messages = new();
    private const int MaxMessages = 200;
    private readonly ChatAuditService _auditService;

    public event Action<ChatMessage>? OnMessageReceived;
    public event Action<string, string>? OnUserJoined;
    public event Action<string>? OnUserLeft;
    public event Action<string, string, bool>? OnTypingChanged;

    private readonly ConcurrentDictionary<string, string> _activeUsers = new();
    private readonly ConcurrentDictionary<string, DateTime> _typingUsers = new();

    public ChatService(ChatAuditService auditService)
    {
        _auditService = auditService;
    }

    public string RegisterUser()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var name = $"User-{id}";
        _activeUsers[id] = name;
        OnUserJoined?.Invoke(id, name);
        return id;
    }

    public void UnregisterUser(string userId)
    {
        _activeUsers.TryRemove(userId, out _);
        OnUserLeft?.Invoke(userId);
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

        OnTypingChanged?.Invoke(userId, GetUserName(userId), isTyping);
    }

    public IReadOnlyList<(string Id, string Name)> GetTypingUsers()
        => _typingUsers
            .Where(kv => (DateTime.UtcNow - kv.Value).TotalSeconds < 5)
            .Select(kv => (kv.Key, GetUserName(kv.Key)))
            .ToList();

    public void SendMessage(string userId, string text, ClientInfo? clientInfo = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        _typingUsers.TryRemove(userId, out _);
        OnTypingChanged?.Invoke(userId, GetUserName(userId), false);

        var message = new ChatMessage(userId, GetUserName(userId), text.Trim(), DateTime.UtcNow);
        EnqueueMessage(message, clientInfo);
    }

    public void SendImages(string userId, IReadOnlyList<ImageData> images, string? caption = null, ClientInfo? clientInfo = null)
    {
        if (images.Count == 0) return;

        _typingUsers.TryRemove(userId, out _);
        OnTypingChanged?.Invoke(userId, GetUserName(userId), false);

        var message = new ChatMessage(userId, GetUserName(userId), caption?.Trim() ?? "", DateTime.UtcNow, MessageType.Image, Images: images);
        EnqueueMessage(message, clientInfo);
    }

    public void SendMedia(string userId, MessageType type, string base64Data, string contentType, string? caption = null, ClientInfo? clientInfo = null)
    {
        if (string.IsNullOrEmpty(base64Data)) return;

        _typingUsers.TryRemove(userId, out _);
        OnTypingChanged?.Invoke(userId, GetUserName(userId), false);

        var message = new ChatMessage(userId, GetUserName(userId), caption?.Trim() ?? "", DateTime.UtcNow, type, base64Data, contentType);
        EnqueueMessage(message, clientInfo);
    }

    private void EnqueueMessage(ChatMessage message, ClientInfo? clientInfo)
    {
        _messages.Enqueue(message);

        while (_messages.Count > MaxMessages)
            _messages.TryDequeue(out _);

        OnMessageReceived?.Invoke(message);

        if (clientInfo is not null)
        {
            _ = _auditService.LogMessageAsync(message, clientInfo.IpAddress, clientInfo.UserAgent);
        }
    }

    public IReadOnlyList<ChatMessage> GetRecentMessages()
        => _messages.ToArray();
}
