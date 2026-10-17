using HRReserveSystem.Data;
using HRReserveSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Services;

public class VacancyService(
    ApplicationDbContext context,
    IAuditService auditService,
    ILogger<VacancyService> logger) : IVacancyService
{
    public async Task<(List<Vacancy> Items, int TotalItems, int TotalPages)> GetVacanciesAsync(string? search, string? status, bool showArchived = false, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;

        var query = context.Vacancies.AsNoTracking().Where(vacancy => vacancy.IsArchived == showArchived);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(vacancy => vacancy.Title.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(vacancy => vacancy.Status == status);
        }

        var totalItems = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        if (totalPages > 0 && page > totalPages) page = totalPages;

        var items = await query
            .OrderByDescending(vacancy => vacancy.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalItems, totalPages);
    }

    public async Task<Vacancy?> GetVacancyDetailsAsync(int id, CancellationToken ct = default)
    {
        return await context.Vacancies
            .AsNoTracking()
            .Include(item => item.Applications)
                .ThenInclude(application => application.Candidate)
            .FirstOrDefaultAsync(item => item.Id == id, ct);
    }

    public async Task<Vacancy> CreateVacancyAsync(Vacancy vacancy, CancellationToken ct = default)
    {
        vacancy.CreatedAt = DateTime.UtcNow;
        context.Vacancies.Add(vacancy);
        await context.SaveChangesAsync(ct);

        await auditService.LogAsync("CreateVacancy", "Vacancy", vacancy.Id.ToString(), $"Створено нову вакансію: {vacancy.Title}");
        logger.LogInformation("Created vacancy #{Id} {Title}", vacancy.Id, vacancy.Title);

        return vacancy;
    }

    public async Task<(bool Success, string? Error)> UpdateVacancyAsync(Vacancy vacancy, CancellationToken ct = default)
    {
        var existing = await context.Vacancies.FirstOrDefaultAsync(v => v.Id == vacancy.Id, ct);
        if (existing is null)
        {
            return (false, "Вакансію не знайдено.");
        }

        existing.Title = vacancy.Title;
        existing.Description = vacancy.Description;
        existing.Requirements = vacancy.Requirements;
        existing.Status = vacancy.Status;
        existing.SalaryMin = vacancy.SalaryMin;
        existing.SalaryMax = vacancy.SalaryMax;

        await context.SaveChangesAsync(ct);
        await auditService.LogAsync("UpdateVacancy", "Vacancy", vacancy.Id.ToString(), $"Оновлено дані вакансії: {vacancy.Title}");

        return (true, null);
    }

    public async Task<bool> ToggleArchiveAsync(int id, CancellationToken ct = default)
    {
        var vacancy = await context.Vacancies.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vacancy is null)
        {
            return false;
        }

        vacancy.IsArchived = !vacancy.IsArchived;
        await context.SaveChangesAsync(ct);

        var action = vacancy.IsArchived ? "ArchiveVacancy" : "UnarchiveVacancy";
        var description = vacancy.IsArchived ? $"Вакансію «{vacancy.Title}» архівовано" : $"Вакансію «{vacancy.Title}» відновлено з архіву";
        await auditService.LogAsync(action, "Vacancy", vacancy.Id.ToString(), description);

        return true;
    }
}
