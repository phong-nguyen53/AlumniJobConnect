using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.ViewComponents;

public class JobListingViewComponent : ViewComponent
{
    private readonly AlumniJobConnectContext _context;

    public JobListingViewComponent(AlumniJobConnectContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var jobs = await _context.JobPosts
            .AsNoTracking()
            .Include(x => x.JobCategory)
            .Where(x => x.IsPublished
                && (x.Deadline == null || x.Deadline >= today))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return View(jobs);
    }
}