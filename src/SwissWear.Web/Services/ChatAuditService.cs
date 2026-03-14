using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using SwissWear.Web.Data;

namespace SwissWear.Web.Services;

public class ChatAuditService : IChatAuditService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BlobContainerClient _privateContainer;
    private readonly ILogger<ChatAuditService> _logger;
    private bool _containerInitialized;

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
            var mediaUrls = await UploadMediaFromMessageAsync(message);

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

    private async Task<List<string>?> UploadMediaFromMessageAsync(ChatMessage message)
    {
        if (message.Type == MessageType.Image && message.Images is { Count: > 0 })
        {
            var urls = new List<string>(message.Images.Count);
            foreach (var img in message.Images)
            {
                var url = await UploadMediaAsync(img.Base64, img.ContentType, message.UserName);
                urls.Add(url);
            }
            return urls;
        }

        if (message is { MediaData: not null })
        {
            var contentType = message.MediaContentType ?? (message.Type == MessageType.Audio ? "audio/webm" : "image/png");
            var url = await UploadMediaAsync(message.MediaData, contentType, message.UserName);
            return [url];
        }

        return null;
    }

    private async Task EnsureContainerExistsAsync()
    {
        if (_containerInitialized) return;
        await _privateContainer.CreateIfNotExistsAsync(PublicAccessType.None);
        _containerInitialized = true;
    }

    private async Task<string> UploadMediaAsync(string base64Data, string contentType, string userName)
    {
        await EnsureContainerExistsAsync();

        var extension = GetExtension(contentType);
        var fileName = $"{userName}/{DateTime.UtcNow:yyyy-MM-dd}/{Guid.NewGuid():N}{extension}";
        var bytes = Convert.FromBase64String(base64Data);
        using var stream = new MemoryStream(bytes);

        var blobClient = _privateContainer.GetBlobClient(fileName);
        await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = contentType });

        return blobClient.Uri.ToString();
    }

    private static string GetExtension(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "audio/webm" => ".webm",
        "audio/ogg" => ".ogg",
        _ => ".bin"
    };
}
