using HRReserveSystem.ViewModels;

namespace HRReserveSystem.Services;

public interface IDashboardService
{
    Task<DashboardViewModel> GetDashboardDataAsync(CancellationToken ct = default);
}
