using HRReserveSystem.Models;

namespace HRReserveSystem.Services;

public class GoogleCalendarOptions
{
    public bool Enabled { get; set; } = false;
    public string CalendarId { get; set; } = "primary";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RefreshToken { get; set; } = "";
}

public interface IGoogleCalendarSyncService
{
    bool IsConfigured { get; }
    Task<(bool Success, string? EventId, string? Error)> CreateEventAsync(Interview interview, CancellationToken ct = default);
    Task<bool> UpdateEventAsync(string eventId, Interview interview, CancellationToken ct = default);
    Task<bool> DeleteEventAsync(string eventId, CancellationToken ct = default);
}
