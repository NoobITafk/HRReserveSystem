using System.ComponentModel.DataAnnotations;

namespace HRReserveSystem.ViewModels;

public class ProfileViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Вкажіть ПІБ.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "ПІБ має містити від 2 до 120 символів.")]
    [Display(Name = "ПІБ")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть електронну пошту.")]
    [EmailAddress(ErrorMessage = "Введіть коректну електронну пошту.")]
    [StringLength(160, ErrorMessage = "Email має містити не більше 160 символів.")]
    [Display(Name = "Електронна пошта")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Логін")]
    public string Login { get; set; } = string.Empty;

    [Display(Name = "Роль")]
    public string Role { get; set; } = string.Empty;

    [Display(Name = "Дата реєстрації")]
    public DateTime CreatedAt { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "Поточний пароль")]
    public string? CurrentPassword { get; set; }

    [StringLength(100, MinimumLength = 6, ErrorMessage = "Новий пароль має містити щонайменше 6 символів.")]
    [DataType(DataType.Password)]
    [Display(Name = "Новий пароль")]
    public string? NewPassword { get; set; }

    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Новий пароль та підтвердження не співпадають.")]
    [Display(Name = "Підтвердження нового пароля")]
    public string? ConfirmNewPassword { get; set; }

    public int InterviewsCount { get; set; }

    public int FeedbacksCount { get; set; }
}
