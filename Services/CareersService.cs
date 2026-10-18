using HRReserveSystem.Data;
using HRReserveSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Services;

public class CareersService(ApplicationDbContext context) : ICareersService
{
    public async Task<List<Vacancy>> GetOpenVacanciesAsync(string? search, CancellationToken ct = default)
    {
        IQueryable<Vacancy> query = context.Vacancies
            .AsNoTracking()
            .Where(v => !v.IsArchived && v.Status == "Open");

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLower();
            query = query.Where(v => v.Title.ToLower().Contains(searchLower)
                || v.Description.ToLower().Contains(searchLower)
                || v.Requirements.ToLower().Contains(searchLower));
        }

        return await query
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<Vacancy?> GetVacancyDetailsAsync(int id, CancellationToken ct = default)
    {
        return await context.Vacancies
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsArchived, ct);
    }

    public async Task<Application?> GetApplicationSuccessDetailsAsync(int id, CancellationToken ct = default)
    {
        return await context.Applications
            .AsNoTracking()
            .Include(a => a.Candidate)
            .Include(a => a.Vacancy)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }
}
