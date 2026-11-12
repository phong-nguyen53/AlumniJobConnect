using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
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

            var categories = await _context.JobCategories
                .AsNoTracking()
                .OrderBy(x => x.CategoryName)
                .ToListAsync();

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

        [HttpGet]
        public async Task<IActionResult> DoanhNghiep()
        {
            var companies = await _context.Companies
                .AsNoTracking()
                .OrderBy(company => company.CompanyName)
                .ThenBy(company => company.CompanyId)
                .ToListAsync();

            return View(companies);
        }

        [HttpGet]
        public IActionResult Alumni()
        {
            return RedirectToRoute("MentorDirectory");
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

        public sealed record CompanyEventItem(
            int EventId,
            string EventName,
            string CompanyName,
            string? Location,
            DateTime StartAt,
            DateTime? RegistrationDeadline,
            string? Description,
            int CreatedByUserId);
       

        [HttpGet]
        [HttpGet]
        public async Task<IActionResult> Events([FromQuery] bool mine = false)
        {
            var registeredEventIds = new HashSet<int>();
            var showMyEvents = false;

            if (User.IsInRole("Alumni") &&
                int.TryParse(
                    User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var userId))
            {
                var ids = await _context.Set<EventRegistration>()
                    .AsNoTracking()
                    .Where(registration => registration.UserId == userId)
                    .Select(registration => registration.EventId)
                    .ToListAsync();

                registeredEventIds = ids.ToHashSet();
                showMyEvents = mine;
            }

            var eventQuery = _context.Events.AsNoTracking();

            if (showMyEvents)
            {
                var ids = registeredEventIds.ToArray();
                eventQuery = eventQuery.Where(item => ids.Contains(item.EventId));
            }

            var events = await (
                from item in eventQuery
                join company in _context.Companies.AsNoTracking()
                    on item.CreatedByUserId equals company.UserId
                orderby item.StartAt descending, item.EventId descending
                select new CompanyEventItem(
                    item.EventId,
                    item.EventName,
                    company.CompanyName,
                    item.Location,
                    item.StartAt,
                    item.RegistrationDeadline,
                    item.Description,
                    item.CreatedByUserId)
                
            ).ToListAsync();

            ViewData["RegisteredEventIds"] = registeredEventIds;
            ViewData["ShowMyEvents"] = showMyEvents;

            return View(events);
        }

        [HttpPost("/Home/Events/Register", Name = "RegisterCompanyEvent")]
        [Authorize(Roles = "Alumni")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterEvent([FromForm] int eventId)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return Forbid();
            }

            if (!ModelState.IsValid || eventId <= 0)
            {
                return BadRequest();
            }

            // Ngày giờ sự kiện được lưu theo giờ Việt Nam.
            var now = DateTime.UtcNow.AddHours(7);

            var inserted = await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.EventRegistrations (EventId, UserId)
                SELECT e.EventId, {userId}
                FROM dbo.Events AS e WITH (UPDLOCK, HOLDLOCK)
                WHERE e.EventId = {eventId}
                  AND e.EventStatus = 'Upcoming'
                  AND e.StartAt > {now}
                  AND (
                      e.RegistrationDeadline IS NULL
                      OR e.RegistrationDeadline >= {now}
                  )
                  AND EXISTS (
                      SELECT 1
                      FROM dbo.Companies AS c
                      WHERE c.UserId = e.CreatedByUserId
                  )
                  AND EXISTS (
                      SELECT 1
                      FROM dbo.Users AS u
                      JOIN dbo.UserRoles AS ur ON ur.UserId = u.UserId
                      JOIN dbo.Roles AS r ON r.RoleId = ur.RoleId
                      WHERE u.UserId = {userId}
                        AND u.IsActive = 1
                        AND r.RoleName = 'Alumni'
                  )
                  AND NOT EXISTS (
                      SELECT 1
                      FROM dbo.EventRegistrations AS reg WITH (UPDLOCK, HOLDLOCK)
                      WHERE reg.EventId = e.EventId
                        AND reg.UserId = {userId}
                  )
                  AND (
                      e.Capacity IS NULL OR e.Capacity > (
                          SELECT COUNT(*)
                          FROM dbo.EventRegistrations AS reg WITH (UPDLOCK, HOLDLOCK)
                          WHERE reg.EventId = e.EventId
                      )
                  );
                """);

            if (inserted > 0)
            {
                TempData["EventRegistrationSuccess"] =
                    "Đăng ký tham gia thành công.";
            }
            else
            {
                TempData["EventRegistrationError"] =
                    "Không thể đăng ký. Vui lòng kiểm tra trạng thái sự kiện, thời hạn, số chỗ và lượt đăng ký của bạn.";
            }

            return RedirectToAction(nameof(Events));
        }

        [HttpPost("/Home/Events/CancelRegistration", Name = "CancelCompanyEventRegistration")]
        [Authorize(Roles = "Alumni")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelEventRegistration([FromForm] int eventId)
        {
            if (!int.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var userId))
            {
                return Forbid();
            }

            if (!ModelState.IsValid || eventId <= 0)
            {
                return BadRequest();
            }

            // Cùng cách tính giờ Việt Nam với hàm đăng ký.
            var now = DateTime.UtcNow.AddHours(7);

            var deleted = await _context.Database.ExecuteSqlInterpolatedAsync($"""
        DELETE registration
        FROM dbo.EventRegistrations AS registration
        INNER JOIN dbo.Events AS e
            ON e.EventId = registration.EventId
        WHERE registration.EventId = {eventId}
          AND registration.UserId = {userId}
          AND registration.AttendanceStatus = 'Registered'
          AND e.StartAt > {now};
        """);

            if (deleted > 0)
            {
                TempData["EventRegistrationSuccess"] = "Đã hủy đăng ký sự kiện.";
            }
            else
            {
                TempData["EventRegistrationError"] =
                    "Không thể hủy lượt đăng ký này. Sự kiện có thể đã bắt đầu hoặc lượt đăng ký không còn hiệu lực.";
            }

            return RedirectToAction(nameof(Events), new { mine = true });
        }

        public sealed record EventParticipantItem(
    string FullName,
    DateTime RegisteredAt);

        [HttpGet("/Home/Events/{eventId:int}/Participants", Name = "CompanyEventParticipants")]
        [Authorize(Roles = "Employer")]
        public async Task<IActionResult> EventParticipants(int eventId)
        {
            if (!int.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var userId))
            {
                return Forbid();
            }

            var companyEvent = await _context.Events
                .AsNoTracking()
                .Where(item => item.EventId == eventId &&
                               item.CreatedByUserId == userId)
                .Select(item => new { item.EventName })
                .SingleOrDefaultAsync();

            if (companyEvent is null)
            {
                return NotFound();
            }

            var participants = await (
                from registration in _context.Set<EventRegistration>().AsNoTracking()
                join student in _context.Users.AsNoTracking()
                    on registration.UserId equals student.UserId
                where registration.EventId == eventId
                orderby registration.RegisteredAt descending, registration.UserId
                select new EventParticipantItem(
                    student.FullName,
                    registration.RegisteredAt)
            ).ToListAsync();

            ViewData["EventName"] = companyEvent.EventName;

            return View(participants);
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