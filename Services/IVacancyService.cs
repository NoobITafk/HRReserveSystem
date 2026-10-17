using HRReserveSystem.Models;

namespace HRReserveSystem.Services;

public interface IVacancyService
{
    Task<(List<Vacancy> Items, int TotalItems, int TotalPages)> GetVacanciesAsync(string? search, string? status, bool showArchived = false, int page = 1, int pageSize = 25, CancellationToken ct = default);
    Task<Vacancy?> GetVacancyDetailsAsync(int id, CancellationToken ct = default);
    Task<Vacancy> CreateVacancyAsync(Vacancy vacancy, CancellationToken ct = default);
    Task<(bool Success, string? Error)> UpdateVacancyAsync(Vacancy vacancy, CancellationToken ct = default);
    Task<bool> ToggleArchiveAsync(int id, CancellationToken ct = default);
}
