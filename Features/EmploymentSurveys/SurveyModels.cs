using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using AlumniJobConnect.Models;

namespace AlumniJobConnect.Features.EmploymentSurveys;

public static class SurveyLabels
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    public static DateTime Now => DateTime.UtcNow.AddHours(7);
    public static readonly Dictionary<string, string> Statuses = new() {
        ["Employed"] = "Đã có việc làm", ["Seeking"] = "Đang tìm việc",
        ["Studying"] = "Đang học tiếp", ["Other"] = "Tình trạng khác"
    };
    public static readonly Dictionary<string, string> Salaries = new() {
        ["Under5"] = "Dưới 5 triệu", ["5to10"] = "Từ 5 đến dưới 10 triệu",
        ["10to15"] = "Từ 10 đến dưới 15 triệu", ["15to20"] = "Từ 15 đến dưới 20 triệu",
        ["Over20"] = "Từ 20 triệu trở lên"
    };
    public static string Status(string code) => Statuses.GetValueOrDefault(code, code);
    public static string Salary(string? code) => code == null ? "Không cung cấp" : Salaries.GetValueOrDefault(code, "Không cung cấp");
    public static decimal Percent(int part, int total) => total == 0 ? 0 : Math.Round(100m * part / total, 1);
}
public class SurveyRow
{
    public int SurveyId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public int? TargetCourseId { get; set; }
    public string TargetCourseName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsClosed { get; set; }
    public int Version { get; set; }
    [NotMapped] public bool IsOpen => !IsClosed && StartsOn <= SurveyLabels.Today && EndsOn >= SurveyLabels.Today;
    [NotMapped] public string State => IsClosed ? "Đã đóng" : StartsOn > SurveyLabels.Today ? "Chưa mở" : EndsOn < SurveyLabels.Today ? "Hết hạn" : "Đang mở";
}
public sealed class SurveyListRow : SurveyRow
{
    public int RecipientCount { get; set; }
    public int ResponseCount { get; set; }
    public bool HasResponded { get; set; }
}
public sealed class SurveyForm
{
    public int SurveyId { get; set; }
    public int Version { get; set; }
    [Required(ErrorMessage = "Nhập tên đợt khảo sát."), StringLength(200)]
    public string Title { get; set; } = "";
    [StringLength(2000)] public string? Description { get; set; }
    [DataType(DataType.Date)] public DateOnly StartsOn { get; set; } = SurveyLabels.Today;
    [DataType(DataType.Date)] public DateOnly EndsOn { get; set; } = SurveyLabels.Today.AddDays(30);
    public int? TargetCourseId { get; set; }
    [ValidateNever] public string TargetCourseName { get; set; } = "";
    [ValidateNever] public List<AcademicCourse> Courses { get; set; } = new();
}
public sealed class ResponseInput
{
    public int SurveyId { get; set; }
    public int Version { get; set; }
    [Required(ErrorMessage = "Chọn tình trạng việc làm.")]
    public string EmploymentStatus { get; set; } = "";
    [StringLength(200)] public string? CompanyName { get; set; }
    [StringLength(150)] public string? JobTitle { get; set; }
    public bool? IsMajorRelated { get; set; }
    public string? SalaryRange { get; set; }
    public bool NeedsSupport { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
}
public sealed class ResponseRow
{
    public int SurveyId { get; set; }
    public int UserId { get; set; }
    public string EmploymentStatus { get; set; } = "";
    public string? CompanyName { get; set; }
    public string? JobTitle { get; set; }
    public bool? IsMajorRelated { get; set; }
    public string? SalaryRange { get; set; }
    public bool NeedsSupport { get; set; }
    public string? Notes { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Version { get; set; }
}
public sealed class RecipientRow
{
    public int UserId { get; set; }
    public int AlumniId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? StudentCode { get; set; }
    public string CourseName { get; set; } = "";
    public short GraduationYear { get; set; }
}
public sealed class ParticipantRow
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? StudentCode { get; set; }
    public string CourseName { get; set; } = "";
    public bool HasResponded { get; set; }
    public string? EmploymentStatus { get; set; }
    public string? CompanyName { get; set; }
    public string? JobTitle { get; set; }
    public bool? IsMajorRelated { get; set; }
    public string? SalaryRange { get; set; }
    public bool? NeedsSupport { get; set; }
    public string? Notes { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
public sealed class SurveyAnswerPage
{
    public SurveyRow Survey { get; set; } = new();
    public ResponseInput Input { get; set; } = new();
    public DateTime? UpdatedAt { get; set; }
}
public sealed class SurveyResultsPage
{
    public SurveyRow Survey { get; set; } = new();
    public List<ParticipantRow> Participants { get; set; } = new();
    public List<ParticipantRow> FilteredParticipants { get; set; } = new();
    public string Filter { get; set; } = "all";
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int FilteredCount { get; set; }
    public int Total => Participants.Count;
    public int Responded => Participants.Count(p => p.HasResponded);
    public int Employed => Participants.Count(p => p.EmploymentStatus == "Employed");
    public int Related => Participants.Count(p => p.EmploymentStatus == "Employed" && p.IsMajorRelated == true);
    public int NeedSupport => Participants.Count(p => p.HasResponded && p.NeedsSupport == true);
}
public sealed class SurveyListPage
{
    public List<SurveyListRow> Items { get; set; } = new();
    public int Page { get; set; }
    public bool HasMore { get; set; }
}
public sealed class SurveyOperationException : Exception
{
    public SurveyOperationException(string message) : base(message) { }
}
