using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using SwissWear.Web.Data;

namespace SwissWear.Web.Services;

public class ChatAuditService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BlobContainerClient _privateContainer;
    private readonly ILogger<ChatAuditService> _logger;

    public ChatAuditService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<ChatAuditService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var connectionString = configuration["AzureStorage:ConnectionString"]
            ?? throw new InvalidOperationException("AzureStorage:ConnectionString is not configured.");
        _privateContainer = new BlobContainerClient(connectionString, "chat-audit");
    }

    public async Task LogMessageAsync(ChatMessage message, string senderIp, string? userAgent)
    {
        try
        {
            List<string>? mediaUrls = null;

            if (message.Type == MessageType.Image && message.Images is { Count: > 0 })
            {
                mediaUrls = new List<string>();
                foreach (var img in message.Images)
                {
                    var url = await UploadMediaAsync(img.Base64, img.ContentType, message.UserName);
                    mediaUrls.Add(url);
                }
            }
            else if (message.Type == MessageType.Audio && message.MediaData is not null)
            {
                var url = await UploadMediaAsync(message.MediaData, message.MediaContentType ?? "audio/webm", message.UserName);
                mediaUrls = [url];
            }
            else if (message.Type == MessageType.Image && message.MediaData is not null)
            {
                var url = await UploadMediaAsync(message.MediaData, message.MediaContentType ?? "image/png", message.UserName);
                mediaUrls = [url];
            }

            var log = new ChatMessageLog
            {
                UserName = message.UserName,
                MessageType = message.Type.ToString(),
                MessageText = string.IsNullOrWhiteSpace(message.Text) ? null : message.Text,
                MediaUrls = mediaUrls is { Count: > 0 } ? JsonSerializer.Serialize(mediaUrls) : null,
                SenderIp = senderIp,
                UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent,
                SentAt = message.Timestamp
            };

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ChatMessageLogs.Add(log);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist chat audit log for {UserName}", message.UserName);
        }
    }

    private async Task<string> UploadMediaAsync(string base64Data, string contentType, string userName)
    {
        await _privateContainer.CreateIfNotExistsAsync(PublicAccessType.None);

        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "audio/webm" => ".webm",
            "audio/ogg" => ".ogg",
            _ => ".bin"
        };

        var fileName = $"{userName}/{DateTime.UtcNow:yyyy-MM-dd}/{Guid.NewGuid():N}{extension}";
        var bytes = Convert.FromBase64String(base64Data);
        using var stream = new MemoryStream(bytes);

        var blobClient = _privateContainer.GetBlobClient(fileName);
        await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = contentType });

        return blobClient.Uri.ToString();
    }
}
