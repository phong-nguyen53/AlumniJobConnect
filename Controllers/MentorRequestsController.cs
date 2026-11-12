using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Controllers;

[Authorize(Roles = "Alumni")]
public class MentorRequestsController : Controller
{
    private readonly AlumniJobConnectContext _context;
    public MentorRequestsController(AlumniJobConnectContext context) => _context = context;

    public sealed class RequestInput
    {
        [Range(1, int.MaxValue)]
        public int MentorUserId { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập nội dung cần tư vấn.")]
        [StringLength(1000, MinimumLength = 10, ErrorMessage = "Nội dung cần từ 10 đến 1.000 ký tự.")]
        public string Message { get; set; } = "";
    }

    public sealed class MentorInfo
    {
        public int UserId { get; set; }
        public string FullName { get; set; } = "";
        public string Expertise { get; set; } = "";
    }

    private Task<MentorInfo?> FindMentor(int mentorUserId) => _context.Database.SqlQuery<MentorInfo>($"""
        SELECT u.UserId, u.FullName, m.Expertise
        FROM dbo.Users AS u
        JOIN dbo.MentorProfiles AS m ON m.UserId = u.UserId
        JOIN dbo.AlumniProfiles AS p ON p.UserId = u.UserId
        WHERE u.UserId = {mentorUserId} AND u.IsActive = 1 AND m.IsAvailable = 1
          AND (p.IsGraduationVerified = 1 OR p.GraduationConfirmedAtUtc IS NOT NULL)
          AND EXISTS (SELECT 1 FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.RoleId = ur.RoleId
                      WHERE ur.UserId = u.UserId AND r.RoleName = 'Alumni')
        """).SingleOrDefaultAsync();

    [HttpGet("/MentorRequests/Create", Name = "CreateMentorRequest")]
    public async Task<IActionResult> Create(int mentorUserId)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var studentId))
            return Forbid();
        if (mentorUserId == studentId) return BadRequest("Bạn không thể gửi yêu cầu cho chính mình.");
        var mentor = await FindMentor(mentorUserId);
        if (mentor == null) return NotFound("Cựu sinh viên hiện không nhận tư vấn.");
        ViewData["Mentor"] = mentor;
        return View(new RequestInput { MentorUserId = mentorUserId });
    }

    [HttpPost("/MentorRequests/Create", Name = "CreateMentorRequestSubmit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RequestInput input)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var studentId))
            return Forbid();
        if (input.MentorUserId == studentId) return BadRequest("Bạn không thể gửi yêu cầu cho chính mình.");
        input.Message = (input.Message ?? "").Trim();
        if (input.Message.Length < 10 || input.Message.Length > 1000)
            ModelState.AddModelError(nameof(input.Message), "Nội dung cần từ 10 đến 1.000 ký tự.");
        var mentor = await FindMentor(input.MentorUserId);
        if (mentor == null) return NotFound("Cựu sinh viên hiện không nhận tư vấn.");
        ViewData["Mentor"] = mentor;
        if (!ModelState.IsValid) return View(input);

        try
        {
            // Khóa kiểm tra và ghi trong cùng giao dịch để chống gửi trùng.
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var inserted = await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.MentorRequests
                    (StudentUserId, MentorUserId, Message, Status, CreatedAtUtc)
                SELECT {studentId}, m.UserId, {input.Message}, 'Pending', SYSUTCDATETIME()
                FROM dbo.MentorProfiles m WITH (UPDLOCK, HOLDLOCK)
                JOIN dbo.Users u WITH (HOLDLOCK) ON u.UserId = m.UserId
                JOIN dbo.AlumniProfiles p WITH (HOLDLOCK) ON p.UserId = m.UserId
                WHERE m.UserId = {input.MentorUserId} AND m.IsAvailable = 1 AND u.IsActive = 1
                  AND (p.IsGraduationVerified = 1 OR p.GraduationConfirmedAtUtc IS NOT NULL)
                  AND EXISTS (SELECT 1 FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.RoleId = ur.RoleId
                              WHERE ur.UserId = m.UserId AND r.RoleName = 'Alumni')
                  AND EXISTS (SELECT 1 FROM dbo.Users s WITH (HOLDLOCK)
                              JOIN dbo.UserRoles ur ON ur.UserId = s.UserId
                              JOIN dbo.Roles r ON r.RoleId = ur.RoleId
                              WHERE s.UserId = {studentId} AND s.IsActive = 1 AND r.RoleName = 'Alumni')
                  AND NOT EXISTS (SELECT 1 FROM dbo.MentorRequests q WITH (UPDLOCK, HOLDLOCK)
                                  WHERE q.StudentUserId = {studentId} AND q.MentorUserId = m.UserId
                                    AND q.Status IN ('Pending', 'Accepted'));
                """);
            await transaction.CommitAsync();
            if (inserted > 0)
            {
                TempData["MentorRequestSuccess"] = "Đã gửi yêu cầu tư vấn. Vui lòng chờ cựu sinh viên phản hồi.";
                return RedirectToRoute("MentorDirectory");
            }
            ModelState.AddModelError("", "Bạn đã có yêu cầu đang chờ hoặc được chấp nhận, hoặc tài khoản hiện không đủ điều kiện gửi yêu cầu.");
        }
        catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
        {
            ModelState.AddModelError("", "Bạn đã gửi yêu cầu cho cựu sinh viên này. Vui lòng chờ phản hồi.");
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            ModelState.AddModelError("", "Hệ thống đang bận. Bạn vui lòng gửi lại yêu cầu.");
        }
        return View(input);
    }
}
