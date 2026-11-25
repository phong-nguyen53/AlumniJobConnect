using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Controllers;

public partial class AiChatController
{
    public sealed class SavedConversation
    {
        public Guid ConversationId { get; set; }
        public string Title { get; set; } = "";
        public DateTime UpdatedAtUtc { get; set; }
        public int Version { get; set; }
    }

    public sealed class SavedMessage
    {
        public long MessageId { get; set; }
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        public string SourcesJson { get; set; } = "[]";
        public DateTime CreatedAtUtc { get; set; }
    }

    private sealed class HistoryConflictException : Exception { }

    private async Task<int?> ActiveUserIdAsync(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        return await _context.Users.AsNoTracking()
            .AnyAsync(u => u.UserId == id && u.IsActive, ct) ? id : null;
    }

    private async Task<SavedConversation?> FindConversationAsync(Guid id, int userId, CancellationToken ct)
    {
        var rows = await _context.Database.SqlQuery<SavedConversation>($"""
            SELECT ConversationId, Title, UpdatedAtUtc, Version
            FROM dbo.AiChatConversations
            WHERE ConversationId = {id} AND UserId = {userId}
            """).ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    // Danh sách phân trang, mỗi người chỉ xem lịch sử của chính mình.
    [HttpGet]
    public async Task<IActionResult> History(int page = 1, CancellationToken ct = default)
    {
        var userId = await ActiveUserIdAsync(ct);
        if (!userId.HasValue) return StatusCode(403, new { error = "Tài khoản không hợp lệ hoặc đã bị khóa." });
        page = Math.Clamp(page, 1, 10000);
        var offset = (page - 1) * 30;
        var rows = await _context.Database.SqlQuery<SavedConversation>($"""
            SELECT ConversationId, Title, UpdatedAtUtc, Version
            FROM dbo.AiChatConversations WHERE UserId = {userId.Value}
            ORDER BY UpdatedAtUtc DESC, ConversationId DESC
            OFFSET {offset} ROWS FETCH NEXT 31 ROWS ONLY
            """).ToListAsync(ct);
        return Json(new { items = rows.Take(30).Select(c => new {
            c.ConversationId, c.Title,
            updatedAtUtc = DateTime.SpecifyKind(c.UpdatedAtUtc, DateTimeKind.Utc)
        }), hasMore = rows.Count > 30 });
    }

    [HttpGet]
    public async Task<IActionResult> Messages(Guid id, long? beforeId, CancellationToken ct)
    {
        var userId = await ActiveUserIdAsync(ct);
        if (!userId.HasValue) return StatusCode(403, new { error = "Tài khoản không hợp lệ hoặc đã bị khóa." });
        var conversation = await FindConversationAsync(id, userId.Value, ct);
        if (conversation == null) return NotFound(new { error = "Không tìm thấy cuộc trò chuyện." });
        var before = beforeId ?? long.MaxValue;
        var rows = await _context.Database.SqlQuery<SavedMessage>($"""
            SELECT TOP (101) m.MessageId, m.Role, m.Content, m.SourcesJson, m.CreatedAtUtc
            FROM dbo.AiChatMessages m
            JOIN dbo.AiChatConversations c ON c.ConversationId = m.ConversationId
            WHERE c.ConversationId = {id} AND c.UserId = {userId.Value}
                AND m.MessageId < {before}
            ORDER BY m.MessageId DESC
            """).ToListAsync(ct);
        var batch = rows.Take(100).Reverse().ToList();
        return Json(new { conversation.ConversationId, conversation.Title,
            hasMore = rows.Count > 100,
            items = batch.Select(m => new {
                m.MessageId, m.Role, m.Content,
                sources = JsonSerializer.Deserialize<List<Source>>(m.SourcesJson) ?? new(),
                createdAtUtc = DateTime.SpecifyKind(m.CreatedAtUtc, DateTimeKind.Utc)
            }) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = await ActiveUserIdAsync(ct);
        if (!userId.HasValue) return StatusCode(403, new { error = "Tài khoản không hợp lệ hoặc đã bị khóa." });
        var changed = await _context.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM dbo.AiChatConversations
            WHERE ConversationId = {id} AND UserId = {userId.Value}
            """, ct);
        if (changed == 0) return NotFound(new { error = "Cuộc trò chuyện không còn tồn tại." });
        return Json(new { success = true });
    }

    private async Task<SavedConversation> SaveExchangeAsync(int userId,
        SavedConversation? conversation, string question, string reply,
        List<Source> sources, CancellationToken ct)
    {
        // Câu hỏi và câu trả lời được lưu cùng một giao dịch.
        // Version chặn hai cửa sổ cùng ghi đè ngữ cảnh cũ.
        var id = conversation?.ConversationId ?? Guid.NewGuid();
        var title = conversation?.Title ?? question.Replace('\r', ' ').Replace('\n', ' ');
        if (title.Length > 100) title = title[..100];
        var now = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(sources);
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        if (conversation == null)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.AiChatConversations
                    (ConversationId, UserId, Title, CreatedAtUtc, UpdatedAtUtc, Version)
                VALUES ({id}, {userId}, {title}, {now}, {now}, 1)
                """, ct);
        }
        else
        {
            var changed = await _context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.AiChatConversations
                SET UpdatedAtUtc = {now}, Version = Version + 1
                WHERE ConversationId = {id} AND UserId = {userId}
                    AND Version = {conversation.Version}
                """, ct);
            if (changed != 1) throw new HistoryConflictException();
        }
        await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.AiChatMessages (ConversationId, Role, Content, SourcesJson, CreatedAtUtc)
            VALUES ({id}, N'user', {question}, N'[]', {now});
            INSERT INTO dbo.AiChatMessages (ConversationId, Role, Content, SourcesJson, CreatedAtUtc)
            VALUES ({id}, N'assistant', {reply}, {json}, {now});
            """, ct);
        await transaction.CommitAsync(ct);
        return new SavedConversation { ConversationId = id, Title = title, UpdatedAtUtc = now };
    }
}
