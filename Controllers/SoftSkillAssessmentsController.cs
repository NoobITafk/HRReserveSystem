using HRReserveSystem.Data;
using HRReserveSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HRReserveSystem.Controllers;

[Authorize(Roles = "Admin,Recruiter,Interviewer")]
public class SoftSkillAssessmentsController(ApplicationDbContext context) : Controller
{
    public async Task<IActionResult> Index()
    {
        var assessments = context.SoftSkillAssessments
            .AsNoTracking()
            .Include(assessment => assessment.Candidate);

        return View(await assessments
            .OrderBy(assessment => assessment.Candidate!.FullName)
            .ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var assessment = await context.SoftSkillAssessments
            .AsNoTracking()
            .Include(item => item.Candidate)
            .FirstOrDefaultAsync(item => item.Id == id);

        return assessment is null ? NotFound() : View(assessment);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateCandidatesSelectList();
        return View(new SoftSkillAssessment());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("CandidateId,Communication,Teamwork,Responsibility,StressResistance,Leadership,OverallComment")] SoftSkillAssessment assessment)
    {
        if (!ModelState.IsValid)
        {
            await PopulateCandidatesSelectList(assessment.CandidateId);
            return View(assessment);
        }

        context.Add(assessment);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var assessment = await context.SoftSkillAssessments.FindAsync(id);
        if (assessment is null)
        {
            return NotFound();
        }

        await PopulateCandidatesSelectList(assessment.CandidateId);
        return View(assessment);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,CandidateId,Communication,Teamwork,Responsibility,StressResistance,Leadership,OverallComment")] SoftSkillAssessment assessment)
    {
        if (id != assessment.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            await PopulateCandidatesSelectList(assessment.CandidateId);
            return View(assessment);
        }

        try
        {
            context.Update(assessment);
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await AssessmentExists(assessment.Id))
            {
                return NotFound();
            }

            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null)
        {
            return NotFound();
        }

        var assessment = await context.SoftSkillAssessments
            .AsNoTracking()
            .Include(item => item.Candidate)
            .FirstOrDefaultAsync(item => item.Id == id);

        return assessment is null ? NotFound() : View(assessment);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var assessment = await context.SoftSkillAssessments.FindAsync(id);

        if (assessment is not null)
        {
            context.SoftSkillAssessments.Remove(assessment);
            await context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateCandidatesSelectList(int? selectedCandidateId = null)
    {
        var candidates = await context.Candidates
            .AsNoTracking()
            .Where(candidate => !candidate.IsDeleted)
            .OrderBy(candidate => candidate.FullName)
            .ToListAsync();

        ViewData["CandidateId"] = new SelectList(candidates, "Id", "FullName", selectedCandidateId);
    }

    private async Task<bool> AssessmentExists(int id)
    {
        return await context.SoftSkillAssessments.AnyAsync(item => item.Id == id);
    }
}
