using System.ComponentModel.DataAnnotations;

namespace HRReserveSystem.Models;

public class Recruiter : IValidatableObject
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Вкажіть ПІБ рекрутера.")]
    [StringLength(120, ErrorMessage = "ПІБ має містити не більше 120 символів.")]
    [Display(Name = "ПІБ")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть електронну пошту.")]
    [EmailAddress(ErrorMessage = "Введіть коректну електронну пошту.")]
    [StringLength(160, ErrorMessage = "Email має містити не більше 160 символів.")]
    [Display(Name = "Електронна пошта")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть логін.")]
    [StringLength(60, ErrorMessage = "Логін має містити не більше 60 символів.")]
    [Display(Name = "Логін")]
    public string Login { get; set; } = string.Empty;

    [Required(ErrorMessage = "Хеш пароля обов'язковий.")]
    [StringLength(500)]
    [Display(Name = "Хеш пароля")]
    public string PasswordHash { get; set; } = string.Empty;

    [Required(ErrorMessage = "Оберіть роль.")]
    [StringLength(40)]
    [Display(Name = "Роль")]
    public string Role { get; set; } = "Recruiter";

    [StringLength(500)]
    [Display(Name = "Фото / Аватар")]
    public string? AvatarUrl { get; set; }

    [Display(Name = "Дата створення")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Interview> Interviews { get; set; } = new List<Interview>();

    public ICollection<InterviewFeedback> Feedbacks { get; set; } = new List<InterviewFeedback>();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!HrOptions.RecruiterRoles.Contains(Role))
        {
            yield return new ValidationResult("Оберіть коректну роль користувача.", [nameof(Role)]);
        }
    }
}
