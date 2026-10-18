using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRReserveSystem.Controllers.Api;

[ApiController]
[Authorize(Roles = "Admin,Recruiter")]
[Route("api/applications")]
public class ApplicationsApiController(
    ICandidateApplicationService applicationService,
    ICandidateService candidateService,
    IVacancyService vacancyService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApplicationDto>>> GetAll()
    {
        var (items, _, _) = await applicationService.GetApplicationsAsync(null, 1, 1000);
        return Ok(items.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApplicationDto>> GetById(int id)
    {
        var application = await applicationService.GetApplicationDetailsAsync(id);
        return application is null ? NotFound() : Ok(ToDto(application));
    }

    [HttpPost]
    public async Task<ActionResult<ApplicationDto>> Create(ApplicationCreateDto dto)
    {
        var candidate = await candidateService.GetCandidateDetailsAsync(dto.CandidateId);
        var vacancy = await vacancyService.GetVacancyDetailsAsync(dto.VacancyId);
        if (candidate is null || vacancy is null || vacancy.IsArchived)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid application reference",
                Detail = "Кандидат або вакансія не знайдені чи архівні.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (await applicationService.ApplicationPairExistsAsync(dto.CandidateId, dto.VacancyId))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Duplicate application",
                Detail = "Заявка для цього кандидата на цю вакансію вже існує.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var application = new Application
        {
            CandidateId = dto.CandidateId,
            VacancyId = dto.VacancyId,
            Status = dto.Status,
            RecruiterComment = dto.RecruiterComment,
            AppliedAt = DateTime.UtcNow
        };

        var created = await applicationService.CreateApplicationAsync(application);
        var loaded = await applicationService.GetApplicationDetailsAsync(created.Id);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(loaded ?? created));
    }

    private static ApplicationDto ToDto(Application application)
    {
        return new ApplicationDto(
            application.Id,
            application.CandidateId,
            application.Candidate?.FullName,
            application.VacancyId,
            application.Vacancy?.Title,
            application.Status,
            application.AppliedAt,
            application.RecruiterComment);
    }
}
