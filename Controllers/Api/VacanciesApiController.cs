using HRReserveSystem.Models;
using HRReserveSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRReserveSystem.Controllers.Api;

[ApiController]
[Authorize(Roles = "Admin,Recruiter")]
[Route("api/vacancies")]
public class VacanciesApiController(IVacancyService vacancyService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<VacancyDto>>> GetAll()
    {
        var (items, _, _) = await vacancyService.GetVacanciesAsync(null, null, false, 1, 1000);
        return Ok(items.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VacancyDto>> GetById(int id)
    {
        var vacancy = await vacancyService.GetVacancyDetailsAsync(id);
        return vacancy is null ? NotFound() : Ok(ToDto(vacancy));
    }

    [HttpPost]
    public async Task<ActionResult<VacancyDto>> Create(VacancyCreateDto dto)
    {
        var vacancy = new Vacancy
        {
            Title = dto.Title,
            Description = dto.Description,
            Requirements = dto.Requirements,
            SalaryMin = dto.SalaryMin,
            SalaryMax = dto.SalaryMax,
            Status = dto.Status,
            CreatedAt = DateTime.UtcNow
        };

        var created = await vacancyService.CreateVacancyAsync(vacancy);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    private static VacancyDto ToDto(Vacancy vacancy)
    {
        return new VacancyDto(
            vacancy.Id,
            vacancy.Title,
            vacancy.Description,
            vacancy.Requirements,
            vacancy.SalaryMin,
            vacancy.SalaryMax,
            vacancy.Status,
            vacancy.CreatedAt);
    }
}
