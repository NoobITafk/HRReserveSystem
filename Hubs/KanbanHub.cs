using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HRReserveSystem.Hubs;

[Authorize]
public class KanbanHub : Hub
{
    public async Task JoinVacancyBoard(string vacancyId)
    {
        var groupName = string.IsNullOrWhiteSpace(vacancyId) || vacancyId == "0"
            ? "vacancy-all"
            : $"vacancy-{vacancyId}";

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    public async Task LeaveVacancyBoard(string vacancyId)
    {
        var groupName = string.IsNullOrWhiteSpace(vacancyId) || vacancyId == "0"
            ? "vacancy-all"
            : $"vacancy-{vacancyId}";

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }
}
