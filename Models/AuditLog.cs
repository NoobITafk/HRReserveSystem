namespace HRReserveSystem.Models;

public class AuditLog
{
    public int Id { get; set; }

    public string? UserId { get; set; }

    public string? UserName { get; set; }

    public string? UserRole { get; set; }

    public string Action { get; set; } = string.Empty;

    public string EntityName { get; set; } = string.Empty;

    public string? EntityId { get; set; }

    public string? Details { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
