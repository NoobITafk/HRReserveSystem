using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRReserveSystem.Controllers.Api;

[ApiController]
[Authorize(Roles = "Admin,Recruiter,Interviewer")]
[Route("api/interviews")]
public class InterviewsApiController(
    IInterviewService interviewService,
    ICandidateApplicationService applicationService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<InterviewDto>>> GetAll()
    {
        var (items, _, _) = await interviewService.GetInterviewsAsync(null, null, 1, 1000);
        return Ok(items.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<InterviewDto>> GetById(int id)
    {
        var interview = await interviewService.GetInterviewDetailsAsync(id);
        return interview is null ? NotFound() : Ok(ToDto(interview));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<InterviewDto>> Create(InterviewCreateDto dto)
    {
        var application = await applicationService.GetApplicationDetailsAsync(dto.ApplicationId);
        if (application is null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid application",
                Detail = "Заявку для співбесіди не знайдено.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var interview = new Interview
        {
            ApplicationId = dto.ApplicationId,
            RecruiterId = dto.RecruiterId,
            InterviewDate = dto.InterviewDate,
            InterviewType = dto.InterviewType,
            Result = dto.Result,
            Notes = dto.Notes
        };

        var created = await interviewService.ScheduleInterviewAsync(interview);
        var loaded = await interviewService.GetInterviewDetailsAsync(created.Id);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(loaded ?? created));
    }

    private static InterviewDto ToDto(Interview interview)
    {
        return new InterviewDto(
            interview.Id,
            interview.ApplicationId,
            interview.RecruiterId,
            interview.Recruiter?.FullName,
            interview.InterviewDate,
            interview.InterviewType,
            interview.Result,
            interview.Notes);
    }
}
