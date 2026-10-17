using System;

namespace HRReserveSystem.Models;

public class AppNotification
{
    public int Id { get; set; }

    public string? RecruiterId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string TargetUrl { get; set; } = string.Empty;

    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
