using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AlumniJobConnect.Models;

namespace AlumniJobConnect.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class MentorProfilesController : Controller
    {
        private readonly AlumniJobConnectContext _context;

        public MentorProfilesController(AlumniJobConnectContext context)
        {
            _context = context;
        }

        // GET: Admin/MentorProfiles
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var profiles = await _context.MentorProfiles
                .AsNoTracking()
                .OrderByDescending(x => x.IsAvailable)
                .ThenBy(x => x.UserId)
                .ToListAsync();

            var userIds = profiles.Select(x => x.UserId).ToList();

            ViewData["UserNames"] = await _context.Users
                .AsNoTracking()
                .Where(x => userIds.Contains(x.UserId))
                .ToDictionaryAsync(x => x.UserId, x => x.FullName);

            return View(profiles);
        }
        [HttpGet]
        public async Task<IActionResult> Details(int? id)
        {
            if (!id.HasValue)
            {
                return NotFound();
            }

            var profile = await _context.MentorProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == id.Value);

            if (profile == null)
            {
                return NotFound();
            }

            var user = await _context.Users
                .AsNoTracking()
                .Where(x => x.UserId == profile.UserId)
                .Select(x => new
                {
                    x.FullName,
                    x.Email,
                    x.IsActive
                })
                .FirstOrDefaultAsync();

            ViewData["FullName"] = user?.FullName
                ?? $"Tài khoản #{profile.UserId}";

            ViewData["Email"] = user?.Email;
            ViewData["AccountActive"] = user?.IsActive ?? false;

            return View("Details", profile);
        }
    }
}
