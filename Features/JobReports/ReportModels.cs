using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace AlumniJobConnect.Features.JobReports;

public static class ReportLabels
{
    public static readonly IReadOnlyDictionary<string, string> Reasons = new Dictionary<string, string>
    {
        ["Scam"] = "Nghi ngờ lừa đảo", ["Fee"] = "Yêu cầu đóng phí",
        ["WrongInfo"] = "Thông tin sai lệch", ["Inappropriate"] = "Nội dung không phù hợp",
        ["Other"] = "Lý do khác"
    };
    public static readonly IReadOnlyDictionary<string, string> States = new Dictionary<string, string>
    {
        ["Pending"] = "Chờ xử lý", ["Reviewing"] = "Đang xử lý",
        ["Kept"] = "Đã xử lý · Giữ tin", ["Removed"] = "Đã xử lý · Gỡ tin"
    };
    public static string Reason(string value) => Reasons.TryGetValue(value, out var label) ? label : value;
    public static string State(string value) => States.TryGetValue(value, out var label) ? label : value;
    public static DateTime Now => DateTime.UtcNow.AddHours(7);
}

public class ReportInput
{
    public int JobPostId { get; set; }
    [Required(ErrorMessage = "Vui lòng chọn lý do báo cáo.")]
    public string Reason { get; set; } = "";
    [Required(ErrorMessage = "Vui lòng mô tả nội dung cần kiểm tra.")]
    [StringLength(2000, ErrorMessage = "Nội dung tối đa 2.000 ký tự.")]
    public string Details { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string CompanyName { get; set; } = "";
}

public class ReportRow
{
    public int ReportId { get; set; }
    public int JobPostId { get; set; }
    public int ReporterUserId { get; set; }
    public string JobTitle { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string EvidenceJson { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Details { get; set; } = "";
    public string State { get; set; } = "";
    public string? AdminNote { get; set; }
    public int? HandledByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Version { get; set; }
}

public class ReportPage
{
    public List<ReportRow> Items { get; set; } = new();
    public int Page { get; set; }
    public bool HasMore { get; set; }
    public string State { get; set; } = "";
}

// Không cần thêm DbSet hoặc thay đổi context scaffold.
public abstract class ReportsAccessController : Controller
{
    protected readonly AlumniJobConnectContext Db;
    protected ReportsAccessController(AlumniJobConnectContext db) => Db = db;
    protected int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    protected Task<bool> ActiveRole(string role) => Db.Users.AsNoTracking().AnyAsync(u =>
        u.UserId == UserId && u.IsActive && u.UserRoles.Any(r => r.Role.RoleName == role));
}
