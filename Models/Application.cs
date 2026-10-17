using System.ComponentModel.DataAnnotations;

namespace HRReserveSystem.Models;

public class Application : IValidatableObject
{
    public int Id { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Оберіть кандидата.")]
    [Display(Name = "Кандидат")]
    public int CandidateId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Оберіть вакансію.")]
    [Display(Name = "Вакансія")]
    public int VacancyId { get; set; }

    [Required(ErrorMessage = "Оберіть етап відбору.")]
    [StringLength(40)]
    [Display(Name = "Статус етапу відбору")]
    public string Status { get; set; } = "New";

    [Display(Name = "Дата подачі")]
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    [StringLength(2000)]
    [Display(Name = "Коментар рекрутера")]
    public string? RecruiterComment { get; set; }

    public Candidate? Candidate { get; set; }

    public Vacancy? Vacancy { get; set; }

    public int? StageId { get; set; }

    public PipelineStage? Stage { get; set; }

    public ICollection<Interview> Interviews { get; set; } = new List<Interview>();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!HrOptions.ApplicationStatuses.Contains(Status))
        {
            yield return new ValidationResult("Оберіть коректний етап відбору.", [nameof(Status)]);
        }
    }
}
