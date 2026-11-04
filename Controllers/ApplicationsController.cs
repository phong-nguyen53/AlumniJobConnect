using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace AlumniJobConnect.Controllers;

[Authorize]
public class ApplicationsController : Controller
{
    private readonly AlumniJobConnectContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ApplicationsController> _logger;

    private const long MaxCvSize = 5 * 1024 * 1024;

    public ApplicationsController(
        AlumniJobConnectContext context,
        IWebHostEnvironment environment,
        ILogger<ApplicationsController> logger)
    {
        _context = context;
        _environment = environment;
        _logger = logger;
    }

    // Lưu ngoài wwwroot để file không được truy cập công khai.
    private string CvFolder => Path.Combine(
        _environment.ContentRootPath,
        "App_Data",
        "CVs");

    public class ApplyInput
    {
        public int CompanyId { get; set; }

        [Range(
            1,
            int.MaxValue,
            ErrorMessage = "Vui lòng chọn vị trí ứng tuyển.")]
        public int JobPostId { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn file CV.")]
        public IFormFile? CvFile { get; set; }

        [StringLength(
            4000,
            ErrorMessage = "Lời giới thiệu không quá 4.000 ký tự.")]
        public string? CoverLetter { get; set; }
    }

    private int? GetCurrentUserId()
    {
        var value = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        return int.TryParse(value, out var id)
            ? id
            : null;
    }

    private async Task<bool> CurrentUserIsActiveAsync()
    {
        var userId = GetCurrentUserId();

        return userId.HasValue &&
            await _context.Users
                .AsNoTracking()
                .AnyAsync(x =>
                    x.UserId == userId.Value &&
                    x.IsActive);
    }

    private IQueryable<JobPost> AvailableJobs(int companyId)
    {
        var today = DateOnly.FromDateTime(
            DateTime.UtcNow.AddHours(7));

        return _context.JobPosts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.IsPublished &&
                x.ApprovalStatus == "Approved" &&
                (!x.Deadline.HasValue ||
                 x.Deadline.Value >= today) &&
                x.Company.User.IsActive &&
                x.Company.User.UserRoles.Any(
                    r => r.Role.RoleName == "Employer"));
    }

