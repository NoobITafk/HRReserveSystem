using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HRReserveSystem.Controllers;

[AllowAnonymous]
public class CareersController(
    ICareersService careersService,
    ICandidateApplicationService applicationService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search)
    {
        var vacancies = await careersService.GetOpenVacanciesAsync(search);
        ViewBag.Search = search;
        return View(vacancies);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var vacancy = await careersService.GetVacancyDetailsAsync(id);
        if (vacancy is null)
        {
            return NotFound();
        }

        return View(vacancy);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("CareersApplyPolicy")]
    public async Task<IActionResult> Apply(
        int vacancyId,
        string fullName,
        string email,
        string? phone,
        int? experienceYears,
        string? skills,
        string? coverLetter,
        IFormFile? resumeFile)
    {
        var vacancy = await careersService.GetVacancyDetailsAsync(vacancyId);
        if (vacancy is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email))
        {
            ModelState.AddModelError(string.Empty, "ПІБ та електронна пошта обов'язкові для заповнення.");
            return View("Details", vacancy);
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (success, error, candidate, application) = await applicationService.ApplyFromCareersAsync(
            fullName,
            email,
            phone,
            null,
            null,
            null,
            coverLetter,
            clientIp,
            resumeFile!,
            vacancyId);

        if (!success)
        {
            ModelState.AddModelError(string.Empty, error ?? "Не вдалося зберегти заявку.");
            return View("Details", vacancy);
        }

        return RedirectToAction(nameof(Success), new { id = application.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Success(int id)
    {
        var application = await careersService.GetApplicationSuccessDetailsAsync(id);
        if (application is null)
        {
            return RedirectToAction(nameof(Index));
        }

        return View(application);
    }
}
