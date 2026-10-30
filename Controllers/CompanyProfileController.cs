using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace AlumniJobConnect.Controllers;

[Authorize]
public class CompanyProfileController : Controller
{
    private readonly AlumniJobConnectContext _context;

    public CompanyProfileController(
        AlumniJobConnectContext context)
    {
        _context = context;
    }

    // Dữ liệu được phép chỉnh sửa.
    // Không cho sửa UserId hoặc trạng thái xác minh.
    public class EditInput
    {
        [Required(ErrorMessage = "Vui lòng nhập tên doanh nghiệp.")]
        public string CompanyName { get; set; } = "";

        public string? TaxCode { get; set; }

        public string? Industry { get; set; }

        public string? Address { get; set; }

        public string? Website { get; set; }

        public string? Description { get; set; }
    }

    private int? GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(value, out var userId)
            ? userId
            : null;
    }

    private async Task<bool> CurrentUserIsActiveAsync()
    {
        var userId = GetCurrentUserId();

        if (!userId.HasValue)
        {
            return false;
        }

        return await _context.Users
            .AsNoTracking()
            .AnyAsync(x =>
                x.UserId == userId.Value &&
                x.IsActive);
    }

    // ================= DANH SÁCH DOANH NGHIỆP =================

    [HttpGet]
    public async Task<IActionResult> Index(string? keyword)
    {
        if (!await CurrentUserIsActiveAsync())
        {
            return Forbid();
        }

        keyword = keyword?.Trim();

        var query = _context.Companies
            .AsNoTracking()
            .Where(x =>
                x.User.IsActive &&
                x.User.UserRoles.Any(
                    r => r.Role.RoleName == "Employer"));

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x =>
                x.CompanyName.Contains(keyword) ||
                (x.Industry != null &&
                 x.Industry.Contains(keyword)) ||
                (x.Address != null &&
                 x.Address.Contains(keyword)));
        }

        ViewBag.Keyword = keyword;

        var companies = await query
            .OrderBy(x => x.CompanyName)
            .ToListAsync();

        return View(companies);
    }

    // ================= CHI TIẾT HỒ SƠ =================

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (!await CurrentUserIsActiveAsync())
        {
            return Forbid();
        }

        // id là CompanyId.
        var company = await _context.Companies
            .AsNoTracking()
            .Include(x => x.User)
            .SingleOrDefaultAsync(x =>
                x.CompanyId == id &&
                x.User.IsActive &&
                x.User.UserRoles.Any(
                    r => r.Role.RoleName == "Employer"));

        if (company == null)
        {
            return NotFound("Không tìm thấy doanh nghiệp.");
        }

        ViewBag.IsOwner =
            User.IsInRole("Employer") &&
            GetCurrentUserId() == company.UserId;

        return View(company);
    }

    // ================= HỒ SƠ CỦA DOANH NGHIỆP ĐANG ĐĂNG NHẬP =================

    [HttpGet]
    [Authorize(Roles = "Employer")]
    public async Task<IActionResult> MyProfile()
    {
        if (!await CurrentUserIsActiveAsync())
        {
            return Forbid();
        }

        var userId = GetCurrentUserId();

        var companyId = await _context.Companies
            .AsNoTracking()
            .Where(x => x.UserId == userId.Value)
            .Select(x => (int?)x.CompanyId)
            .SingleOrDefaultAsync();

        if (!companyId.HasValue)
        {
            return NotFound(
                "Tài khoản này chưa có hồ sơ doanh nghiệp.");
        }

        return RedirectToAction(
            nameof(Details),
            new { id = companyId.Value });
    }

    // ================= MỞ FORM CHỈNH SỬA =================

    [HttpGet]
    [Authorize(Roles = "Employer")]
    public async Task<IActionResult> Edit()
    {
        if (!await CurrentUserIsActiveAsync())
        {
            return Forbid();
        }

        var userId = GetCurrentUserId();

        // Chỉ lấy hồ sơ thuộc tài khoản đang đăng nhập.
        var company = await _context.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.UserId == userId.Value);

        if (company == null)
        {
            return NotFound("Không tìm thấy hồ sơ doanh nghiệp.");
        }

        var model = new EditInput
        {
            CompanyName = company.CompanyName,
            TaxCode = company.TaxCode,
            Industry = company.Industry,
            Address = company.Address,
            Website = company.Website,
            Description = company.Description
        };

        return View(model);
    }

    // ================= LƯU CHỈNH SỬA =================

    [HttpPost]
    [Authorize(Roles = "Employer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditInput input)
    {
        if (!await CurrentUserIsActiveAsync())
        {
            return Forbid();
        }

        input.CompanyName = (input.CompanyName ?? "").Trim();

        if (string.IsNullOrWhiteSpace(input.CompanyName))
        {
            ModelState.AddModelError(
                nameof(input.CompanyName),
                "Vui lòng nhập tên doanh nghiệp.");
        }

        // Chỉ chấp nhận liên kết website HTTP hoặc HTTPS.
        if (!string.IsNullOrWhiteSpace(input.Website))
        {
            input.Website = input.Website.Trim();

            if (!Uri.TryCreate(
                    input.Website,
                    UriKind.Absolute,
                    out var websiteUri) ||
                (websiteUri.Scheme != Uri.UriSchemeHttp &&
                 websiteUri.Scheme != Uri.UriSchemeHttps))
            {
                ModelState.AddModelError(
                    nameof(input.Website),
                    "Website cần bắt đầu bằng https:// hoặc http://.");
            }
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var userId = GetCurrentUserId();

        // Không nhận UserId từ form để tránh sửa hồ sơ người khác.
        var company = await _context.Companies
            .SingleOrDefaultAsync(
                x => x.UserId == userId.Value);

        if (company == null)
        {
            return NotFound("Không tìm thấy hồ sơ doanh nghiệp.");
        }

        company.CompanyName = input.CompanyName;
        company.TaxCode = Clean(input.TaxCode);
        company.Industry = Clean(input.Industry);
        company.Address = Clean(input.Address);
        company.Website = Clean(input.Website);
        company.Description = Clean(input.Description);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(
                "",
                "Chưa lưu được hồ sơ. Vui lòng kiểm tra dữ liệu, mã số thuế có thể đã được sử dụng.");

            return View(input);
        }

        TempData["CompanyProfileSuccess"] =
            "Đã cập nhật hồ sơ doanh nghiệp.";

        return RedirectToAction(
            nameof(Details),
            new { id = company.CompanyId });
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}