    private async Task LoadFormAsync(Company company)
    {
        ViewBag.CompanyName = company.CompanyName;

        ViewBag.Jobs = await AvailableJobs(company.CompanyId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    // Giữ đuôi file đơn giản để đặt tên tải xuống.
    // File có đuôi đặc biệt vẫn được nhận nhưng lưu bằng tên không đuôi.
    private static string GetFileExtension(string originalName)
    {
        var extension = Path.GetExtension(originalName);

        if (extension.Length > 20 ||
            extension.Skip(1).Any(c =>
                !((c >= 'a' && c <= 'z') ||
                  (c >= 'A' && c <= 'Z') ||
                  (c >= '0' && c <= '9'))))
        {
            return "";
        }

        return extension.ToLowerInvariant();
    }

    // ================= MỞ FORM GỬI CV =================

    [HttpGet]
    [Authorize(Roles = "Alumni")]
    public async Task<IActionResult> Create(int companyId)
    {
        if (!await CurrentUserIsActiveAsync())
        {
            return Forbid();
        }

        var company = await _context.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.CompanyId == companyId &&
                x.User.IsActive &&
                x.User.UserRoles.Any(
                    r => r.Role.RoleName == "Employer"));

        if (company == null)
        {
            return NotFound("Không tìm thấy doanh nghiệp.");
        }

        await LoadFormAsync(company);

        return View(new ApplyInput
        {
            CompanyId = company.CompanyId
        });
    }

    // ================= LƯU ĐƠN VÀ FILE CV =================

    [HttpPost]
    [Authorize(Roles = "Alumni")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Create(ApplyInput input)
    {
        if (!await CurrentUserIsActiveAsync())
        {
            return Forbid();
        }

        var userId = GetCurrentUserId();

        var alumni = await _context.AlumniProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.UserId == userId.Value);

        if (alumni == null)
        {
            return BadRequest(
                "Vui lòng tạo hồ sơ sinh viên trước khi ứng tuyển.");
        }

        var company = await _context.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.CompanyId == input.CompanyId &&
                x.User.IsActive &&
                x.User.UserRoles.Any(
                    r => r.Role.RoleName == "Employer"));

        if (company == null)
        {
            return NotFound("Không tìm thấy doanh nghiệp.");
        }

        // Kiểm tra tin thuộc đúng công ty và còn nhận ứng tuyển.
        var job = await AvailableJobs(input.CompanyId)
            .SingleOrDefaultAsync(
                x => x.JobPostId == input.JobPostId);

        if (job == null)
        {
            ModelState.AddModelError(
                nameof(input.JobPostId),
                "Vị trí không hợp lệ hoặc đã hết hạn tuyển dụng.");
        }

        var alreadyApplied = await _context
            .Set<AlumniJobConnect.Models.Application>()
            .AnyAsync(x =>
                x.AlumniId == alumni.AlumniId &&
                x.JobPostId == input.JobPostId);

        if (alreadyApplied)
        {
            ModelState.AddModelError(
                "",
                "Bạn đã ứng tuyển vị trí này.");
        }

        if (input.CvFile != null &&
            (input.CvFile.Length == 0 ||
             input.CvFile.Length > MaxCvSize))
        {
            ModelState.AddModelError(
                nameof(input.CvFile),
                "File phải có dung lượng lớn hơn 0 và không quá 5 MB.");
        }

        var statusId = await _context
            .Set<ApplicationStatus>()
            .Where(x => x.StatusCode == "SUBMITTED")
            .Select(x => (int?)x.ApplicationStatusId)
            .SingleOrDefaultAsync();

        if (!statusId.HasValue)
        {
            ModelState.AddModelError(
                "",
                "Chưa có trạng thái SUBMITTED trong database.");
        }

        if (!ModelState.IsValid)
        {
            await LoadFormAsync(company);
            return View(input);
        }

        var extension = GetFileExtension(
            input.CvFile!.FileName);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(CvFolder, fileName);

        try
        {
            Directory.CreateDirectory(CvFolder);

            // Không dùng tên file của người dùng làm đường dẫn lưu.
            await using (var stream = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true))
            {
                await input.CvFile.CopyToAsync(stream);
            }

            var application =
                new AlumniJobConnect.Models.Application
                {
                    JobPostId = job!.JobPostId,
                    AlumniId = alumni.AlumniId,
                    ApplicationStatusId = statusId!.Value,
                    Cvurl = fileName,

                    CoverLetter =
                        string.IsNullOrWhiteSpace(input.CoverLetter)
                            ? null
                            : input.CoverLetter.Trim(),

                    AppliedAt = DateTime.Now
                };

            _context
                .Set<AlumniJobConnect.Models.Application>()
                .Add(application);

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            try
            {
                System.IO.File.Delete(filePath);
            }
            catch (Exception cleanupError)
            {
                _logger.LogWarning(
                    cleanupError,
                    "Không thể dọn file CV sau khi lưu đơn thất bại.");
            }

            _logger.LogError(
                ex,
                "Không thể lưu đơn ứng tuyển.");

            ModelState.AddModelError(
                "",
                "Chưa gửi được CV. Vui lòng chọn lại file và thử lại.");

            await LoadFormAsync(company);
            return View(input);
        }

        TempData["CompanyProfileSuccess"] =
            "Đã gửi CV ứng tuyển thành công.";

