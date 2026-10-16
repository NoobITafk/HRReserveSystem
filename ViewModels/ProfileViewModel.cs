using System.ComponentModel.DataAnnotations;
using HRReserveSystem.Models;
using Microsoft.AspNetCore.Http;

namespace HRReserveSystem.ViewModels;

public class ProfileViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Вкажіть ваше повне ім'я (ПІБ).")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "ПІБ має містити від 2 до 120 символів.")]
    [Display(Name = "ПІБ (Повне ім'я)")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Вкажіть електронну пошту.")]
    [EmailAddress(ErrorMessage = "Введіть коректну електронну пошту.")]
    [StringLength(160, ErrorMessage = "Email має містити не більше 160 символів.")]
    [Display(Name = "Корпоративна електронна пошта")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Логін у системі")]
    public string Login { get; set; } = string.Empty;

    [Display(Name = "Роль у команді")]
    public string Role { get; set; } = string.Empty;

    [Display(Name = "Фото профілю (аватар)")]
    public string? AvatarUrl { get; set; }

    [Display(Name = "Завантажити нове фото")]
    public IFormFile? AvatarFile { get; set; }

    [Display(Name = "Дата реєстрації в системі")]
    public DateTime CreatedAt { get; set; }

    // Password change
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

    // Employee Performance & Activity Metrics
    public int InterviewsCount { get; set; }
    public int FeedbacksCount { get; set; }
    public int CandidatesCount { get; set; }
    public int VacanciesCount { get; set; }
    public double AverageFeedbackScore { get; set; }

    // Personal assigned schedule & feedbacks
    public List<Interview> MyUpcomingInterviews { get; set; } = [];
    public List<InterviewFeedback> MyRecentFeedbacks { get; set; } = [];
}
