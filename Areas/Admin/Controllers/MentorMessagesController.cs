using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AlumniJobConnect.Models;

namespace AlumniJobConnect.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
    public class MentorMessagesController : Controller
    {
        private readonly AlumniJobConnectContext _context;

        public MentorMessagesController(AlumniJobConnectContext context)
        {
            _context = context;
        }

        // GET: Admin/MentorMessages
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var messages = await _context.MentorMessages
                .AsNoTracking()
                .OrderByDescending(x => x.SentAtUtc)
                .ThenByDescending(x => x.MessageId)
                .ToListAsync();

            var senderIds = messages
                .Select(x => x.SenderUserId)
                .Distinct()
                .ToList();

            ViewData["SenderNames"] = await _context.Users
                .AsNoTracking()
                .Where(x => senderIds.Contains(x.UserId))
                .ToDictionaryAsync(x => x.UserId, x => x.FullName);

            var jobIds = messages
                .Where(x => x.JobPostId.HasValue)
                .Select(x => x.JobPostId!.Value)
                .Distinct()
                .ToList();

            ViewData["JobTitles"] = await _context.JobPosts
                .AsNoTracking()
                .Where(x => jobIds.Contains(x.JobPostId))
                .ToDictionaryAsync(x => x.JobPostId, x => x.Title);

            return View(messages);
        }

        // GET: Admin/MentorMessages/Details/5
        [HttpGet]
        public async Task<IActionResult> Details(long? id)
        {
            if (!id.HasValue)
            {
                return NotFound();
            }

            var message = await _context.MentorMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.MessageId == id.Value);

            if (message == null)
            {
                return NotFound();
            }

            var sender = await _context.Users
                .AsNoTracking()
                .Where(x => x.UserId == message.SenderUserId)
                .Select(x => new
                {
                    x.FullName,
                    x.Email
                })
                .FirstOrDefaultAsync();

            ViewData["SenderName"] = sender?.FullName
                ?? $"Tài khoản #{message.SenderUserId}";

            ViewData["SenderEmail"] = sender?.Email;

            if (message.JobPostId.HasValue)
            {
                ViewData["JobTitle"] = await _context.JobPosts
                    .AsNoTracking()
                    .Where(x => x.JobPostId == message.JobPostId.Value)
                    .Select(x => x.Title)
                    .FirstOrDefaultAsync();
            }

            // Admin xem không thay đổi ReadAtUtc.
            return View("Details", message);
        }

    }
}
