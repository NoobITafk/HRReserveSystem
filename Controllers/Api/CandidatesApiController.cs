using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRReserveSystem.Controllers.Api;

[ApiController]
[Authorize(Roles = "Admin,Recruiter")]
[Route("api/candidates")]
public class CandidatesApiController(ICandidateService candidateService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CandidateDto>>> GetAll()
    {
        var candidates = await candidateService.GetCandidatesForExportAsync(null, null, false);
        return Ok(candidates.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CandidateDto>> GetById(int id)
    {
        var candidate = await candidateService.GetCandidateDetailsAsync(id);
        return candidate is null ? NotFound() : Ok(ToDto(candidate));
    }

    [HttpPost]
    public async Task<ActionResult<CandidateDto>> Create(CandidateCreateDto dto)
    {
        if (await candidateService.EmailExistsAsync(dto.Email))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Duplicate candidate email",
                Detail = "Кандидат із такою електронною поштою вже існує.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var candidate = new Candidate
        {
            FullName = dto.FullName,
            Email = dto.Email,
            Phone = dto.Phone,
            Skills = dto.Skills,
            ExperienceYears = dto.ExperienceYears,
            ResumeSummary = dto.ResumeSummary,
            CreatedAt = DateTime.UtcNow
        };

        var created = await candidateService.CreateCandidateAsync(candidate, null, null);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    private static CandidateDto ToDto(Candidate candidate)
    {
        return new CandidateDto(
            candidate.Id,
            candidate.FullName,
            candidate.Email,
            candidate.Phone,
            candidate.Skills,
            candidate.ExperienceYears,
            candidate.ResumeFilePath,
            candidate.ResumeSummary,
            candidate.CreatedAt);
    }
}
