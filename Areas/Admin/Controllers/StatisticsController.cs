using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AlumniJobConnect.Areas.Admin.ViewModels;
using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class StatisticsController : Controller
{
    private readonly AlumniJobConnectContext _context;
    public StatisticsController(AlumniJobConnectContext context) => _context = context;

    [HttpGet("/Admin/Statistics")]
    [HttpGet("/Admin/Statistics/Index")]
    public async Task<IActionResult> Index(DateTime? from, DateTime? to)
    {
        var ct = HttpContext.RequestAborted;
        // Calendar dates: use the same convention as CreatedAt / AppliedAt in this database.
        var today = DateTime.Today;
        var defaultFrom = new DateTime(today.Year, today.Month, 1).AddMonths(-11);
        var start = (from ?? defaultFrom).Date;
        var end = (to ?? today).Date;
        var months = (end.Year - start.Year) * 12 + end.Month - start.Month + 1;
        if (!ModelState.IsValid || start > end || months > 36 ||
            start.Year < 1900 || end.Year > 9998)
        {
            ModelState.AddModelError("", "Khoảng ngày không hợp lệ. Chọn từ ngày không sau đến ngày và tối đa 36 tháng. Đang hiển thị lại 12 tháng gần nhất.");
            start = defaultFrom;
            end = today;
        }
        var exclusiveEnd = end.AddDays(1);
        var vm = new AdminDashboardViewModel { From = start, To = end };

        // Await sequentially: one DbContext does not support concurrent queries.
        vm.TotalUsers = await _context.Users.CountAsync(ct);
        vm.TotalProfiles = await _context.AlumniProfiles.CountAsync(ct);
        vm.TotalCompanies = await _context.Companies.CountAsync(ct);
        vm.TotalJobPosts = await _context.JobPosts.CountAsync(ct);
        vm.TotalApplications = await _context.Applications.CountAsync(ct);
        vm.PendingJobPosts = await _context.JobPosts.CountAsync(x => x.ApprovalStatus == "Pending", ct);
        vm.PendingCompanies = await _context.Companies.CountAsync(x => x.VerificationStatus == "Pending", ct);
        vm.PeriodUsers = await _context.Users.CountAsync(x => x.CreatedAt >= start && x.CreatedAt < exclusiveEnd, ct);
        vm.PeriodCompanies = await _context.Companies.CountAsync(x => x.CreatedAt >= start && x.CreatedAt < exclusiveEnd, ct);
        vm.PeriodJobs = await _context.JobPosts.CountAsync(x => x.CreatedAt >= start && x.CreatedAt < exclusiveEnd, ct);
        var applications = _context.Applications.AsNoTracking()
            .Where(x => x.AppliedAt >= start && x.AppliedAt < exclusiveEnd);
        vm.PeriodApplications = await applications.CountAsync(ct);

        var monthly = await applications.GroupBy(x => new { x.AppliedAt.Year, x.AppliedAt.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync(ct);
        var monthMap = monthly.ToDictionary(x => (x.Year, x.Month), x => x.Count);
        for (var cursor = new DateTime(start.Year, start.Month, 1);
             cursor <= new DateTime(end.Year, end.Month, 1); cursor = cursor.AddMonths(1))
        {
            vm.Months.Add(new DashboardCount {
                Label = cursor.ToString("MM/yyyy", CultureInfo.InvariantCulture),
                Count = monthMap.TryGetValue((cursor.Year, cursor.Month), out var count) ? count : 0
            });
        }
        // Read the display name in the SQL projection, not EF.Property inside ToDictionary.
        vm.Statuses = await applications
            .GroupBy(x => new { x.ApplicationStatusId, x.ApplicationStatus.StatusName })
            .Select(g => new DashboardCount { Label = g.Key.StatusName, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Label).ToListAsync(ct);
        vm.TopCompanies = await applications
            .GroupBy(x => new { x.JobPost.CompanyId, x.JobPost.Company.CompanyName })
            .Select(g => new DashboardCompany {
                CompanyId = g.Key.CompanyId, Name = g.Key.CompanyName, Applications = g.Count()
            }).OrderByDescending(x => x.Applications).ThenBy(x => x.CompanyId)
            .Take(5).ToListAsync(ct);
        vm.PendingJobs = await _context.JobPosts.AsNoTracking()
            .Where(x => x.ApprovalStatus == "Pending")
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.JobPostId)
            .Take(5).Select(x => new DashboardPending {
                Title = x.Title, Description = x.Company.CompanyName, CreatedAt = x.CreatedAt
            }).ToListAsync(ct);
        vm.PendingBusinesses = await _context.Companies.AsNoTracking()
            .Where(x => x.VerificationStatus == "Pending")
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.CompanyId)
            .Take(5).Select(x => new DashboardPending {
                Title = x.CompanyName, Description = x.Industry ?? "Chưa cập nhật lĩnh vực", CreatedAt = x.CreatedAt
            }).ToListAsync(ct);
        return View(vm);
    }
}
