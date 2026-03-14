namespace SwissWear.Web.Services;

public interface IChatAuditService
{
    Task LogMessageAsync(ChatMessage message, string senderIp, string? userAgent);
}
