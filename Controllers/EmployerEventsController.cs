using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.Tasks;
using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Controllers;

[Authorize(Roles = "Employer")]
[Route("EmployerEvents")]
public class EmployerEventsController : Controller
{
    private readonly AlumniJobConnectContext _context;

    public EmployerEventsController(AlumniJobConnectContext context)
    {
        _context = context;
    }

    public sealed class CreateEventInput
    {
        [Required(ErrorMessage = "Vui lòng nhập tên sự kiện.")]
        [StringLength(200, ErrorMessage = "Tên sự kiện tối đa 200 ký tự.")]
        public string? EventName { get; set; }

        public string? Description { get; set; }

        [StringLength(300, ErrorMessage = "Địa điểm tối đa 300 ký tự.")]
        public string? Location { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn thời gian bắt đầu.")]
        public DateTime? StartAt { get; set; }

        public DateTime? EndAt { get; set; }

        public DateTime? RegistrationDeadline { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số chỗ phải lớn hơn 0.")]
        public int? Capacity { get; set; }
    }

    [HttpGet("Create")]
    public IActionResult Create()
    {
        return View(new CreateEventInput());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateEventInput input)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Forbid();
        }

        if (!await _context.Companies.AnyAsync(company => company.UserId == userId))
        {
            ModelState.AddModelError("", "Tài khoản cần có hồ sơ doanh nghiệp để đăng sự kiện.");
        }

        if (input.EndAt is DateTime end && input.StartAt is DateTime start && end < start)
        {
            ModelState.AddModelError(nameof(input.EndAt),
                "Thời gian kết thúc phải bằng hoặc sau thời gian bắt đầu.");
        }

        if (input.RegistrationDeadline is DateTime deadline &&
            input.StartAt is DateTime startsAt && deadline > startsAt)
        {
            ModelState.AddModelError(nameof(input.RegistrationDeadline),
                "Hạn đăng ký phải bằng hoặc trước thời gian bắt đầu.");
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.Events
            (
                CreatedByUserId,
                EventName,
                Description,
                Location,
                StartAt,
                EndAt,
                RegistrationDeadline,
                Capacity
            )
            VALUES
            (
                {userId},
                {input.EventName!.Trim()},
                {input.Description?.Trim()},
                {input.Location?.Trim()},
                {input.StartAt!.Value},
                {input.EndAt},
                {input.RegistrationDeadline},
                {input.Capacity}
            )
            """);

        TempData["EventSuccess"] = "Đã đăng sự kiện thành công.";
        return RedirectToAction(nameof(Create));
    }
}