namespace SwissWear.Web.Services;

public interface IChatService
{
    event Action<ChatMessage>? OnMessageReceived;
    event Action<string, string>? OnUserJoined;
    event Action<string>? OnUserLeft;
    event Action<string, string, bool>? OnTypingChanged;

    string RegisterUser();
    void UnregisterUser(string userId);
    string GetUserName(string userId);
    IReadOnlyList<(string Id, string Name)> GetActiveUsers();
    void SetTyping(string userId, bool isTyping);
    IReadOnlyList<(string Id, string Name)> GetTypingUsers();
    void SendMessage(string userId, string text, ClientInfo? clientInfo = null);
    void SendImages(string userId, IReadOnlyList<ImageData> images, string? caption = null, ClientInfo? clientInfo = null);
    void SendMedia(string userId, MessageType type, string base64Data, string contentType, string? caption = null, ClientInfo? clientInfo = null);
    IReadOnlyList<ChatMessage> GetRecentMessages();
}
