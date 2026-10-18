using System.Diagnostics;
using HRReserveSystem.Models;
using HRReserveSystem.Services;
using HRReserveSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRReserveSystem.Controllers;

[Authorize]
public class HomeController(
    ILogger<HomeController> logger,
    IDashboardService dashboardService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var dashboard = await dashboardService.GetDashboardDataAsync();
        return View(dashboard);
    }

    [AllowAnonymous]
    public IActionResult About()
    {
        return View();
    }

    [AllowAnonymous]
    public IActionResult CourseMaterials()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(int? statusCode = null)
    {
        if (statusCode.HasValue)
        {
            logger.LogWarning("Request finished with status code {StatusCode}. TraceId: {TraceId}", statusCode, HttpContext.TraceIdentifier);
        }

        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            StatusCode = statusCode
        });
    }
}
