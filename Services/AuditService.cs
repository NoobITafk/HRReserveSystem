using HRReserveSystem.Data;
using HRReserveSystem.Models;
using System.Security.Claims;

namespace HRReserveSystem.Services;

public class AuditService(
    ApplicationDbContext context,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuditService> logger) : IAuditService
{
    public async Task LogAsync(string action, string entityName, string? entityId = null, string? details = null)
    {
        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            var user = httpContext?.User;

            var userId = user?.FindFirstValue(ClaimTypes.NameIdentifier);
            var userName = user?.Identity?.Name ?? (user?.Identity?.IsAuthenticated == true ? "Користувач" : "Анонім");
            var userRole = user?.FindFirstValue(ClaimTypes.Role) ?? (user?.Identity?.IsAuthenticated == true ? "Авторизований" : "Анонім");
            var ipAddress = httpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var auditLog = new AuditLog
            {
                UserId = userId,
                UserName = userName,
                UserRole = userRole,
                Action = action,
                EntityName = entityName,
                EntityId = entityId,
                Details = details,
                IpAddress = ipAddress,
                CreatedAt = DateTime.UtcNow
            };

            context.AuditLogs.Add(auditLog);
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record audit log for action {Action} on {EntityName}", action, entityName);
        }
    }
}
