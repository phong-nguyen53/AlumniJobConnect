using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class HomeController : Controller
{
    private readonly AlumniJobConnectContext _context;

    public HomeController(AlumniJobConnectContext context)
    {
        _context = context;
    }

    [HttpGet("/Admin")]
    [HttpGet("/Admin/Home/Index")]
    public async Task<IActionResult> Index()
    {
        var totalJobPosts = await _context.JobPosts.CountAsync();
        ViewBag.TotalJobPosts = totalJobPosts;
        return View();
    }
}