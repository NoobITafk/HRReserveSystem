using System.Security.Claims;
using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRReserveSystem.Controllers.Api;

[ApiController]
[Authorize(Roles = "Admin,Interviewer")]
[Route("api/soft-skills")]
public class SoftSkillsApiController(
    ISoftSkillAssessmentService assessmentService,
    ICandidateService candidateService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SoftSkillDto>>> GetAll()
    {
        var assessments = await assessmentService.GetAssessmentsAsync();
        return Ok(assessments.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SoftSkillDto>> GetById(int id)
    {
        var assessment = await assessmentService.GetAssessmentDetailsAsync(id);
        return assessment is null ? NotFound() : Ok(ToDto(assessment));
    }

    [HttpPost]
    public async Task<ActionResult<SoftSkillDto>> Create(SoftSkillCreateDto dto)
    {
        var candidate = await candidateService.GetCandidateDetailsAsync(dto.CandidateId);
        if (candidate is null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid candidate",
                Detail = "Кандидата для оцінки soft skills не знайдено.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var currentRecruiterId = await assessmentService.GetRecruiterIdForUserAsync(
            User.FindFirstValue(IdentityRecruiterSyncService.RecruiterIdClaim),
            User.FindFirstValue(ClaimTypes.NameIdentifier));

        var assessment = new SoftSkillAssessment
        {
            CandidateId = dto.CandidateId,
            RecruiterId = currentRecruiterId,
            Communication = dto.Communication,
            Teamwork = dto.Teamwork,
            Responsibility = dto.Responsibility,
            StressResistance = dto.StressResistance,
            Leadership = dto.Leadership,
            OverallComment = dto.OverallComment,
            CreatedAt = DateTime.UtcNow
        };

        var (success, error) = await assessmentService.CreateAssessmentAsync(
            assessment,
            currentRecruiterId,
            User.IsInRole("Interviewer"));

        if (!success)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Failed to create assessment",
                Detail = error ?? "Не вдалося зберегти оцінку.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var loaded = await assessmentService.GetAssessmentDetailsAsync(assessment.Id);
        return CreatedAtAction(nameof(GetById), new { id = assessment.Id }, ToDto(loaded ?? assessment));
    }

    private static SoftSkillDto ToDto(SoftSkillAssessment assessment)
    {
        return new SoftSkillDto(
            assessment.Id,
            assessment.CandidateId,
            assessment.Candidate?.FullName,
            assessment.Communication,
            assessment.Teamwork,
            assessment.Responsibility,
            assessment.StressResistance,
            assessment.Leadership,
            assessment.AverageScore,
            assessment.OverallComment);
    }
}
