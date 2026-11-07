using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class JobPostsController : Controller
{
    private readonly AlumniJobConnectContext _context;

    public JobPostsController(AlumniJobConnectContext context)
    {
        _context = context;
    }

    [HttpGet("/Admin/JobPosts", Name = "AdminJobPosts")]
    public async Task<IActionResult> Index()
    {
        var jobPosts = await _context.JobPosts
            .AsNoTracking()
            .Include(job => job.Company)
            .OrderByDescending(job => job.ApprovalStatus == "Pending")
            .ThenByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.JobPostId)
            .ToListAsync();

        return View(jobPosts);
    }
    [HttpPost("/Admin/JobPosts/{id:int}/Approve", Name = "AdminApproveJobPost")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var now = DateTime.Now;

        var updated = await _context.JobPosts
            .Where(job => job.JobPostId == id && job.ApprovalStatus == "Pending")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.ApprovalStatus, "Approved")
                .SetProperty(job => job.UpdatedAt, now));

        if (updated == 0)
        {
            TempData["JobPostError"] = "Tin không tồn tại hoặc đã được xử lý.";
        }
        else
        {
            TempData["JobPostSuccess"] = "Đã duyệt tin tuyển dụng.";
        }

        return RedirectToRoute("AdminJobPosts");
    }
    [HttpGet("/Admin/JobPosts/{id:int}", Name = "AdminJobPostDetails")]
    public async Task<IActionResult> Details(int id)
    {
        var job = await _context.JobPosts
            .AsNoTracking()
            .Include(item => item.Company)
            .FirstOrDefaultAsync(item => item.JobPostId == id);

        if (job is null)
        {
            return NotFound();
        }

        return View(job);
    }
}