namespace SwissWear.Domain.Contracts;

public interface IChatService
{
    event Action<ChatMessage>? OnMessageReceived;
    event Action<string, string>? OnUserJoined;
    event Action<string>? OnUserLeft;
    event Action<string, string, bool>? OnTypingChanged;

    Task<string> RegisterUserAsync();
    Task UnregisterUserAsync(string userId);
    Task<string> GetUserNameAsync(string userId);
    Task<IReadOnlyList<(string Id, string Name)>> GetActiveUsersAsync();
    Task SetTypingAsync(string userId, bool isTyping);
    Task<IReadOnlyList<(string Id, string Name)>> GetTypingUsersAsync();
    Task SendMessageAsync(string userId, string text, ClientInfo? clientInfo = null);
    Task SendImagesAsync(string userId, IReadOnlyList<ImageData> images, string? caption = null, ClientInfo? clientInfo = null);
    Task SendMediaAsync(string userId, MessageType type, string base64Data, string contentType, string? caption = null, ClientInfo? clientInfo = null);
    Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync();
}
