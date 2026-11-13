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

[Authorize(Roles = "Alumni,Employer")]
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

        public int? JobPostId { get; set; }
        public string? JobTitle { get; set; }
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
            .AnyAsync(user =>
                user.UserId == id.Value &&
                user.IsActive &&
                user.UserRoles.Any(link =>
                    link.Role.RoleName == "Alumni" ||
                    link.Role.RoleName == "Employer"));

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
                    CASE
                    WHEN company.UserId = otherUser.UserId
                        THEN company.CompanyName
                    ELSE otherUser.FullName
                END AS FullName,
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
                 LEFT JOIN dbo.Companies AS company
                ON company.CompanyId = c.CompanyId
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
    [Authorize(Roles = "Alumni")]
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
                message.MessageId,
                message.SenderUserId,
                message.Content,
                message.SentAtUtc,
                message.JobPostId,
                job.Title AS JobTitle
            FROM dbo.MentorMessages AS message
            LEFT JOIN dbo.JobPosts AS job
                ON job.JobPostId = message.JobPostId
            WHERE message.ConversationId = {conversationId}
              AND message.MessageId > {afterId}
            """)
            .OrderBy(message => message.MessageId)
            .Take(100)
            .ToListAsync();

        return Json(messages.Select(message => new
        {
            messageId = message.MessageId,
            content = message.Content,
            isMine = message.SenderUserId == userId.Value,

            sentAtUtc = DateTime.SpecifyKind(
                message.SentAtUtc,
                DateTimeKind.Utc).ToString("O"),

            jobPostId = message.JobPostId,
            jobTitle = message.JobTitle,

            jobUrl = message.JobPostId.HasValue
                ? Url.Action(
                    "Details",
                    "Jobs",
                    new { id = message.JobPostId.Value })
                : null
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

    [HttpPost(
    "/MentorChat/StartCompany",
    Name = "StartCompanyChat")]
    [Authorize(Roles = "Alumni")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartCompany(
    [FromForm] int companyId,
    [FromForm] int? jobPostId)
    {
        var userId = await ActiveUserIdAsync();

        if (!userId.HasValue)
            return Forbid();

        if (!ModelState.IsValid || companyId <= 0)
            return BadRequest("Doanh nghiệp không hợp lệ.");

        try
        {
            await using var transaction = await _context.Database
                .BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable);

            // Người nhận lấy từ tài khoản sở hữu công ty.
            // Không nhận mã tài khoản người nhận từ form.
            var employerUserId = await _context.Database.SqlQuery<int>($"""
            SELECT company.UserId AS [Value]
            FROM dbo.Companies AS company WITH (HOLDLOCK)
            INNER JOIN dbo.Users AS account WITH (HOLDLOCK)
                ON account.UserId = company.UserId
            WHERE company.CompanyId = {companyId}
              AND account.IsActive = 1
              AND EXISTS (
                  SELECT 1
                  FROM dbo.UserRoles AS userRole
                  INNER JOIN dbo.Roles AS role
                      ON role.RoleId = userRole.RoleId
                  WHERE userRole.UserId = account.UserId
                    AND role.RoleName = 'Employer'
              )
            """).SingleOrDefaultAsync();

            if (employerUserId == 0)
            {
                return NotFound(
                    "Doanh nghiệp không tồn tại hoặc tài khoản hiện không nhận tin.");
            }

            if (employerUserId == userId.Value)
            {
                return BadRequest(
                    "Bạn không thể nhắn tin cho chính tài khoản của mình.");
            }

            // Tin tuyển dụng phải thuộc đúng doanh nghiệp được chọn.
            string? selectedJobTitle = null;

            if (jobPostId.HasValue)
            {
                selectedJobTitle = await _context.Database.SqlQuery<string>($"""
        SELECT job.Title AS [Value]
        FROM dbo.JobPosts AS job WITH (HOLDLOCK)
        WHERE job.JobPostId = {jobPostId.Value}
          AND job.CompanyId = {companyId}
          AND job.ApprovalStatus = 'Approved'
          AND job.IsPublished = 1
        """).SingleOrDefaultAsync();

                if (string.IsNullOrWhiteSpace(selectedJobTitle))
                {
                    return BadRequest(
                        "Tin tuyển dụng không thuộc doanh nghiệp này hoặc không còn được công khai.");
                }
            }

            // Dùng lại hộp chat nếu hai tài khoản đã từng trò chuyện.
            var conversationId = await _context.Database.SqlQuery<int>($"""
            SELECT ConversationId AS [Value]
            FROM dbo.MentorConversations WITH (UPDLOCK, HOLDLOCK)
            WHERE (
                StudentUserId = {userId.Value}
                AND MentorUserId = {employerUserId}
            )
            OR (
                StudentUserId = {employerUserId}
                AND MentorUserId = {userId.Value}
            )
            """)
                .OrderBy(id => id)
                .FirstOrDefaultAsync();

            if (conversationId == 0)
            {
                await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.MentorConversations
                (
                    StudentUserId,
                    MentorUserId,
                    CompanyId,
                    CreatedAtUtc
                )
                VALUES
                (
                    {userId.Value},
                    {employerUserId},
                    {companyId},
                    SYSUTCDATETIME()
                );
                """);

                conversationId = await _context.Database.SqlQuery<int>($"""
                SELECT ConversationId AS [Value]
                FROM dbo.MentorConversations
                WHERE StudentUserId = {userId.Value}
                  AND MentorUserId = {employerUserId}
                """).SingleAsync();
            }
            else
            {
                // Gắn doanh nghiệp khi cuộc trò chuyện chưa có liên kết.
                await _context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.MentorConversations
                SET CompanyId = {companyId}
                WHERE ConversationId = {conversationId}
                  AND CompanyId IS NULL;
                """);
            }

            if (jobPostId.HasValue && selectedJobTitle != null)
            {
                // Chỉ thêm lời dẫn khi chuyển sang hỏi một tin khác.
                var previousJobPostId = await _context.Database
                    .SqlQuery<int?>($"""
            SELECT TOP (1) message.JobPostId AS [Value]
            FROM dbo.MentorMessages AS message WITH (UPDLOCK, HOLDLOCK)
            WHERE message.ConversationId = {conversationId}
              AND message.JobPostId IS NOT NULL
            ORDER BY message.MessageId DESC
            """)
                    .SingleOrDefaultAsync();

                if (previousJobPostId != jobPostId)
                {
                    var introduction = "Trao đổi về tin tuyển dụng: "
                        + selectedJobTitle;

                    // Giữ nội dung trong giới hạn cột Content.
                    if (introduction.Length > 2000)
                        introduction = introduction.Substring(0, 2000);

                    await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.MentorMessages
            (
                ConversationId,
                SenderUserId,
                Content,
                JobPostId,
                SentAtUtc
            )
            VALUES
            (
                {conversationId},
                {userId.Value},
                {introduction},
                {jobPostId.Value},
                SYSUTCDATETIME()
            );
            """);
                }
            }


            await transaction.CommitAsync();

            return RedirectToRoute(
                "MentorChat",
                new { conversationId });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
            when (ex.Number == 1205 ||
                  ex.Number == 2601 ||
                  ex.Number == 2627)
        {
            return StatusCode(
                409,
                "Có thao tác đồng thời. Vui lòng quay lại và bấm Nhắn tin doanh nghiệp lần nữa.");
        }
    }


}