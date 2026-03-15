namespace SwissWear.Domain.Contracts;

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
