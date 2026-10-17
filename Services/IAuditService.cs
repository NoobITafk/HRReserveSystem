namespace HRReserveSystem.Services;

public interface IAuditService
{
    Task LogAsync(string action, string entityName, string? entityId = null, string? details = null);
}
