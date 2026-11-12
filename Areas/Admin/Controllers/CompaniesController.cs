using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class CompaniesController : Controller
{
    private readonly AlumniJobConnectContext _context;

    public CompaniesController(AlumniJobConnectContext context)
    {
        _context = context;
    }

    // 1. HIỂN THỊ DANH SÁCH

    [HttpGet("/Admin/Companies", Name = "AdminCompanies")]
    public async Task<IActionResult> Index()
    {
        var companies = await _context.Companies
            .AsNoTracking()
            .Include(company => company.JobPosts)
            .OrderBy(company => company.CompanyName)
            .ThenBy(company => company.CompanyId)
            .ToListAsync();

        return View(companies);
    }

    // 2. THÊM DOANH NGHIỆP

    [HttpGet("/Admin/Companies/Create", Name = "AdminCreateCompany")]
    public async Task<IActionResult> Create()
    {
        await LoadEmployerOptionsAsync();
        return View(new Company());
    }

    [HttpPost("/Admin/Companies/Create", Name = "AdminCreateCompanySubmit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromForm] int? userId,
        [FromForm] string? companyName,
        [FromForm] string? taxCode,
        [FromForm] string? industry,
        [FromForm] string? address,
        [FromForm] string? website,
        [FromForm] string? description)
    {
        var company = new Company
        {
            UserId = userId.GetValueOrDefault(),
            VerificationStatus = "Pending",
            CreatedAt = DateTime.Now
        };

        ApplyCompanyFields(company, companyName, taxCode, industry,
            address, website, description);
        ValidateCompanyFields(company);

        try
        {
            await using var transaction = await _context.Database
                .BeginTransactionAsync(IsolationLevel.Serializable);

            var canUseAccount = await AvailableEmployers()
                .AnyAsync(user => user.UserId == company.UserId);

            if (!canUseAccount)
            {
                ModelState.AddModelError(nameof(userId),
                    "Hãy chọn tài khoản doanh nghiệp đang hoạt động và chưa có hồ sơ.");
            }

            if (ModelState.IsValid)
            {
                _context.Companies.Add(company);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["CompanySuccess"] = "Đã thêm doanh nghiệp.";
                return RedirectToRoute("AdminCompanies");
            }
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("",
                "Chưa thêm được hồ sơ. Hãy kiểm tra dữ liệu và thử lại.");
        }

        await LoadEmployerOptionsAsync(company.UserId);
        return View(company);
    }

    // 3. SỬA DOANH NGHIỆP

    [HttpGet("/Admin/Companies/{id:int}/Edit", Name = "AdminEditCompany")]
    public async Task<IActionResult> Edit(int id)
    {
        var company = await _context.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompanyId == id);

        if (company is null)
        {
            return NotFound();
        }

        return View(company);
    }

    [HttpPost("/Admin/Companies/{id:int}/Edit", Name = "AdminEditCompanySubmit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        [FromRoute] int id,
        [FromForm] string? companyName,
        [FromForm] string? taxCode,
        [FromForm] string? industry,
        [FromForm] string? address,
        [FromForm] string? website,
        [FromForm] string? description)
    {
        var company = await _context.Companies.FindAsync(id);

        if (company is null)
        {
            return NotFound();
        }

        ApplyCompanyFields(company, companyName, taxCode, industry,
            address, website, description);
        ValidateCompanyFields(company);

        if (!ModelState.IsValid)
        {
            return View(company);
        }

        try
        {
            await _context.SaveChangesAsync();

            TempData["CompanySuccess"] = "Đã cập nhật doanh nghiệp.";
            return RedirectToRoute("AdminCompanies");
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("",
                "Chưa lưu được thay đổi. Hãy kiểm tra dữ liệu hoặc tải lại trang.");
            return View(company);
        }
    }

    // 4. XÓA DOANH NGHIỆP

    [HttpPost("/Admin/Companies/{id:int}/Delete", Name = "AdminDeleteCompany")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        try
        {
            await using var transaction = await _context.Database
                .BeginTransactionAsync(IsolationLevel.Serializable);

            var company = await _context.Companies.FindAsync(id);

            if (company is null)
            {
                TempData["CompanyError"] = "Hồ sơ doanh nghiệp không còn tồn tại.";
                return RedirectToRoute("AdminCompanies");
            }

            var hasJobPosts = await _context.JobPosts
                .AnyAsync(job => job.CompanyId == id);

            if (hasJobPosts)
            {
                TempData["CompanyError"] =
                    "Doanh nghiệp đã có tin tuyển dụng nên chưa thể xóa hồ sơ.";

                return RedirectToRoute("AdminCompanies");
            }

            _context.Companies.Remove(company);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["CompanySuccess"] = "Đã xóa hồ sơ doanh nghiệp.";
        }
        catch (DbUpdateException)
        {
            TempData["CompanyError"] =
                "Chưa xóa được hồ sơ. Hãy tải lại danh sách và thử lại.";
        }

        return RedirectToRoute("AdminCompanies");
    }

    // HÀM DÙNG CHUNG

    private static void ApplyCompanyFields(
        Company company,
        string? companyName,
        string? taxCode,
        string? industry,
        string? address,
        string? website,
        string? description)
    {
        company.CompanyName = companyName?.Trim() ?? "";
        company.TaxCode = OptionalText(taxCode);
        company.Industry = OptionalText(industry);
        company.Address = OptionalText(address);
        company.Website = OptionalText(website);
        company.Description = OptionalText(description);
    }

    private void ValidateCompanyFields(Company company)
    {
        if (company.CompanyName.Length == 0)
        {
            ModelState.AddModelError("companyName",
                "Vui lòng nhập tên doanh nghiệp.");
        }

        if (company.Website is not null &&
            (!Uri.TryCreate(company.Website, UriKind.Absolute, out var url) ||
             (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps) ||
             string.IsNullOrWhiteSpace(url.Host)))
        {
            ModelState.AddModelError("website",
                "Website phải bắt đầu bằng http:// hoặc https:// và có địa chỉ máy chủ.");
        }
    }

    private IQueryable<AlumniJobConnect.Models.User> AvailableEmployers()
    {
        return _context.Users
            .AsNoTracking()
            .Where(user =>
                user.IsActive &&
                user.UserRoles.Any(link => link.Role.RoleName == "Employer") &&
                !_context.Companies.Any(company => company.UserId == user.UserId));
    }

    private async Task LoadEmployerOptionsAsync(int? selectedUserId = null)
    {
        var employers = await AvailableEmployers()
            .OrderBy(user => user.Email)
            .ThenBy(user => user.UserId)
            .Select(user => new
            {
                user.UserId,
                Label = user.FullName + " (" + user.Email + ")"
            })
            .ToListAsync();

        ViewBag.EmployerOptions =
            new SelectList(employers, "UserId", "Label", selectedUserId);
    }

    private static string? OptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}