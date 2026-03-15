using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwissWear.Domain.Contracts;

namespace SwissWear.Infrastructure.Services;

public class ChatService : IChatService, IDisposable
{
    private const int MaxMessages = 200;
    private const string MessagesKey = "chat:messages";
    private const string UsersKey = "chat:users";
    private const string TypingKeyPrefix = "chat:typing:";
    private const string ChannelMessage = "chat:events:message";
    private const string ChannelUserJoined = "chat:events:user_joined";
    private const string ChannelUserLeft = "chat:events:user_left";
    private const string ChannelTyping = "chat:events:typing";

    private static readonly TimeSpan TypingExpiry = TimeSpan.FromSeconds(5);

    private readonly ICacheService _cache;
    private readonly IPubSubService _pubSub;
    private readonly IChatAuditService _auditService;
    private readonly ILogger<ChatService> _logger;

    public event Action<ChatMessage>? OnMessageReceived;
    public event Action<string, string>? OnUserJoined;
    public event Action<string>? OnUserLeft;
    public event Action<string, string, bool>? OnTypingChanged;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ChatService(ICacheService cache, IPubSubService pubSub, IChatAuditService auditService, ILogger<ChatService> logger)
    {
        _cache = cache;
        _pubSub = pubSub;
        _auditService = auditService;
        _logger = logger;

        SubscribeToChannels();
    }

    private void SubscribeToChannels()
    {
        _pubSub.Subscribe(ChannelMessage, value =>
        {
            var msg = Deserialize<ChatMessage>(value);
            if (msg is not null) RaiseEvent(OnMessageReceived, msg);
        });

        _pubSub.Subscribe(ChannelUserJoined, value =>
        {
            var data = Deserialize<UserEvent>(value);
            if (data is not null) RaiseEvent(OnUserJoined, data.UserId, data.UserName);
        });

        _pubSub.Subscribe(ChannelUserLeft, value =>
        {
            RaiseEvent(OnUserLeft, value);
        });

        _pubSub.Subscribe(ChannelTyping, value =>
        {
            var data = Deserialize<TypingEvent>(value);
            if (data is not null) RaiseEvent(OnTypingChanged, data.UserId, data.UserName, data.IsTyping);
        });
    }

    public async Task<string> RegisterUserAsync()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var name = $"User-{id}";
        await _cache.HashSetAsync(UsersKey, id, name);
        await _pubSub.PublishAsync(ChannelUserJoined, Serialize(new UserEvent(id, name)));
        return id;
    }

    public async Task UnregisterUserAsync(string userId)
    {
        await _cache.HashRemoveAsync(UsersKey, userId);
        await _cache.RemoveAsync(TypingKeyPrefix + userId);
        await _pubSub.PublishAsync(ChannelUserLeft, userId);
    }

    public async Task<string> GetUserNameAsync(string userId)
    {
        var name = await _cache.HashGetAsync(UsersKey, userId);
        return name ?? "Unknown";
    }

    public async Task<IReadOnlyList<(string Id, string Name)>> GetActiveUsersAsync()
    {
        var entries = await _cache.HashGetAllAsync(UsersKey);
        return entries.Select(e => (e.Key, e.Value)).ToList();
    }

    public async Task SetTypingAsync(string userId, bool isTyping)
    {
        var key = TypingKeyPrefix + userId;

        if (isTyping)
            await _cache.SetAsync(key, "1", TypingExpiry);
        else
            await _cache.RemoveAsync(key);

        var userName = await GetUserNameAsync(userId);
        await _pubSub.PublishAsync(ChannelTyping, Serialize(new TypingEvent(userId, userName, isTyping)));
    }

    public async Task<IReadOnlyList<(string Id, string Name)>> GetTypingUsersAsync()
    {
        var users = await _cache.HashGetAllAsync(UsersKey);
        var result = new List<(string, string)>();

        foreach (var user in users)
        {
            if (await _cache.ExistsAsync(TypingKeyPrefix + user.Key))
                result.Add((user.Key, user.Value));
        }

        return result;
    }

    public async Task SendMessageAsync(string userId, string text, ClientInfo? clientInfo = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        await ClearTypingAsync(userId);
        var userName = await GetUserNameAsync(userId);
        var message = new ChatMessage(userId, userName, text.Trim(), DateTime.UtcNow);
        await EnqueueMessageAsync(message, clientInfo);
    }

    public async Task SendImagesAsync(string userId, IReadOnlyList<ImageData> images, string? caption = null, ClientInfo? clientInfo = null)
    {
        if (images.Count == 0) return;

        await ClearTypingAsync(userId);
        var userName = await GetUserNameAsync(userId);
        var message = new ChatMessage(userId, userName, caption?.Trim() ?? "", DateTime.UtcNow, MessageType.Image, Images: images);
        await EnqueueMessageAsync(message, clientInfo);
    }

    public async Task SendMediaAsync(string userId, MessageType type, string base64Data, string contentType, string? caption = null, ClientInfo? clientInfo = null)
    {
        if (string.IsNullOrEmpty(base64Data)) return;

        await ClearTypingAsync(userId);
        var userName = await GetUserNameAsync(userId);
        var message = new ChatMessage(userId, userName, caption?.Trim() ?? "", DateTime.UtcNow, type, base64Data, contentType);
        await EnqueueMessageAsync(message, clientInfo);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync()
    {
        var values = await _cache.ListRangeAsync(MessagesKey, 0, MaxMessages - 1);
        return values
            .Select(v => Deserialize<ChatMessage>(v))
            .Where(m => m is not null)
            .ToList()!;
    }

    private async Task ClearTypingAsync(string userId)
    {
        await _cache.RemoveAsync(TypingKeyPrefix + userId);
        var userName = await GetUserNameAsync(userId);
        await _pubSub.PublishAsync(ChannelTyping, Serialize(new TypingEvent(userId, userName, false)));
    }

    private async Task EnqueueMessageAsync(ChatMessage message, ClientInfo? clientInfo)
    {
        var json = Serialize(message);
        await _cache.ListPushAsync(MessagesKey, json);
        await _cache.ListTrimAsync(MessagesKey, -MaxMessages, -1);
        await _pubSub.PublishAsync(ChannelMessage, json);

        if (clientInfo is not null)
        {
            _ = _auditService.LogMessageAsync(message, clientInfo.IpAddress, clientInfo.UserAgent);
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static T? Deserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch { return default; }
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

    public void Dispose()
    {
        _pubSub.UnsubscribeAll();
    }

    private record UserEvent(string UserId, string UserName);
    private record TypingEvent(string UserId, string UserName, bool IsTyping);
}
