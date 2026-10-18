using HRReserveSystem.Models;

namespace HRReserveSystem.Services;

public interface ICareersService
{
    Task<List<Vacancy>> GetOpenVacanciesAsync(string? search, CancellationToken ct = default);
    Task<Vacancy?> GetVacancyDetailsAsync(int id, CancellationToken ct = default);
    Task<Application?> GetApplicationSuccessDetailsAsync(int id, CancellationToken ct = default);
}
