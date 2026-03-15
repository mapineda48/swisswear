using System.ComponentModel.DataAnnotations;

namespace SwissWear.Domain.Entities;

public class ChatMessageLog
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string UserName { get; set; } = "";

    [Required, MaxLength(10)]
    public string MessageType { get; set; } = ""; // Text, Image, Audio

    public string? MessageText { get; set; }

    public string? MediaUrls { get; set; } // JSON array of URLs when media is present

    [Required, MaxLength(50)]
    public string SenderIp { get; set; } = "";

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
