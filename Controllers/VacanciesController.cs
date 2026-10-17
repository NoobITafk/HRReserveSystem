using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRReserveSystem.Controllers;

[Authorize(Roles = "Admin,Recruiter,Candidate")]
public class VacanciesController(IVacancyService vacancyService) : Controller
{
    public async Task<IActionResult> Index(string? search, string? status, bool showArchived = false, int page = 1)
    {
        var (vacancies, totalItems, totalPages) = await vacancyService.GetVacanciesAsync(search, status, showArchived, page, 25);

        ViewData["CurrentFilter"] = search;
        ViewData["StatusFilter"] = status;
        ViewData["ShowArchived"] = showArchived;
        ViewData["Statuses"] = new SelectList(HrOptions.VacancyStatuses, status);
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalItems = totalItems;

        return View(vacancies);
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var vacancy = await vacancyService.GetVacancyDetailsAsync(id.Value);
        return vacancy is null ? NotFound() : View(vacancy);
    }

    [Authorize(Roles = "Admin,Recruiter")]
    public IActionResult Create()
    {
        return View(new Vacancy());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Create([Bind("Title,Description,Requirements,SalaryMin,SalaryMax,Status")] Vacancy vacancy)
    {
        if (!ModelState.IsValid)
        {
            return View(vacancy);
        }

        await vacancyService.CreateVacancyAsync(vacancy);
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var vacancy = await vacancyService.GetVacancyDetailsAsync(id.Value);
        return vacancy is null ? NotFound() : View(vacancy);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Recruiter")]
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

        var (success, error) = await vacancyService.UpdateVacancyAsync(vacancy);
        if (!success)
        {
            return NotFound();
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

        var vacancy = await vacancyService.GetVacancyDetailsAsync(id.Value);
        return vacancy is null ? NotFound() : View(vacancy);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        await vacancyService.ToggleArchiveAsync(id);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Restore(int id)
    {
        await vacancyService.ToggleArchiveAsync(id);
        return RedirectToAction(nameof(Index), new { showArchived = true });
    }
}
