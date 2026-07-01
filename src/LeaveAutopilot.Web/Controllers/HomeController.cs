using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LeaveAutopilot.Web.Models;

namespace LeaveAutopilot.Web.Controllers;

/// <summary>
/// The authenticated landing page. No explicit role restriction is needed — the app-wide
/// fallback authorization policy (Program.cs) already requires sign-in for any endpoint
/// without its own [Authorize]/[AllowAnonymous]; the attribute below just makes that
/// requirement explicit here too.
/// </summary>
[Authorize]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
