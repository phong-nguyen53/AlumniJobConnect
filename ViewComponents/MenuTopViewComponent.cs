using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.ViewComponents
{
    public class MenuTopViewComponent : ViewComponent
    {
        private readonly AlumniJobConnectContext _context;

        public MenuTopViewComponent(AlumniJobConnectContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var menus = await _context.Menus
                .AsNoTracking()
                .ToListAsync();

            return View(menus);
        }
    }
}