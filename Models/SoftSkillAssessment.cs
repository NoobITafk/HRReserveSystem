using System.ComponentModel.DataAnnotations;

namespace HRReserveSystem.Models;

public class SoftSkillAssessment
{
    public int Id { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Оберіть кандидата.")]
    [Display(Name = "Кандидат")]
    public int CandidateId { get; set; }

    [Range(1, 10, ErrorMessage = "Оцінка має бути від 1 до 10.")]
    [Display(Name = "Комунікація")]
    public int Communication { get; set; } = 5;

    [Range(1, 10, ErrorMessage = "Оцінка має бути від 1 до 10.")]
    [Display(Name = "Командна робота")]
    public int Teamwork { get; set; } = 5;

    [Range(1, 10, ErrorMessage = "Оцінка має бути від 1 до 10.")]
    [Display(Name = "Відповідальність")]
    public int Responsibility { get; set; } = 5;

    [Range(1, 10, ErrorMessage = "Оцінка має бути від 1 до 10.")]
    [Display(Name = "Стресостійкість")]
    public int StressResistance { get; set; } = 5;

    [Range(1, 10, ErrorMessage = "Оцінка має бути від 1 до 10.")]
    [Display(Name = "Лідерство")]
    public int Leadership { get; set; } = 5;

    [StringLength(2000)]
    [Display(Name = "Загальний коментар")]
    public string? OverallComment { get; set; }

    [Display(Name = "Середній бал")]
    public double AverageScore => Math.Round((Communication + Teamwork + Responsibility + StressResistance + Leadership) / 5.0, 1);

    public Candidate? Candidate { get; set; }

    [Display(Name = "Оцінювач")]
    public int? RecruiterId { get; set; }
    public Recruiter? Recruiter { get; set; }

    [Display(Name = "Дата оцінки")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
