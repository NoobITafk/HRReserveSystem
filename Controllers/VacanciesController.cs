using HRReserveSystem.Data;
using HRReserveSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Controllers;

[Authorize(Roles = "Admin,Recruiter,Candidate")]
public class VacanciesController(ApplicationDbContext context) : Controller
{
    public async Task<IActionResult> Index(string? search, string? status, bool showArchived = false)
    {
        var vacancies = context.Vacancies.AsNoTracking().Where(vacancy => vacancy.IsArchived == showArchived);

        if (!string.IsNullOrWhiteSpace(search))
        {
            vacancies = vacancies.Where(vacancy => vacancy.Title.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            vacancies = vacancies.Where(vacancy => vacancy.Status == status);
        }

        ViewData["CurrentFilter"] = search;
        ViewData["StatusFilter"] = status;
        ViewData["ShowArchived"] = showArchived;
        ViewData["Statuses"] = new SelectList(HrOptions.VacancyStatuses, status);

        return View(await vacancies
            .OrderByDescending(vacancy => vacancy.CreatedAt)
            .ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var vacancy = await context.Vacancies
            .AsNoTracking()
            .Include(item => item.Applications)
                .ThenInclude(application => application.Candidate)
            .FirstOrDefaultAsync(item => item.Id == id);

        return vacancy is null ? NotFound() : View(vacancy);
    }

    [Authorize(Roles = "Admin,Recruiter")]
    public IActionResult Create()
    {
        return View(new Vacancy());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Title,Description,Requirements,SalaryMin,SalaryMax,Status")] Vacancy vacancy)
    {
        if (!ModelState.IsValid)
        {
            return View(vacancy);
        }

        vacancy.CreatedAt = DateTime.UtcNow;
        context.Add(vacancy);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var vacancy = await context.Vacancies.FindAsync(id);
        return vacancy is null ? NotFound() : View(vacancy);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Title,Description,Requirements,SalaryMin,SalaryMax,Status,CreatedAt")] Vacancy vacancy)
    {
        if (id != vacancy.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(vacancy);
        }

        try
        {
            context.Update(vacancy);
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await VacancyExists(vacancy.Id))
            {
                return NotFound();
            }

            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var vacancy = await context.Vacancies
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id);

        return vacancy is null ? NotFound() : View(vacancy);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var vacancy = await context.Vacancies.FindAsync(id);

        if (vacancy is not null)
        {
            vacancy.IsArchived = true;
            await context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Restore(int id)
    {
        var vacancy = await context.Vacancies.FirstOrDefaultAsync(item => item.Id == id && item.IsArchived);

        if (vacancy is not null)
        {
            vacancy.IsArchived = false;
            await context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index), new { showArchived = true });
    }

    private async Task<bool> VacancyExists(int id)
    {
        return await context.Vacancies.AnyAsync(item => item.Id == id);
    }
}
