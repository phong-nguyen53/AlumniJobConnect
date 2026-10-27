using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace AlumniJobConnect.Controllers;

[Authorize(Roles = "Employer")]
public class EmployerJobsController : Controller
{
    private readonly AlumniJobConnectContext _context;

    public EmployerJobsController(AlumniJobConnectContext context)
    {
        _context = context;
    }

    // Dữ liệu nhập từ form
    public class JobInput
    {
        [Required(ErrorMessage = "Vui lòng nhập tên công việc.")]
        [StringLength(200)]
        public string Title { get; set; } = "";

        [Required(ErrorMessage = "Vui lòng chọn ngành nghề.")]
        public int? JobCategoryId { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập địa điểm làm việc.")]
        [StringLength(200)]
        public string WorkLocation { get; set; } = "";

        [Required]
        [RegularExpression(
            "^(Full Time|Part Time)$",
            ErrorMessage = "Hình thức làm việc không hợp lệ.")]
        public string EmploymentType { get; set; } = "Full Time";

        [Range(0, 1000000000)]
        public decimal? SalaryMin { get; set; }

        [Range(0, 1000000000)]
        public decimal? SalaryMax { get; set; }

        [Range(1, 10000,
            ErrorMessage = "Số người cần tuyển phải từ 1 đến 10.000.")]
        public int Quantity { get; set; } = 1;

        [Required(ErrorMessage = "Vui lòng chọn hạn nộp hồ sơ.")]
        [DataType(DataType.Date)]
        public DateOnly? Deadline { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mô tả công việc.")]
        public string JobDescription { get; set; } = "";

        public string? Requirements { get; set; }

        public string? Benefits { get; set; }
    }

    private async Task LoadCategories(int? selectedId = null)
    {
        var categories = await _context.JobCategories
            .AsNoTracking()
            .OrderBy(x => x.CategoryName)
            .ToListAsync();

        ViewBag.Categories = new SelectList(
            categories,
            "JobCategoryId",
            "CategoryName",
            selectedId);
    }

   
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadCategories();

        return View(new JobInput());
    }

   
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(JobInput input)
    {
        if (!int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var userId))
        {
            return Challenge();
        }

        var user = await _context.Users
            .Include(x => x.Company)
            .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.UserId == userId);

        if (user == null || !user.IsActive
            || !user.UserRoles.Any(x => x.Role.RoleName == "Employer"))
        {
            return Forbid();
        }

        if (user.Company == null)
        {
            ModelState.AddModelError(
                "", "Tài khoản chưa có thông tin doanh nghiệp.");
        }

        if (input.SalaryMin.HasValue
            && input.SalaryMax.HasValue
            && input.SalaryMin > input.SalaryMax)
        {
            ModelState.AddModelError(
                nameof(input.SalaryMax),
                "Lương tối đa phải lớn hơn hoặc bằng lương tối thiểu.");
        }

        if (input.Deadline.HasValue
            && input.Deadline.Value < DateOnly.FromDateTime(DateTime.Today))
        {
            ModelState.AddModelError(
                nameof(input.Deadline),
                "Hạn nộp hồ sơ không được trước hôm nay.");
        }

        if (input.JobCategoryId.HasValue
            && !await _context.JobCategories.AnyAsync(
                x => x.JobCategoryId == input.JobCategoryId.Value))
        {
            ModelState.AddModelError(
                nameof(input.JobCategoryId),
                "Ngành nghề không tồn tại.");
        }

        if (!ModelState.IsValid)
        {
            await LoadCategories(input.JobCategoryId);
            return View(input);
        }

        var job = new JobPost
        {
            
            CompanyId = user.Company!.CompanyId,
            JobCategoryId = input.JobCategoryId,
            Title = input.Title.Trim(),
            WorkLocation = input.WorkLocation.Trim(),
            EmploymentType = input.EmploymentType,
            SalaryMin = input.SalaryMin,
            SalaryMax = input.SalaryMax,
            CurrencyCode = "VND",
            Quantity = input.Quantity,
            Deadline = input.Deadline,
            JobDescription = input.JobDescription.Trim(),
            Requirements = input.Requirements?.Trim(),
            Benefits = input.Benefits?.Trim(),
            ApprovalStatus = "Pending",
            IsPublished = false,
            CreatedAt = DateTime.Now
        };

        _context.JobPosts.Add(job);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(
                "",
                "Chưa thể lưu tin. Vui lòng kiểm tra dữ liệu và ràng buộc database.");

            await LoadCategories(input.JobCategoryId);
            return View(input);
        }

        TempData["JobSuccess"] =
            "Gửi tin thành công! Tin tuyển dụng đang chờ duyệt.";

        return RedirectToAction(nameof(Create));
    }
}