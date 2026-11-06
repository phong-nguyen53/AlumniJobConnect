using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AlumniJobConnect.Controllers;

[Authorize]
public class NotificationsController : Controller
{
    private readonly AlumniJobConnectContext _context;

    public NotificationsController(
        AlumniJobConnectContext context)
    {
        _context = context;
    }

    // Bấm thông báo: đánh dấu đã đọc và mở trang liên quan.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Open(int id)
    {
        var userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdValue, out var userId))
        {
            return Forbid();
        }

        var isActive = await _context.Users
            .AsNoTracking()
            .AnyAsync(x =>
                x.UserId == userId &&
                x.IsActive);

        if (!isActive)
        {
            return Forbid();
        }

        // Chỉ được mở thông báo của tài khoản đang đăng nhập.
        var notification = await _context
            .Set<Notification>()
            .SingleOrDefaultAsync(x =>
                x.NotificationId == id &&
                x.UserId == userId);

        if (notification == null)
        {
            return NotFound("Không tìm thấy thông báo.");
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;

            await _context.SaveChangesAsync();
        }

        // Chỉ chuyển đến đường dẫn bên trong website.
        var destination = notification.RelatedUrl;

        if (!string.IsNullOrWhiteSpace(destination) &&
            Url.IsLocalUrl(destination))
        {
            return LocalRedirect(destination);
        }

        return RedirectToAction(
            "MyApplications",
            "Applications");
    }
}