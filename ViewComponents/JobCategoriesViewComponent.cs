using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.ViewComponents
{
    public class JobCategoriesViewComponent : ViewComponent
    {
        private readonly AlumniJobConnectContext _context;

        public JobCategoriesViewComponent(AlumniJobConnectContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var categories = await _context.JobCategories
                .AsNoTracking()
                .Include(x => x.JobPosts)
                .ToListAsync();

            return View(categories);
        }
    }
}