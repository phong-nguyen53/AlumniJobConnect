using AlumniJobConnect.Features.JobReports;
using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin")]
public class JobReportsController : ReportsAccessController
{
    public JobReportsController(AlumniJobConnectContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> Index(string? state, int page = 1)
    {
        if (!await ActiveRole("Admin")) return Forbid();
        state = state != null && ReportLabels.States.ContainsKey(state) ? state : "";
        page = Math.Clamp(page, 1, 100000);
        var rows = await Db.Database.SqlQuery<ReportRow>($@"SELECT * FROM dbo.JobReports
            WHERE ({state}='' OR State={state}) ORDER BY ReportId DESC
            OFFSET {(page - 1) * 15} ROWS FETCH NEXT 16 ROWS ONLY").ToListAsync();
        return View(new ReportPage { Items = rows.Take(15).ToList(), State = state, Page = page, HasMore = rows.Count > 15 });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (!await ActiveRole("Admin")) return Forbid();
        var row = await Db.Database.SqlQuery<ReportRow>($"SELECT * FROM dbo.JobReports WHERE ReportId={id}").SingleOrDefaultAsync();
        if (row == null) return NotFound();
        ViewBag.Reporter = await Db.Users.AsNoTracking().Where(u => u.UserId == row.ReporterUserId).Select(u => u.FullName).SingleOrDefaultAsync();
        ViewBag.Handler = await Db.Users.AsNoTracking().Where(u => u.UserId == row.HandledByUserId).Select(u => u.FullName).SingleOrDefaultAsync();
        ViewBag.CurrentJob = await Db.JobPosts.AsNoTracking().Include(j => j.Company).SingleOrDefaultAsync(j => j.JobPostId == row.JobPostId);
        return View(row);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Process(int id, int version, string? decision, string? note)
    {
        if (!await ActiveRole("Admin")) return Forbid();
        note = note?.Trim();
        if (decision != "Reviewing" && decision != "Kept" && decision != "Removed")
            return BadRequest("Thao tác không hợp lệ.");
        if (decision != "Reviewing" && (string.IsNullOrWhiteSpace(note) || note.Length > 2000))
        {
            TempData["ReportError"] = "Vui lòng nhập kết quả kiểm tra từ 1 đến 2.000 ký tự.";
            return RedirectToAction(nameof(Details), new { id });
        }
        await using var tx = await Db.Database.BeginTransactionAsync();
        // Khóa báo cáo trong giao dịch để hai quản trị viên không xử lý ghi đè.
        var rows = await Db.Database.SqlQuery<ReportRow>($"SELECT * FROM dbo.JobReports WITH (UPDLOCK,HOLDLOCK) WHERE ReportId={id}").ToListAsync();
        var row = rows.SingleOrDefault();
        if (row == null) return NotFound();
        if (row.Version != version || row.State is "Kept" or "Removed" || (decision == "Reviewing" && row.State != "Pending"))
        {
            TempData["ReportError"] = "Báo cáo đã thay đổi hoặc đã xử lý. Vui lòng kiểm tra lại nội dung mới nhất.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var now = ReportLabels.Now;
        if (decision == "Removed")
        {
            // Gỡ khỏi công khai và yêu cầu xét duyệt lại trước khi được hiển thị.
            // Không xóa tin, file CV hoặc đơn ứng tuyển.
            await Db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.JobPosts
                SET IsPublished=0, ApprovalStatus='Rejected', UpdatedAt={now} WHERE JobPostId={row.JobPostId}");
        }
        var finalNote = decision == "Reviewing" ? null : note;
        await Db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.JobReports SET State={decision},AdminNote={finalNote},
            HandledByUserId={UserId},UpdatedAt={now},Version=Version+1 WHERE ReportId={id}");
        if (decision != "Reviewing")
        {
            Db.Notifications.Add(new Notification {
                UserId = row.ReporterUserId, Title = "Kết quả xử lý báo cáo tuyển dụng",
                Message = $"Tin: {row.JobTitle}. {(decision == "Removed" ? "Đã gỡ tin khỏi trang công khai." : "Giữ tin sau khi kiểm tra.")} Kết quả: {note}",
                NotificationType = "JOB_REPORT", RelatedUrl = Url.Action("Index", "JobReports", new { area = "" }),
                IsRead = false, CreatedAt = now
            });
            await Db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        TempData["ReportMessage"] = decision == "Reviewing" ? "Đã chuyển sang đang xử lý." : "Đã lưu kết quả và gửi thông báo cho người báo cáo.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
