using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Controllers;

[Authorize(Roles = "Alumni")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MentorChatController : Controller
{
    private readonly AlumniJobConnectContext _context;

    public MentorChatController(AlumniJobConnectContext context)
    {
        _context = context;
    }

    public sealed class ConversationItem
    {
        public int ConversationId { get; set; }
        public int OtherUserId { get; set; }
        public string FullName { get; set; } = "";
        public string? LastMessage { get; set; }
        public DateTime LastActivityUtc { get; set; }
    }

    public sealed class MessageItem
    {
        public long MessageId { get; set; }
        public int SenderUserId { get; set; }
        public string Content { get; set; } = "";
        public DateTime SentAtUtc { get; set; }
    }

    public sealed class ChatPage
    {
        public int CurrentUserId { get; set; }
        public int? ConversationId { get; set; }

        public IReadOnlyList<ConversationItem> Conversations { get; set; }
            = Array.Empty<ConversationItem>();
    }

    private int? CurrentUserId()
    {
        return int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var id)
            ? id
            : null;
    }

    private async Task<int?> ActiveUserIdAsync()
    {
        var id = CurrentUserId();

        if (!id.HasValue)
            return null;

        var allowed = await _context.Users
            .AsNoTracking()
            .AnyAsync(u =>
                u.UserId == id.Value &&
                u.IsActive &&
                u.UserRoles.Any(r => r.Role.RoleName == "Alumni"));

        return allowed ? id : null;
    }

    private Task<bool> CanAccessAsync(int conversationId, int userId)
    {
        return _context.Database.SqlQuery<int>($"""
            SELECT c.ConversationId AS [Value]
            FROM dbo.MentorConversations AS c
            WHERE c.ConversationId = {conversationId}
              AND (
                  c.StudentUserId = {userId}
                  OR c.MentorUserId = {userId}
              )
            """).AnyAsync();
    }

    // Trang hộp thư.
    [HttpGet("/MentorChat", Name = "MentorChat")]
    public async Task<IActionResult> Index(int? conversationId)
    {
        var userId = await ActiveUserIdAsync();

        if (!userId.HasValue)
            return Forbid();

        if (conversationId.HasValue &&
            !await CanAccessAsync(conversationId.Value, userId.Value))
        {
            return NotFound();
        }

        var conversations = await _context.Database
            .SqlQuery<ConversationItem>($"""
                SELECT
                    c.ConversationId,
                    otherUser.UserId AS OtherUserId,
                    otherUser.FullName,
                    lastMessage.Content AS LastMessage,
                    COALESCE(
                        lastMessage.SentAtUtc,
                        c.CreatedAtUtc
                    ) AS LastActivityUtc
                FROM dbo.MentorConversations AS c
                INNER JOIN dbo.Users AS otherUser
                    ON otherUser.UserId =
                        CASE
                            WHEN c.StudentUserId = {userId.Value}
                                THEN c.MentorUserId
                            ELSE c.StudentUserId
                        END
                OUTER APPLY (
                    SELECT TOP (1)
                        m.Content,
                        m.SentAtUtc
                    FROM dbo.MentorMessages AS m
                    WHERE m.ConversationId = c.ConversationId
                    ORDER BY m.MessageId DESC
                ) AS lastMessage
                WHERE c.StudentUserId = {userId.Value}
                   OR c.MentorUserId = {userId.Value}
                """)
            .OrderByDescending(c => c.LastActivityUtc)
            .ThenByDescending(c => c.ConversationId)
            .ToListAsync();

        var model = new ChatPage
        {
            CurrentUserId = userId.Value,
            ConversationId = conversationId
                ?? conversations.FirstOrDefault()?.ConversationId,
            Conversations = conversations
        };

        return View("Index", model);
    }

    // Bấm nút Nhắn tin tư vấn để mở hoặc tạo cuộc trò chuyện.
    [HttpPost("/MentorChat/Start", Name = "StartMentorChat")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start([FromForm] int mentorUserId)
    {
        var userId = await ActiveUserIdAsync();

        if (!userId.HasValue)
            return Forbid();

        if (!ModelState.IsValid ||
            mentorUserId <= 0 ||
            mentorUserId == userId.Value)
        {
            return BadRequest("Người nhận không hợp lệ.");
        }

        await using var transaction = await _context.Database
            .BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

        // Dùng lại cuộc trò chuyện nếu hai người từng nhắn với nhau.
        var conversationId = await _context.Database.SqlQuery<int>($"""
            SELECT c.ConversationId AS [Value]
            FROM dbo.MentorConversations AS c WITH (UPDLOCK, HOLDLOCK)
            WHERE (
                c.StudentUserId = {userId.Value}
                AND c.MentorUserId = {mentorUserId}
            )
            OR (
                c.StudentUserId = {mentorUserId}
                AND c.MentorUserId = {userId.Value}
            )
            """)
            .OrderBy(id => id)
            .FirstOrDefaultAsync();

        if (conversationId == 0)
        {
            // Chỉ tạo mới với cựu sinh viên đang nhận tư vấn.
            var inserted = await _context.Database
                .ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.MentorConversations
                        (StudentUserId, MentorUserId)
                    SELECT {userId.Value}, mentor.UserId
                    FROM dbo.MentorProfiles AS mentor WITH (HOLDLOCK)
                    INNER JOIN dbo.Users AS account WITH (HOLDLOCK)
                        ON account.UserId = mentor.UserId
                    INNER JOIN dbo.AlumniProfiles AS profile WITH (HOLDLOCK)
                        ON profile.UserId = mentor.UserId
                    WHERE mentor.UserId = {mentorUserId}
                      AND mentor.IsAvailable = 1
                      AND account.IsActive = 1
                      AND (
                          profile.IsGraduationVerified = 1
                          OR profile.GraduationConfirmedAtUtc IS NOT NULL
                      )
                      AND EXISTS (
                          SELECT 1
                          FROM dbo.UserRoles AS ur
                          INNER JOIN dbo.Roles AS role
                              ON role.RoleId = ur.RoleId
                          WHERE ur.UserId = mentor.UserId
                            AND role.RoleName = 'Alumni'
                      );
                    """);

            if (inserted == 0)
            {
                return BadRequest(
                    "Cựu sinh viên hiện không nhận tư vấn.");
            }

            conversationId = await _context.Database.SqlQuery<int>($"""
                SELECT ConversationId AS [Value]
                FROM dbo.MentorConversations
                WHERE StudentUserId = {userId.Value}
                  AND MentorUserId = {mentorUserId}
                """).SingleAsync();
        }

        await transaction.CommitAsync();

        return RedirectToRoute(
            "MentorChat",
            new { conversationId });
    }

    // Lấy tin nhắn để giao diện tự cập nhật.
    [HttpGet("/MentorChat/Messages", Name = "MentorChatMessages")]
    public async Task<IActionResult> Messages(
        int conversationId,
        long afterId = 0)
    {
        var userId = await ActiveUserIdAsync();

        if (!userId.HasValue)
            return Forbid();

        if (!ModelState.IsValid || afterId < 0)
            return BadRequest();

        if (!await CanAccessAsync(conversationId, userId.Value))
            return NotFound();

        var messages = await _context.Database
            .SqlQuery<MessageItem>($"""
                SELECT
                    MessageId,
                    SenderUserId,
                    Content,
                    SentAtUtc
                FROM dbo.MentorMessages
                WHERE ConversationId = {conversationId}
                  AND MessageId > {afterId}
                """)
            .OrderBy(m => m.MessageId)
            .Take(100)
            .ToListAsync();

        return Json(messages.Select(m => new
        {
            messageId = m.MessageId,
            content = m.Content,
            isMine = m.SenderUserId == userId.Value,
            sentAtUtc = DateTime.SpecifyKind(
                m.SentAtUtc,
                DateTimeKind.Utc).ToString("O")
        }));
    }

    // Gửi tin nhắn.
    [HttpPost("/MentorChat/Send", Name = "SendMentorChatMessage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(
        [FromForm] int conversationId,
        [FromForm] string? content)
    {
        var userId = await ActiveUserIdAsync();

        if (!userId.HasValue)
            return Forbid();

        content = content?.Trim();

        if (!ModelState.IsValid ||
            string.IsNullOrWhiteSpace(content) ||
            content.Length > 2000)
        {
            return BadRequest(new
            {
                error = "Tin nhắn cần từ 1 đến 2.000 ký tự."
            });
        }

        // Kiểm tra quyền ngay trong câu lệnh lưu.
        // Người gửi luôn lấy từ tài khoản đăng nhập.
        var inserted = await _context.Database
            .ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.MentorMessages
                    (ConversationId, SenderUserId, Content)
                SELECT
                    c.ConversationId,
                    {userId.Value},
                    {content}
                FROM dbo.MentorConversations AS c
                INNER JOIN dbo.Users AS recipient
                    ON recipient.UserId =
                        CASE
                            WHEN c.StudentUserId = {userId.Value}
                                THEN c.MentorUserId
                            ELSE c.StudentUserId
                        END
                WHERE c.ConversationId = {conversationId}
                  AND recipient.IsActive = 1
                  AND (
                      c.StudentUserId = {userId.Value}
                      OR c.MentorUserId = {userId.Value}
                  );
                """);

        if (inserted == 0)
        {
            return BadRequest(new
            {
                error = "Không thể gửi vào cuộc trò chuyện này."
            });
        }

        return Ok(new { success = true });
    }
    // Đếm tin do người khác gửi mà mình chưa đọc.
    [HttpGet("/MentorChat/Unread", Name = "MentorChatUnread")]
    public async Task<IActionResult> Unread()
    {
        var userId = await ActiveUserIdAsync();

        if (!userId.HasValue)
            return Forbid();

        var count = await _context.Database.SqlQuery<int>($"""
        SELECT COUNT(*) AS [Value]
        FROM dbo.MentorMessages AS m
        INNER JOIN dbo.MentorConversations AS c
            ON c.ConversationId = m.ConversationId
        WHERE (
            c.StudentUserId = {userId.Value}
            OR c.MentorUserId = {userId.Value}
        )
          AND m.SenderUserId <> {userId.Value}
          AND m.ReadAtUtc IS NULL
        """).SingleAsync();

        return Json(new { count });
    }

    // Chỉ đánh dấu các tin đã tải và hiển thị trong cuộc trò chuyện.
    [HttpPost("/MentorChat/Read", Name = "ReadMentorChat")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(
        [FromForm] int conversationId,
        [FromForm] long throughMessageId)
    {
        var userId = await ActiveUserIdAsync();

        if (!userId.HasValue)
            return Forbid();

        if (!ModelState.IsValid || throughMessageId <= 0)
            return BadRequest();

        if (!await CanAccessAsync(conversationId, userId.Value))
            return NotFound();

        await _context.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE m
        SET ReadAtUtc = SYSUTCDATETIME()
        FROM dbo.MentorMessages AS m
        INNER JOIN dbo.MentorConversations AS c
            ON c.ConversationId = m.ConversationId
        WHERE c.ConversationId = {conversationId}
          AND (
              c.StudentUserId = {userId.Value}
              OR c.MentorUserId = {userId.Value}
          )
          AND m.MessageId <= {throughMessageId}
          AND m.SenderUserId <> {userId.Value}
          AND m.ReadAtUtc IS NULL;
        """);

        return Ok(new { success = true });
    }
}