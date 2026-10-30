using System.Diagnostics;
using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly AlumniJobConnectContext _context;

        public HomeController(
            ILogger<HomeController> logger,
            AlumniJobConnectContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var today = DateOnly.FromDateTime(DateTime.Today);

            // Danh sách ngành nghề cho ô chọn Category.
            var categories = await _context.JobCategories
                .AsNoTracking()
                .OrderBy(x => x.CategoryName)
                .ToListAsync();

            // Địa điểm lấy từ các tin được phép hiển thị.
            var locations = await _context.JobPosts
                .AsNoTracking()
                .Where(x =>
                    x.ApprovalStatus == "Approved"
                    && x.IsPublished
                    && (x.Deadline == null || x.Deadline >= today)
                    && x.WorkLocation != null
                    && x.WorkLocation.Trim() != "")
                .Select(x => x.WorkLocation!.Trim())
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            ViewBag.Categories = new SelectList(
                categories,
                "JobCategoryId",
                "CategoryName");

            ViewBag.Locations = new SelectList(locations);

            return View();
        }

        public IActionResult DoanhNghiep()
        {
            return View();
        }

        public IActionResult Alumni()
        {
            return View();
        }



        [HttpGet]
        public async Task<IActionResult> jobs(
      string? keyword,
      int? categoryId,
      string? location)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);

            keyword = keyword?.Trim();
            location = location?.Trim();

            var query = _context.JobPosts
                .AsNoTracking()
                .Include(x => x.JobCategory)
                .Where(x =>
                    x.ApprovalStatus == "Approved"
                    && x.IsPublished
                    && (x.Deadline == null || x.Deadline >= today));

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(x => x.Title.Contains(keyword));
            }

            if (categoryId.HasValue)
            {
                query = query.Where(x => x.JobCategoryId == categoryId.Value);
            }

            if (!string.IsNullOrWhiteSpace(location))
            {
                query = query.Where(x =>
                    x.WorkLocation != null
                    && x.WorkLocation.Trim() == location);
            }

            var jobs = await query
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            ViewBag.Keyword = keyword;

            return View(jobs);
        }


        public IActionResult Events()
        {
            return View();
        }

        public IActionResult Profile()
        {
            return RedirectToAction("Index", "Profile");
        }

        public IActionResult ungtuyen()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(
            Duration = 0,
            Location = ResponseCacheLocation.None,
            NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id
                    ?? HttpContext.TraceIdentifier
            });
        }
    }
}