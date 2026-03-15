namespace SwissWear.Domain.Contracts;

public interface IChatAuditService
{
    Task LogMessageAsync(ChatMessage message, string senderIp, string? userAgent);
}
