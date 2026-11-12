using System.Linq;
using System.Threading.Tasks;
using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;

namespace AlumniJobConnect.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class UsersController : Controller
{
    private readonly AlumniJobConnectContext _context;

    public UsersController(AlumniJobConnectContext context)
    {
        _context = context;
    }

    // GET /Admin/Users
    // GET /Admin/Users/Index
    [HttpGet("/Admin/Users", Name = "AdminUsers")]
    [HttpGet("/Admin/Users/Index")]
    public async Task<IActionResult> Index([FromQuery] string? role)
    {
        role = role?.Trim();

        IQueryable<User> query = _context.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role);

        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(user =>
                user.UserRoles.Any(userRole => userRole.Role.RoleName == role));
        }

        var users = await query
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.UserId)
            .ToListAsync();

        ViewData["SelectedRole"] = role;
        return View(users);
    }
    public sealed class CreateUserInput
    {
        [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
        public string FullName { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập email.")]
        [EmailAddress(ErrorMessage = "Email chưa đúng định dạng.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        [StringLength(128, MinimumLength = 8,
            ErrorMessage = "Mật khẩu phải có từ 8 đến 128 ký tự.")]
        public string Password { get; set; } = "";

        [Compare(nameof(Password), ErrorMessage = "Hai mật khẩu không khớp.")]
        public string ConfirmPassword { get; set; } = "";

        public string? Phone { get; set; }

        public string RoleName { get; set; } = "Alumni";

        public string? CompanyName { get; set; }
    }

    [HttpGet("/Admin/Users/Create", Name = "AdminCreateUser")]
    public IActionResult Create()
    {
        return View(new CreateUserInput());
    }

    [HttpPost("/Admin/Users/Create", Name = "AdminCreateUserSubmit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserInput input)
    {
        input.FullName = input.FullName.Trim();
        input.Email = input.Email.Trim().ToLowerInvariant();
        input.CompanyName = input.CompanyName?.Trim();

        if (input.RoleName is not ("Admin" or "Alumni" or "Employer"))
        {
            ModelState.AddModelError(nameof(input.RoleName), "Vai trò không hợp lệ.");
        }

        if (input.RoleName == "Employer" && string.IsNullOrWhiteSpace(input.CompanyName))
        {
            ModelState.AddModelError(nameof(input.CompanyName),
                "Vui lòng nhập tên doanh nghiệp.");
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        try
        {
            await using var transaction = await _context.Database
                .BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            if (await _context.Users.AnyAsync(user => user.Email.ToLower() == input.Email))
            {
                ModelState.AddModelError(nameof(input.Email), "Email này đã có tài khoản.");
                return View(input);
            }

            var role = await _context.Roles
                .SingleOrDefaultAsync(item => item.RoleName == input.RoleName);

            if (role is null)
            {
                ModelState.AddModelError(nameof(input.RoleName),
                    "Vai trò này chưa có trong cơ sở dữ liệu.");
                return View(input);
            }

            var now = DateTime.Now;

            var user = new User
            {
                FullName = input.FullName,
                Email = input.Email,
                Phone = input.Phone?.Trim(),
                IsActive = true,
                CreatedAt = now
            };

            user.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<User>()
                .HashPassword(user, input.Password);

            user.UserRoles.Add(new UserRole
            {
                User = user,
                Role = role,
                AssignedAt = now
            });

            if (input.RoleName == "Alumni")
            {
                user.AlumniProfile = new AlumniProfile
                {
                    User = user,
                    ProfileUpdatedAt = now
                };
            }

            _context.Users.Add(user);

            if (input.RoleName == "Employer")
            {
                _context.Companies.Add(new Company
                {
                    User = user,
                    CompanyName = input.CompanyName!,
                    VerificationStatus = "Pending",
                    CreatedAt = now
                });
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("",
                "Chưa lưu được tài khoản. Vui lòng kiểm tra dữ liệu và thử lại.");
            return View(input);
        }

        return RedirectToRoute("AdminUsers");
    }
    [HttpGet("/Admin/Users/{id:int}/Edit", Name = "AdminEditUser")]
    public async Task<IActionResult> Edit([FromRoute] int id)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == id);

        if (user is null)
        {
            return NotFound();
        }

        return View(user);
    }

    [HttpPost("/Admin/Users/{id:int}/Edit", Name = "AdminEditUserSubmit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        [FromRoute] int id,
        [FromForm] string? fullName,
        [FromForm] string? phone)
    {
        var user = await _context.Users.FindAsync(id);

        if (user is null)
        {
            return NotFound();
        }

        user.FullName = fullName?.Trim() ?? "";
        user.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

        if (user.FullName.Length == 0)
        {
            ModelState.AddModelError(nameof(fullName), "Vui lòng nhập họ tên.");
        }

        if (!ModelState.IsValid)
        {
            return View(user);
        }

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("", "Chưa lưu được thay đổi. Vui lòng kiểm tra dữ liệu.");
            return View(user);
        }

        TempData["UserSuccess"] = "Đã cập nhật tài khoản.";
        return RedirectToRoute("AdminUsers");
    }
    [HttpPost("/Admin/Users/{id:int}/GrantAdmin", Name = "AdminGrantUserRole")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GrantAdmin(
    [FromRoute] int id,
    [FromForm] string? currentRole)
    {
        try
        {
            await using var transaction = await _context.Database
                .BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            var user = await _context.Users
                .Include(item => item.UserRoles)
                .FirstOrDefaultAsync(item => item.UserId == id);

            var adminRole = await _context.Roles
                .SingleOrDefaultAsync(role => role.RoleName == "Admin");

            if (user is null || !user.IsActive)
            {
                TempData["UserError"] = "Tài khoản không tồn tại hoặc đang bị tắt.";
            }
            else if (adminRole is null)
            {
                TempData["UserError"] = "Chưa có vai trò Admin trong cơ sở dữ liệu.";
            }
            else if (user.UserRoles.Any(link => link.RoleId == adminRole.RoleId))
            {
                TempData["UserSuccess"] = "Tài khoản này đã có quyền quản trị.";
            }
            else
            {
                user.UserRoles.Add(new UserRole
                {
                    User = user,
                    Role = adminRole,
                    AssignedAt = DateTime.Now
                });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["UserSuccess"] =
                    $"Đã cấp quyền quản trị cho {user.Email}. Hãy đăng nhập lại để nhận quyền mới.";
            }
        }
        catch (DbUpdateException)
        {
            TempData["UserError"] = "Chưa cấp được quyền. Vui lòng thử lại.";
        }

        return RedirectToRoute("AdminUsers", new { role = currentRole });
    }
    [HttpPost("/Admin/Users/{id:int}/RevokeAdmin", Name = "AdminRevokeUserRole")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeAdmin([FromRoute] int id)
    {
        var currentUserId = User.FindFirst(
            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (!int.TryParse(currentUserId, out var adminId) || adminId == id)
        {
            TempData["UserError"] = "Bạn không thể gỡ quyền quản trị của chính mình.";
            return RedirectToRoute("AdminUsers");
        }

        try
        {
            await using var transaction = await _context.Database
                .BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            var canManageUsers = await _context.Users.AnyAsync(user =>
                user.UserId == adminId &&
                user.IsActive &&
                user.UserRoles.Any(link => link.Role.RoleName == "Admin"));

            if (!canManageUsers)
            {
                return Forbid();
            }

            var target = await _context.Users
                .Include(user => user.UserRoles)
                .ThenInclude(link => link.Role)
                .FirstOrDefaultAsync(user => user.UserId == id);

            var adminLink = target?.UserRoles
                .FirstOrDefault(link => link.Role.RoleName == "Admin");

            if (target is null || adminLink is null)
            {
                TempData["UserError"] = "Tài khoản không tồn tại hoặc đã được gỡ quyền quản trị.";
                return RedirectToRoute("AdminUsers");
            }

            if (!target.UserRoles.Any(link => link.Role.RoleName != "Admin"))
            {
                TempData["UserError"] = "Tài khoản này chỉ có vai trò Quản trị viên.";
                return RedirectToRoute("AdminUsers");
            }

            _context.Remove(adminLink);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["UserSuccess"] = "Đã gỡ quyền quản trị và giữ các vai trò còn lại.";
        }
        catch (DbUpdateException)
        {
            TempData["UserError"] = "Chưa gỡ được quyền quản trị. Vui lòng thử lại.";
        }

        return RedirectToRoute("AdminUsers");
    }
}