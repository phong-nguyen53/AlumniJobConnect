using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
    public class RegisterInput
    {
        [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
        [StringLength(150)]
        public string FullName { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập email.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        [StringLength(256)]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        [StringLength(
            128,
            MinimumLength = 10,
            ErrorMessage = "Mật khẩu phải từ 10 đến 128 ký tự.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập lại mật khẩu.")]
        [Compare(
            nameof(Password),
            ErrorMessage = "Hai mật khẩu chưa khớp.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = "";

        [Required]
        [RegularExpression(
            "^(Alumni|Employer)$",
            ErrorMessage = "Loại tài khoản không hợp lệ.")]
        public string AccountType { get; set; } = "Alumni";

        [StringLength(200)]
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

        if (string.IsNullOrWhiteSpace(input.FullName))
        {
            ModelState.AddModelError(
                nameof(input.FullName), "Vui lòng nhập họ tên.");
        }

        if (input.AccountType == "Employer"
            && string.IsNullOrWhiteSpace(input.CompanyName))
        {
            ModelState.AddModelError(
                nameof(input.CompanyName),
                "Vui lòng nhập tên doanh nghiệp.");
        }

        if (!ModelState.IsValid)
            return View(input);

        var emailExists = await _context.Users
            .AnyAsync(x => x.Email == input.Email);

        if (emailExists)
        {
            ModelState.AddModelError(
                nameof(input.Email), "Email này đã được đăng ký.");

            return View(input);
        }

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
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
                CreatedAt = DateTime.Now
            };

            var hasher =
                new PasswordHasher<AlumniJobConnect.Models.User>();

            user.PasswordHash =
                hasher.HashPassword(user, input.Password);

            user.UserRoles.Add(new UserRole
            {
                User = user,
                Role = role,
                AssignedAt = DateTime.Now
            });

            if (input.AccountType == "Alumni")
            {
                user.AlumniProfile = new AlumniProfile
                {
                    User = user,
                    ProfileUpdatedAt = DateTime.Now
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
                        {DateTime.Now}
                    )
                    """);
            }
            await transaction.CommitAsync();

            TempData["RegisterSuccess"] =
                "Đăng ký thành công! Vui lòng đăng nhập.";

            return RedirectToAction("Login", "Account");
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqlException sql
                && (sql.Number == 2601 || sql.Number == 2627))
        {
            await transaction.RollbackAsync();

            ModelState.AddModelError(
                "", "Thông tin đăng ký bị trùng. Vui lòng kiểm tra email.");

            return View(input);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();

            _logger.LogError(ex, "Không thể tạo tài khoản đăng ký.");

            ModelState.AddModelError(
                "", "Chưa thể đăng ký. Vui lòng thử lại.");

            return View(input);
        }
    }


    /// đăng nhập 
    public class LoginInput
    {
        [Required(ErrorMessage = "Vui lòng nhập email.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        [StringLength(256)]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
        [StringLength(128)]
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
            return View(input);

        var email = input.Email.Trim().ToLowerInvariant();

        var user = await _context.Users
            .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Email == email);

        if (user == null || !user.IsActive)
        {
            ModelState.AddModelError(
                "", "Email hoặc mật khẩu không đúng, hoặc tài khoản bị khóa.");

            return View(input);
        }

        var hasher =
            new PasswordHasher<AlumniJobConnect.Models.User>();

        PasswordVerificationResult result;

        try
        {
            result = hasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                input.Password);
        }
        catch (FormatException)
        {
            result = PasswordVerificationResult.Failed;
        }

        if (result == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(
                "", "Email hoặc mật khẩu không đúng, hoặc tài khoản bị khóa.");

            return View(input);
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash =
                hasher.HashPassword(user, input.Password);

            await _context.SaveChangesAsync();
        }

        var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
        new(ClaimTypes.Name, user.FullName),
        new(ClaimTypes.Email, user.Email)
    };

        foreach (var roleName in user.UserRoles
            .Select(x => x.Role.RoleName)
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

        // Đăng nhập thành công → trang chủ
        return RedirectToAction("Index", "Home");
    }


}