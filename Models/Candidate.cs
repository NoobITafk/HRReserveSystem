using System.ComponentModel.DataAnnotations;

namespace HRReserveSystem.Models;

public class Candidate
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Вкажіть ПІБ кандидата.")]
    [StringLength(120)]
    [Display(Name = "ПІБ")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть електронну пошту.")]
    [EmailAddress(ErrorMessage = "Введіть коректну електронну пошту.")]
    [StringLength(160)]
    [Display(Name = "Електронна пошта")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Введіть коректний номер телефону.")]
    [StringLength(40)]
    [Display(Name = "Телефон")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "Опишіть ключові навички кандидата.")]
    [StringLength(1000)]
    [Display(Name = "Навички")]
    public string Skills { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Файл резюме")]
    public string? ResumeFilePath { get; set; }

    [StringLength(500)]
    [Display(Name = "Фото / Аватар")]
    public string? AvatarUrl { get; set; }

    [StringLength(4000)]
    [Display(Name = "Коротке резюме")]
    public string? ResumeSummary { get; set; }

    [Range(0, 60, ErrorMessage = "Досвід має бути від 0 до 60 років.")]
    [Display(Name = "Досвід, років")]
    public int ExperienceYears { get; set; }

    [Display(Name = "Дата створення")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "Видалено")]
    public bool IsDeleted { get; set; }

    public ICollection<Application> Applications { get; set; } = new List<Application>();

    public ICollection<SoftSkillAssessment> SoftSkillAssessments { get; set; } = new List<SoftSkillAssessment>();

    public ICollection<CandidateCommunication> Communications { get; set; } = new List<CandidateCommunication>();
}
