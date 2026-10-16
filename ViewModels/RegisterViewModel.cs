using System.ComponentModel.DataAnnotations;

namespace HRReserveSystem.ViewModels;

public class RegisterViewModel : IValidatableObject
{
    public static readonly string[] AllowedRegistrationRoles = ["Candidate", "Recruiter", "Interviewer"];

    [Required(ErrorMessage = "Вкажіть ваше повне ім'я (ПІБ).")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "ПІБ має містити від 2 до 120 символів.")]
    [Display(Name = "ПІБ (Повне ім'я)")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть корпоративну або особисту електронну пошту.")]
    [EmailAddress(ErrorMessage = "Введіть коректну адресу електронної пошти.")]
    [StringLength(160, ErrorMessage = "Email має містити не більше 160 символів.")]
    [Display(Name = "Електронна пошта")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть логін для входу.")]
    [StringLength(60, MinimumLength = 3, ErrorMessage = "Логін має містити від 3 до 60 символів.")]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Логін може містити лише латинські літери, цифри, крапку, дефіс або підкреслення.")]
    [Display(Name = "Логін")]
    public string Login { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть пароль.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Пароль має містити щонайменше 6 символів.")]
    [DataType(DataType.Password)]
    [Display(Name = "Пароль")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Підтвердіть пароль.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Паролі не співпадають.")]
    [Display(Name = "Підтвердження пароля")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Оберіть роль у системі.")]
    [Display(Name = "Роль у команді")]
    public string Role { get; set; } = "Recruiter";

    public string? ReturnUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!AllowedRegistrationRoles.Contains(Role))
        {
            yield return new ValidationResult("Оберіть доступну роль для самостійної реєстрації (Рекрутер або Інтерв'юер).", [nameof(Role)]);
        }
    }
}
