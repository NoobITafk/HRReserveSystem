namespace HRReserveSystem.Models;

public class EmailOutboxMessage
{
    public int Id { get; set; }

    public string RecipientEmail { get; set; } = string.Empty;

    public string? RecipientName { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string? TemplateType { get; set; }

    public string Status { get; set; } = "Pending";

    public int RetryCount { get; set; } = 0;

    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }
}
