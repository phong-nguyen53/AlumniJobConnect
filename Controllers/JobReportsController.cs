using AlumniJobConnect.Features.JobReports;
using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AlumniJobConnect.Controllers;

[Authorize(Roles = "Alumni")]
public class JobReportsController : ReportsAccessController
{
    public JobReportsController(AlumniJobConnectContext db) : base(db) { }

    private IQueryable<JobPost> VisibleJob(int id)
    {
        var today = DateOnly.FromDateTime(ReportLabels.Now);
        return Db.JobPosts.AsNoTracking().Include(j => j.Company).Where(j =>
            j.JobPostId == id && j.IsPublished && j.ApprovalStatus == "Approved" &&
            (j.Deadline == null || j.Deadline >= today) && j.Company.User.IsActive &&
            j.Company.User.UserRoles.Any(r => r.Role.RoleName == "Employer"));
    }

    [HttpGet]
    public async Task<IActionResult> Create(int jobPostId)
    {
        if (!await ActiveRole("Alumni")) return Forbid();
        var job = await VisibleJob(jobPostId).SingleOrDefaultAsync();
        if (job == null) return NotFound("Tin không tồn tại hoặc không còn hiển thị.");
        if (await Db.Database.SqlQuery<int>($"SELECT ReportId AS Value FROM dbo.JobReports WHERE JobPostId={jobPostId} AND ReporterUserId={UserId}").AnyAsync())
        {
            TempData["ReportMessage"] = "Bạn đã báo cáo tin này. Hãy theo dõi kết quả trong danh sách.";
            return RedirectToAction(nameof(Index));
        }
        return View(new ReportInput { JobPostId = job.JobPostId, JobTitle = job.Title, CompanyName = job.Company.CompanyName });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("JobPostId,Reason,Details")] ReportInput input)
    {
        if (!await ActiveRole("Alumni")) return Forbid();
        var job = await VisibleJob(input.JobPostId).SingleOrDefaultAsync();
        if (job == null) return NotFound("Tin không tồn tại hoặc không còn nhận báo cáo.");
        input.JobTitle = job.Title;
        input.CompanyName = job.Company.CompanyName;
        // Hai nhãn chỉ hiển thị, luôn lấy từ SQL, không nhận từ người dùng.
        ModelState.Remove(nameof(input.JobTitle)); ModelState.Remove(nameof(input.CompanyName));
        input.Details = (input.Details ?? "").Trim();
        if (!ReportLabels.Reasons.ContainsKey(input.Reason ?? "")) ModelState.AddModelError("Reason", "Lý do không hợp lệ.");
        if (string.IsNullOrWhiteSpace(input.Details)) ModelState.AddModelError("Details", "Vui lòng mô tả nội dung cần kiểm tra.");
        if (!ModelState.IsValid) return View(input);
        var evidence = JsonSerializer.Serialize(new {
            job.JobPostId, job.Title, CompanyName = job.Company.CompanyName,
            job.JobDescription, job.Requirements, job.Benefits, job.WorkLocation,
            job.EmploymentType, job.SalaryMin, job.SalaryMax, job.CurrencyCode, job.Quantity, job.Deadline
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var now = ReportLabels.Now;
        try
        {
            await Db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT dbo.JobReports(JobPostId,ReporterUserId,JobTitle,CompanyName,EvidenceJson,Reason,Details,State,CreatedAt,UpdatedAt,Version)
                VALUES({job.JobPostId},{UserId},{job.Title},{job.Company.CompanyName},{evidence},{input.Reason},{input.Details},'Pending',{now},{now},1)");
            TempData["ReportMessage"] = "Đã nhận báo cáo. Quản trị viên sẽ kiểm tra và thông báo kết quả cho bạn.";
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            TempData["ReportMessage"] = "Bạn đã báo cáo tin này. Không cần gửi lại.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1)
    {
        if (!await ActiveRole("Alumni")) return Forbid();
        page = Math.Clamp(page, 1, 100000);
        var rows = await Db.Database.SqlQuery<ReportRow>($@"SELECT * FROM dbo.JobReports
            WHERE ReporterUserId={UserId} ORDER BY ReportId DESC OFFSET {(page - 1) * 15} ROWS FETCH NEXT 16 ROWS ONLY").ToListAsync();
        return View(new ReportPage { Items = rows.Take(15).ToList(), Page = page, HasMore = rows.Count > 15 });
    }
}