        return RedirectToAction(
            "Details",
            "CompanyProfile",
            new { id = company.CompanyId });
    }

    // ================= DANH SÁCH ĐƠN GỬI ĐẾN CÔNG TY =================

    [HttpGet]
    [Authorize(Roles = "Employer")]
    public async Task<IActionResult> Received()
    {
        if (!await CurrentUserIsActiveAsync())
        {
            return Forbid();
        }

        var userId = GetCurrentUserId();

        var applications = await _context
            .Set<AlumniJobConnect.Models.Application>()
            .AsNoTracking()
            .Include(x => x.Alumni)
                .ThenInclude(x => x.User)
            .Include(x => x.JobPost)
            .Include(x => x.ApplicationStatus)
            .Where(x =>
                x.JobPost.Company.UserId == userId.Value)
            .OrderByDescending(x => x.AppliedAt)
            .ToListAsync();

        ViewBag.StatusOptions = await EmployerStatuses()
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        return View(applications);
    }

    // ================= TẢI FILE CV =================



    [HttpGet]
    [Authorize(Roles = "Alumni")]
    public async Task<IActionResult> MyApplications()
    {
        if (GetCurrentUserId() is not int userId ||
            !await CurrentUserIsActiveAsync())
        {
            return Forbid();
        }

        var applications = await _context
            .Set<AlumniJobConnect.Models.Application>()
            .AsNoTracking()
            .Where(x => x.Alumni.UserId == userId)
            .Include(x => x.JobPost)
                .ThenInclude(x => x.Company)
            .Include(x => x.ApplicationStatus)
            .OrderByDescending(x => x.AppliedAt)
            .ThenByDescending(x => x.ApplicationId)
            .ToListAsync();

        return View(applications);
    }




    [HttpGet]
    public async Task<IActionResult> DownloadCv(int id)
    {
        if (!await CurrentUserIsActiveAsync())
        {
            return Forbid();
        }

        var application = await _context
            .Set<AlumniJobConnect.Models.Application>()
            .AsNoTracking()
            .Where(x => x.ApplicationId == id)
            .Select(x => new
            {
                x.Cvurl,
                ApplicantUserId = x.Alumni.UserId,
                EmployerUserId = x.JobPost.Company.UserId
            })
            .SingleOrDefaultAsync();

        if (application == null)
        {
            return NotFound("Không tìm thấy đơn ứng tuyển.");
        }

        var userId = GetCurrentUserId();

        var isApplicant =
            User.IsInRole("Alumni") &&
            userId == application.ApplicantUserId;

        var isEmployer =
            User.IsInRole("Employer") &&
            userId == application.EmployerUserId;

        if (!isApplicant && !isEmployer)
        {
            return Forbid();
        }

        var fileName = application.Cvurl;

        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName != Path.GetFileName(fileName) ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            !Guid.TryParseExact(
                Path.GetFileNameWithoutExtension(fileName),
                "N",
                out _))
        {
            return NotFound("Không tìm thấy CV.");
        }

        var extension = Path.GetExtension(fileName);

        if (extension != GetFileExtension(fileName))
        {
            return NotFound("Tên file CV không hợp lệ.");
        }

        var path = Path.Combine(CvFolder, fileName);

        if (!System.IO.File.Exists(path))
        {
            return NotFound("File CV không còn tồn tại.");
        }

        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        // Luôn trả file dưới dạng tải xuống.
        return PhysicalFile(
            path,
            "application/octet-stream",
            $"CV-ung-tuyen-{id}{extension}");
    }


    private IQueryable<ApplicationStatus> EmployerStatuses()
    {
        return _context.Set<ApplicationStatus>().Where(x =>
            x.StatusCode == "REVIEWING" ||
            x.StatusCode == "INTERVIEW" ||
            x.StatusCode == "ACCEPTED" ||
            x.StatusCode == "REJECTED");
    }

    [HttpPost]
    [Authorize(Roles = "Employer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(
        int applicationId,
        int statusId)
    {
        if (!await CurrentUserIsActiveAsync() ||
            GetCurrentUserId() is not int userId)
        {
            return Forbid();
        }

        var application = await _context
            .Set<AlumniJobConnect.Models.Application>()
            .SingleOrDefaultAsync(x =>
                x.ApplicationId == applicationId &&
                x.JobPost.Company.UserId == userId);

        if (application == null)
        {
            return NotFound();
        }

        var validStatus = await EmployerStatuses()
            .AnyAsync(x => x.ApplicationStatusId == statusId);

        if (!validStatus)
        {
            TempData["ApplicationStatusError"] =
                "Vui lòng chọn trạng thái xét tuyển hợp lệ.";

            return RedirectToAction(nameof(Received));
        }

        if (application.ApplicationStatusId != statusId)
        {
            application.ApplicationStatusId = statusId;
            application.UpdatedAt = DateTime.Now;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Không thể cập nhật trạng thái đơn {ApplicationId}.",
                    applicationId);

                TempData["ApplicationStatusError"] =
                    "Chưa lưu được trạng thái. Vui lòng thử lại.";

                return RedirectToAction(nameof(Received));
            }
        }

        TempData["ApplicationStatusSuccess"] =
            "Đã lưu trạng thái xét tuyển.";

        return RedirectToAction(nameof(Received));
    }
}