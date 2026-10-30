using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace AlumniJobConnect.Controllers;

public class AccountController : Controller
{
    private readonly AlumniJobConnectContext _context;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        AlumniJobConnectContext context,
        ILogger<AccountController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ================= ĐĂNG KÝ =================

    public class RegisterInput
    {
        [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
        public string FullName { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập email.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập lại mật khẩu.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng chọn loại tài khoản.")]
        public string AccountType { get; set; } = "Alumni";

        public string? CompanyName { get; set; }
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View(new RegisterInput());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        input.FullName = (input.FullName ?? "").Trim();
        input.Email = (input.Email ?? "").Trim().ToLowerInvariant();
        input.CompanyName = input.CompanyName?.Trim();

        // Chỉ được đăng ký sinh viên hoặc doanh nghiệp.
        if (input.AccountType != "Alumni"
            && input.AccountType != "Employer")
        {
            ModelState.AddModelError(
                nameof(input.AccountType),
                "Loại tài khoản không hợp lệ.");
        }

        if (input.AccountType == "Employer"
            && string.IsNullOrWhiteSpace(input.CompanyName))
        {
            ModelState.AddModelError(
                nameof(input.CompanyName),
                "Vui lòng nhập tên doanh nghiệp.");
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            var now = DateTime.Now;

            var role = await _context.Roles
                .SingleOrDefaultAsync(
                    x => x.RoleName == input.AccountType);

            if (role == null)
            {
                role = new Role
                {
                    RoleName = input.AccountType,
                    Description = input.AccountType == "Employer"
                        ? "Doanh nghiệp"
                        : "Sinh viên/Cựu sinh viên"
                };

                _context.Roles.Add(role);
            }

            var user = new AlumniJobConnect.Models.User
            {
                FullName = input.FullName,
                Email = input.Email,
                IsActive = true,
                CreatedAt = now
            };

            // Lưu mật khẩu anh nhập dưới dạng mã hóa.
            var hasher =
                new PasswordHasher<AlumniJobConnect.Models.User>();

            user.PasswordHash =
                hasher.HashPassword(user, input.Password);

            user.UserRoles.Add(new UserRole
            {
                User = user,
                Role = role,
                AssignedAt = now
            });

            if (input.AccountType == "Alumni")
            {
                user.AlumniProfile = new AlumniProfile
                {
                    User = user,
                    ProfileUpdatedAt = now
                };
            }

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            if (input.AccountType == "Employer")
            {
                await _context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.Companies
                    (
                        UserId,
                        CompanyName,
                        VerificationStatus,
                        CreatedAt
                    )
                    VALUES
                    (
                        {user.UserId},
                        {input.CompanyName},
                        {"Pending"},
                        {now}
                    )
                    """);
            }

            await transaction.CommitAsync();

            TempData["RegisterSuccess"] =
                "Đăng ký thành công! Vui lòng đăng nhập.";

            return RedirectToAction(nameof(Login));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();

            _logger.LogError(ex, "Không thể đăng ký tài khoản.");

            // Chỉ hiện chi tiết lỗi khi chạy trên máy phát triển.
            var environment = HttpContext.RequestServices
                .GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>();

            var isDevelopment =
                environment.EnvironmentName == "Development";

            ModelState.AddModelError(
                "",
                isDevelopment
                    ? ex.GetBaseException().Message
                    : "Chưa lưu được tài khoản. Vui lòng kiểm tra nhật ký lỗi.");

            return View(input);
        }
    }

    // ================= ĐĂNG NHẬP =================

    public class LoginInput
    {
        [Required(ErrorMessage = "Vui lòng nhập email.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }

    [HttpGet]
    public IActionResult Login()
    {
        return View(new LoginInput());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInput input)
    {
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var email = input.Email.Trim().ToLowerInvariant();

        // Email được phép trùng nên kiểm tra từng tài khoản.
        var users = await _context.Users
            .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
            .Where(x => x.Email == email && x.IsActive)
            .ToListAsync();

        var hasher =
            new PasswordHasher<AlumniJobConnect.Models.User>();

        var matches =
            new List<AlumniJobConnect.Models.User>();

        foreach (var candidate in users)
        {
            if (string.IsNullOrWhiteSpace(candidate.PasswordHash))
            {
                continue;
            }

            try
            {
                var result = hasher.VerifyHashedPassword(
                    candidate,
                    candidate.PasswordHash,
                    input.Password);

                if (result != PasswordVerificationResult.Failed)
                {
                    matches.Add(candidate);
                }
            }
            catch (FormatException)
            {
                // Bỏ qua dữ liệu mật khẩu không hợp lệ.
            }
        }

        if (matches.Count == 0)
        {
            ModelState.AddModelError(
                "",
                "Email hoặc mật khẩu không đúng, hoặc tài khoản bị khóa.");

            return View(input);
        }

        if (matches.Count > 1)
        {
            ModelState.AddModelError(
                "",
                "Có nhiều tài khoản cùng email và mật khẩu. Không thể xác định tài khoản đăng nhập.");

            return View(input);
        }

        var user = matches[0];

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email)
        };

        foreach (var roleName in user.UserRoles
            .Select(x => x.Role.RoleName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct())
        {
            claims.Add(new Claim(ClaimTypes.Role, roleName));
        }

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = false
            });

        return RedirectToAction("Index", "Home");
    }

    // ================= ĐĂNG XUẤT =================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction("Index", "Home");
    }
}