using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AlumniJobConnect.Controllers;
public class JobsController : Controller
{
    private readonly AlumniJobConnectContext _context;
    public JobsController(AlumniJobConnectContext context) => _context = context;
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var job = await _context.JobPosts.AsNoTracking()
            .Include(x => x.JobCategory).Include(x => x.Company)
            .SingleOrDefaultAsync(x => x.JobPostId == id && x.ApprovalStatus == "Approved"
                && x.IsPublished && (x.Deadline == null || x.Deadline >= today)
                && x.Company.User.IsActive && x.Company.User.UserRoles.Any(r => r.Role.RoleName == "Employer"));
        if (job == null) return NotFound("Tin không tồn tại hoặc không còn hiển thị.");
        return View(job);
    }
}
