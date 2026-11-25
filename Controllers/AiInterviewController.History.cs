using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AlumniJobConnect.Models;

namespace AlumniJobConnect.Controllers;

public partial class AiInterviewController
{
    public sealed class InterviewSession
    {
        public Guid SessionId { get; set; }
        public int JobPostId { get; set; }
        public string JobTitle { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string Level { get; set; } = "graduate";
        public string ContextJson { get; set; } = "";
        public int AnswersCount { get; set; }
        public bool IsCompleted { get; set; }
        public int Version { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
    private sealed class InterviewConflictException : Exception { }
    private static string? LevelName(string level) => level switch {
        "intern" => "Thực tập sinh", "graduate" => "Mới tốt nghiệp",
        "experienced" => "Có kinh nghiệm", _ => null
    };
    private async Task<int?> CurrentActiveUserAsync(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        return await _context.Users.AsNoTracking().AnyAsync(u => u.UserId == id && u.IsActive, ct) ? id : null;
    }
    private async Task<InterviewSession?> FindSessionAsync(Guid id, int userId, CancellationToken ct)
    {
        var rows = await _context.Database.SqlQuery<InterviewSession>($"""
            SELECT SessionId, JobPostId, JobTitle, CompanyName, Level, ContextJson,
                AnswersCount, IsCompleted, Version, CreatedAtUtc, UpdatedAtUtc
            FROM dbo.AiInterviewSessions WHERE SessionId = {id} AND UserId = {userId}
            """).ToListAsync(ct);
        return rows.SingleOrDefault();
    }
    private async Task<List<ChatTurn>> ReadTurnsAsync(Guid id, int userId, CancellationToken ct) =>
        await _context.Database.SqlQuery<ChatTurn>($"""
            SELECT m.Role, m.Content FROM dbo.AiInterviewMessages m
            JOIN dbo.AiInterviewSessions s ON s.SessionId = m.SessionId
            WHERE s.SessionId = {id} AND s.UserId = {userId}
            ORDER BY m.MessageId
            """).ToListAsync(ct);

    [HttpGet]
    public async Task<IActionResult> History(int page = 1, CancellationToken ct = default)
    {
        var userId = await CurrentActiveUserAsync(ct);
        if (!userId.HasValue) return StatusCode(403, new { error = "Tài khoản không hợp lệ hoặc đã bị khóa." });
        var offset = (Math.Clamp(page, 1, 10000) - 1) * 20;
        // Không tải nội dung câu trả lời khi chỉ xem danh sách.
        var rows = await _context.Database.SqlQuery<InterviewSession>($"""
            SELECT SessionId, JobPostId, JobTitle, CompanyName, Level,
                CAST(N'' AS nvarchar(max)) AS ContextJson, AnswersCount,
                IsCompleted, Version, CreatedAtUtc, UpdatedAtUtc
            FROM dbo.AiInterviewSessions WHERE UserId = {userId.Value}
            ORDER BY UpdatedAtUtc DESC, SessionId DESC
            OFFSET {offset} ROWS FETCH NEXT 21 ROWS ONLY
            """).ToListAsync(ct);
        return Json(new { items = rows.Take(20).Select(s => new {
            s.SessionId, s.JobPostId, s.JobTitle, s.CompanyName, s.Level,
            levelName = LevelName(s.Level), s.AnswersCount, s.IsCompleted,
            updatedAtUtc = DateTime.SpecifyKind(s.UpdatedAtUtc, DateTimeKind.Utc)
        }), hasMore = rows.Count > 20 });
    }
    [HttpGet]
    public async Task<IActionResult> Session(Guid id, CancellationToken ct)
    {
        var userId = await CurrentActiveUserAsync(ct);
        if (!userId.HasValue) return StatusCode(403, new { error = "Tài khoản không hợp lệ hoặc đã bị khóa." });
        var session = await FindSessionAsync(id, userId.Value, ct);
        if (session == null) return NotFound(new { error = "Không tìm thấy buổi luyện tập." });
        var turns = await ReadTurnsAsync(id, userId.Value, ct);
        return Json(new { session.SessionId, session.JobPostId, session.JobTitle,
            session.CompanyName, session.Level, session.AnswersCount, session.IsCompleted,
            session.Version, items = turns });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = await CurrentActiveUserAsync(ct);
        if (!userId.HasValue) return StatusCode(403, new { error = "Tài khoản không hợp lệ hoặc đã bị khóa." });
        var changed = await _context.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM dbo.AiInterviewSessions WHERE SessionId = {id} AND UserId = {userId.Value}
            """, ct);
        return changed == 0 ? NotFound(new { error = "Buổi luyện tập không còn tồn tại." }) : Json(new { success = true });
    }
    private async Task<InterviewSession> PersistTurnAsync(int userId,
        InterviewSession? session, JobPost? job, string level, string context,
        string? answer, string reply, int answers, CancellationToken ct)
    {
        var id = session?.SessionId ?? Guid.NewGuid();
        var now = DateTime.UtcNow;
        var completed = answers >= 5;
        var version = (session?.Version ?? 0) + 1;
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        if (session == null)
        {
            var jobId = job!.JobPostId;
            var jobTitle = job.Title;
            var companyName = job.Company?.CompanyName ?? "";
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.AiInterviewSessions
                    (SessionId, UserId, JobPostId, JobTitle, CompanyName, Level, ContextJson,
                     AnswersCount, IsCompleted, Version, CreatedAtUtc, UpdatedAtUtc)
                VALUES ({id}, {userId}, {jobId}, {jobTitle}, {companyName}, {level}, {context},
                    0, 0, 1, {now}, {now})
                """, ct);
        }
        else
        {
            var changed = await _context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.AiInterviewSessions SET AnswersCount = {answers},
                    IsCompleted = {completed}, Version = Version + 1, UpdatedAtUtc = {now}
                WHERE SessionId = {id} AND UserId = {userId} AND Version = {session.Version}
                    AND IsCompleted = 0
                """, ct);
            if (changed != 1) throw new InterviewConflictException();
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.AiInterviewMessages (SessionId, Role, Content, CreatedAtUtc)
                VALUES ({id}, N'user', {answer!}, {now})
                """, ct);
        }
        await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.AiInterviewMessages (SessionId, Role, Content, CreatedAtUtc)
            VALUES ({id}, N'assistant', {reply}, {now})
            """, ct);
        await transaction.CommitAsync(ct);
        return new InterviewSession { SessionId = id, AnswersCount = answers,
            IsCompleted = completed, Version = version };
    }
}
