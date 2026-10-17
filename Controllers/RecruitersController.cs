using HRReserveSystem.Services;
using HRReserveSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRReserveSystem.Controllers;

[Authorize(Roles = "Admin")]
public class RecruitersController(IRecruiterService recruiterService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var recruiters = await recruiterService.GetRecruitersAsync();
        return View(recruiters);
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var recruiter = await recruiterService.GetRecruiterDetailsAsync(id.Value);
        return recruiter is null ? NotFound() : View(recruiter);
    }

    public IActionResult Create()
    {
        return View(new RecruiterFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RecruiterFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(nameof(RecruiterFormViewModel.Password), "Вкажіть пароль.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var (success, errors) = await recruiterService.CreateRecruiterAsync(model);
        if (!success)
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return View(model);
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var model = await recruiterService.GetRecruiterForEditAsync(id.Value);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, RecruiterFormViewModel model)
    {
        if (id != model.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var (success, errors) = await recruiterService.UpdateRecruiterAsync(model);
        if (!success)
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return View(model);
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var recruiter = await recruiterService.GetRecruiterDetailsAsync(id.Value);
        return recruiter is null ? NotFound() : View(recruiter);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        await recruiterService.DeleteRecruiterAsync(id);
        return RedirectToAction(nameof(Index));
    }
}
