using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly AlumniJobConnectContext _context;
        private readonly IWebHostEnvironment _environment;

        public ProfileController(
            AlumniJobConnectContext context,
            IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // Dữ liệu của form chỉnh sửa hồ sơ.
        public class ProfileInput
        {
            public string? AvatarUrl { get; set; }

            public IFormFile? AvatarFile { get; set; }

            public string Email { get; set; } = "";

            [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
            [StringLength(150,
                ErrorMessage = "Họ tên tối đa 150 ký tự.")]
            public string FullName { get; set; } = "";

            [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
            [StringLength(20,
                ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
            public string? Phone { get; set; }

            [DataType(DataType.Date)]
            public DateOnly? DateOfBirth { get; set; }

            [Range(1900, 2100,
                ErrorMessage = "Năm tốt nghiệp phải từ 1900 đến 2100.")]
            public short? GraduationYear { get; set; }

            [StringLength(1000,
                ErrorMessage = "Giới thiệu bản thân tối đa 1.000 ký tự.")]
            public string? Bio { get; set; }
        }

        public sealed class WorkMentorInput
        {
            [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn công ty hợp lệ.")]
            public int? CompanyId { get; set; }

            [StringLength(100, ErrorMessage = "Vị trí công việc tối đa 100 ký tự.")]
            public string? JobTitle { get; set; }

            [StringLength(150, ErrorMessage = "Chủ đề tư vấn tối đa 150 ký tự.")]
            public string? Expertise { get; set; }

            [StringLength(500, ErrorMessage = "Giới thiệu tư vấn tối đa 500 ký tự.")]
            public string? Introduction { get; set; }

            public bool IsAvailable { get; set; }
        }

        public sealed class CompanyOption
        {
            public int CompanyId { get; set; }
            public string CompanyName { get; set; } = "";
        }

        // Có thể dùng cả tin hết hạn để gợi ý công việc hiện tại.
        [HttpGet("/Profile/CompanyJobs", Name = "ProfileCompanyJobs")]
        [Authorize(Roles = "Alumni")]
        public async Task<IActionResult> CompanyJobs(int companyId)
        {
            if (!await CurrentUserIsActiveAsync()) return Forbid();
            var company = await _context.Companies.AsNoTracking()
                .Where(c => c.CompanyId == companyId)
                .Select(c => new { c.Industry }).SingleOrDefaultAsync();
            if (company == null) return NotFound();
            var jobs = await _context.JobPosts.AsNoTracking()
                .Where(j => j.CompanyId == companyId && j.ApprovalStatus == "Approved"
                    && j.IsPublished && j.Title != null && j.Title.Trim() != "")
                .OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.JobPostId)
                .Select(j => new { id = j.JobPostId, title = j.Title })
                .ToListAsync();
            return Json(new { industry = company.Industry, jobs });
        }

        private async Task LoadWorkMentorAsync(int userId)
        {
            ViewData["WorkMentor"] = await _context.Database.SqlQuery<WorkMentorInput>($"""
                SELECT
                    currentJob.CompanyId,
                    COALESCE(currentJob.JobTitle, profile.CurrentJobTitle) AS JobTitle,
                    mentor.Expertise,
                    mentor.Introduction,
                    COALESCE(mentor.IsAvailable, CAST(0 AS bit)) AS IsAvailable
                FROM dbo.AlumniProfiles AS profile
                LEFT JOIN dbo.MentorProfiles AS mentor ON mentor.UserId = profile.UserId
                OUTER APPLY (
                    SELECT TOP (1) experience.CompanyId, experience.JobTitle
                    FROM dbo.WorkExperiences AS experience
                    WHERE experience.AlumniId = profile.AlumniId AND experience.IsCurrent = 1
                    ORDER BY experience.StartDate DESC, experience.WorkExperienceId DESC
                ) AS currentJob
                WHERE profile.UserId = {userId}
                """).SingleOrDefaultAsync() ?? new WorkMentorInput();

            var work = (WorkMentorInput)ViewData["WorkMentor"]!;
            var companies = await _context.Database.SqlQuery<CompanyOption>($"""
                SELECT company.CompanyId, company.CompanyName
                FROM dbo.Companies AS company
                INNER JOIN dbo.Users AS account ON account.UserId = company.UserId
                WHERE account.IsActive = 1 OR company.CompanyId = {work.CompanyId}
                """)
                .OrderBy(company => company.CompanyName)
                .ThenBy(company => company.CompanyId)
                .ToListAsync();

            ViewData["WorkCompanyOptions"] = new SelectList(
                companies, "CompanyId", "CompanyName", work.CompanyId);
        }

        [HttpPost("/Profile/WorkMentor", Name = "SaveWorkMentor")]
        [Authorize(Roles = "Alumni")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveWorkMentor([Bind(Prefix = "Work")] WorkMentorInput input, [FromForm] int? jobPostId)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue || !await CurrentUserIsActiveAsync())
            {
                return Forbid();
            }

            // Mã tin phải thuộc công ty đã chọn; tên vị trí lấy lại từ SQL.
            if (jobPostId.HasValue && jobPostId.Value != 0)
            {
                var title = await _context.JobPosts.AsNoTracking()
                    .Where(j => j.JobPostId == jobPostId.Value
                        && j.CompanyId == input.CompanyId
                        && j.ApprovalStatus == "Approved" && j.IsPublished)
                    .Select(j => j.Title).SingleOrDefaultAsync();
                if (string.IsNullOrWhiteSpace(title))
                    ModelState.AddModelError("jobPostId", "Vị trí không thuộc công ty đã chọn hoặc tin không còn hiển thị. Vui lòng chọn lại.");
                else
                {
                    input.JobTitle = title.Trim();
                    ModelState.Remove("Work.JobTitle");
                    ModelState.SetModelValue("Work.JobTitle", new Microsoft.AspNetCore.Mvc.ModelBinding.ValueProviderResult(input.JobTitle));
                    if (input.JobTitle.Length > 100)
                    {
                        ModelState.AddModelError(
                            "Work.JobTitle",
                            "Tên tin dài hơn 100 ký tự. Chọn Khác và nhập tên vị trí ngắn gọn.");
                    }
                    else
                    {
                        // SetModelValue đưa ô về trạng thái chưa kiểm tra.
                        // Tên đã lấy từ SQL và kiểm tra độ dài nên đánh dấu hợp lệ.
                        ModelState.MarkFieldValid("Work.JobTitle");
                    }
                }
            }

            input.JobTitle = input.JobTitle?.Trim();
            input.Expertise = input.Expertise?.Trim();
            input.Introduction = input.Introduction?.Trim();

            var hasCompany = input.CompanyId.HasValue;
            var hasJob = !string.IsNullOrWhiteSpace(input.JobTitle);

            if (hasCompany != hasJob || input.IsAvailable)
            {
                if (!hasCompany)
                    ModelState.AddModelError("Work.CompanyId", "Vui lòng chọn công ty đang làm.");
                if (!hasJob)
                    ModelState.AddModelError("Work.JobTitle", "Vui lòng nhập vị trí công việc.");
            }

            if (input.IsAvailable && string.IsNullOrWhiteSpace(input.Expertise))
            {
                ModelState.AddModelError("Work.Expertise", "Vui lòng nhập chủ đề có thể tư vấn.");
            }

            if (!ModelState.IsValid)
            {
                return await WorkMentorErrorViewAsync(input);
            }

            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();

                // Khóa hồ sơ trong lúc lưu để các lần gửi cùng lúc không tạo trùng công việc.
                var alumniId = await _context.Database.SqlQuery<int>($"""
                    SELECT profile.AlumniId AS [Value]
                    FROM dbo.AlumniProfiles AS profile WITH (UPDLOCK, HOLDLOCK)
                    WHERE profile.UserId = {userId.Value}
                      AND (profile.GraduationConfirmedAtUtc IS NOT NULL OR profile.IsGraduationVerified = 1)
                      AND EXISTS (
                          SELECT 1 FROM dbo.Users AS account
                          INNER JOIN dbo.UserRoles AS userRole ON userRole.UserId = account.UserId
                          INNER JOIN dbo.Roles AS role ON role.RoleId = userRole.RoleId
                          WHERE account.UserId = profile.UserId
                            AND account.IsActive = 1 AND role.RoleName = 'Alumni'
                      )
                    """).SingleOrDefaultAsync();

                if (alumniId == 0)
                {
                    return Forbid();
                }

                // Lấy tên công ty từ SQL theo mã được chọn.
                // Công ty có tài khoản ngừng hoạt động chỉ được giữ lại nếu đang gắn với công việc hiện tại.
                CompanyOption? selectedCompany = null;

                if (input.CompanyId.HasValue)
                {
                    selectedCompany = await _context.Database.SqlQuery<CompanyOption>($"""
                        SELECT company.CompanyId, company.CompanyName
                        FROM dbo.Companies AS company WITH (HOLDLOCK)
                        INNER JOIN dbo.Users AS account WITH (HOLDLOCK)
                            ON account.UserId = company.UserId
                        WHERE company.CompanyId = {input.CompanyId.Value}
                          AND (
                              account.IsActive = 1 OR EXISTS (
                                  SELECT 1 FROM dbo.WorkExperiences
                                  WHERE AlumniId = {alumniId} AND IsCurrent = 1
                                    AND CompanyId = company.CompanyId
                              )
                          )
                        """).SingleOrDefaultAsync();

                    if (selectedCompany == null)
                    {
                        await transaction.RollbackAsync();
                        await transaction.DisposeAsync();
                        ModelState.AddModelError("Work.CompanyId", "Công ty không tồn tại hoặc tài khoản đã ngừng hoạt động.");
                        return await WorkMentorErrorViewAsync(input);
                    }
                }

                var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
                var companyId = input.CompanyId;
                var companyName = selectedCompany?.CompanyName;
                var jobTitle = hasJob ? input.JobTitle : null;
                var expertise = input.Expertise ?? "";

                await _context.Database.ExecuteSqlInterpolatedAsync($"""
                    IF {companyId} IS NULL OR NOT EXISTS (
                        SELECT 1 FROM dbo.WorkExperiences
                        WHERE AlumniId = {alumniId} AND IsCurrent = 1
                          AND CompanyId = {companyId} AND JobTitle = {jobTitle}
                    )
                    BEGIN
                        UPDATE dbo.WorkExperiences
                        SET IsCurrent = 0,
                            EndDate = CASE WHEN StartDate > {today} THEN StartDate ELSE {today} END
                        WHERE AlumniId = {alumniId} AND IsCurrent = 1;

                        IF {companyId} IS NOT NULL
                        BEGIN
                            INSERT INTO dbo.WorkExperiences (AlumniId, CompanyId, CompanyName, JobTitle, IsCurrent)
                            VALUES ({alumniId}, {companyId}, {companyName}, {jobTitle}, 1);
                        END;
                    END;

                    UPDATE dbo.AlumniProfiles
                    SET CurrentJobTitle = {jobTitle}, ProfileUpdatedAt = SYSDATETIME()
                    WHERE AlumniId = {alumniId};

                    UPDATE dbo.MentorProfiles WITH (UPDLOCK, HOLDLOCK)
                    SET Expertise = {expertise},
                        Introduction = {input.Introduction},
                        IsAvailable = {input.IsAvailable}
                    WHERE UserId = {userId.Value};

                    IF @@ROWCOUNT = 0
                    BEGIN
                        INSERT INTO dbo.MentorProfiles (UserId, Expertise, Introduction, IsAvailable)
                        VALUES ({userId.Value}, {expertise}, {input.Introduction}, {input.IsAvailable});
                    END;
                    """);

                await transaction.CommitAsync();
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                var detail = ex.Number switch
                {
                    207 => "Cấu trúc bảng chưa khớp với code. Kiểm tra các cột đã bổ sung trong SQL.",
                    208 => "Thiếu bảng dùng để lưu công việc hoặc thông tin tư vấn.",
                    547 => "Dữ liệu vi phạm liên kết hoặc điều kiện của bảng. Cần kiểm tra ràng buộc SQL.",
                    515 => "Bảng có cột bắt buộc chưa được cung cấp dữ liệu.",
                    2601 or 2627 => "Dữ liệu bị trùng với bản ghi đã có.",
                    2628 or 8152 => "Nội dung dài hơn giới hạn của cột trong SQL.",
                    1934 => "Thiết lập kết nối SQL chưa phù hợp với chỉ mục của bảng.",
                    _ => "Cần kiểm tra lỗi SQL trong nhật ký ứng dụng."
                };
                ModelState.AddModelError("", $"Chưa lưu được công việc và tư vấn (SQL {ex.Number}). {detail}");
                // Chi tiết kỹ thuật chỉ ghi nhật ký, không hiển thị ra trang.
                var logger = HttpContext.RequestServices.GetService(typeof(Microsoft.Extensions.Logging.ILogger<ProfileController>))
                    as Microsoft.Extensions.Logging.ILogger<ProfileController>;
                if (logger != null) Microsoft.Extensions.Logging.LoggerExtensions.LogError(logger, ex,
                    "Lỗi lưu công việc và tư vấn của UserId {UserId}", userId.Value);
                return await WorkMentorErrorViewAsync(input);
            }

            TempData["WorkMentorSuccess"] = "Đã cập nhật công việc và trạng thái nhận tư vấn.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<IActionResult> WorkMentorErrorViewAsync(WorkMentorInput input)
        {
            var result = await Index();
            if (result is ViewResult view)
            {
                view.ViewName = "Index";
                view.ViewData["WorkMentor"] = input;
                view.ViewData["WorkMentorFailed"] = true;
            }
            return result;
        }

        // Đường dẫn lưu ảnh, không cần sửa Program.cs.
        private string AvatarFolder => Path.Combine(
            _environment.ContentRootPath,
            "App_Data",
            "avatars");

        private int? GetCurrentUserId()
        {
            return int.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var userId)
                ? userId
                : null;
        }

        // =====================================================
        // SINH VIÊN XEM FORM CHỈNH SỬA HỒ SƠ CỦA MÌNH
        // =====================================================
        [HttpGet]
        [Authorize(Roles = "Alumni")]
        public async Task<IActionResult> Index()
        {
            var userId = GetCurrentUserId();

            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var user = await _context.Users
                .AsNoTracking()
                .Include(x => x.AlumniProfile)
                .SingleOrDefaultAsync(x => x.UserId == userId.Value);

            if (user == null || !user.IsActive)
            {
                return Forbid();
            }

            var model = new ProfileInput
            {
                Email = user.Email,
                FullName = user.FullName,
                Phone = user.Phone,
                AvatarUrl = user.AvatarUrl,
                DateOfBirth = user.AlumniProfile?.DateOfBirth,
                GraduationYear = user.AlumniProfile?.GraduationYear,
                Bio = user.AlumniProfile?.Bio
            };

            ViewData["GraduationState"] = await GetGraduationStateAsync(userId.Value);
            await LoadWorkMentorAsync(userId.Value);
            return View("Index", model);
        }

        [HttpPost("/Profile/ConfirmGraduation", Name = "ConfirmGraduation")]
        [Authorize(Roles = "Alumni")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmGraduation([FromForm] int graduationYear)
        {
            var userId = GetCurrentUserId();

            if (!userId.HasValue || !await CurrentUserIsActiveAsync())
            {
                return Forbid();
            }

            var currentYear = DateTime.UtcNow.AddHours(7).Year;

            if (!ModelState.IsValid || graduationYear < 1900 || graduationYear > currentYear)
            {
                TempData["GraduationError"] = "Vui lòng nhập năm tốt nghiệp hợp lệ.";
                return RedirectToAction(nameof(Index));
            }

            var updated = await _context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.AlumniProfiles
                SET GraduationYear = {graduationYear},
                    GraduationConfirmedAtUtc = SYSUTCDATETIME(),
                    ProfileUpdatedAt = SYSDATETIME()
                WHERE UserId = {userId.Value}
                  AND GraduationConfirmedAtUtc IS NULL
                  AND IsGraduationVerified = 0
                """);

            if (updated > 0)
            {
                TempData["GraduationSuccess"] = "Đã xác nhận tốt nghiệp.";
            }
            else
            {
                TempData["GraduationError"] =
                    "Hồ sơ đã xác nhận tốt nghiệp hoặc chưa có hồ sơ sinh viên.";
            }

            return RedirectToAction(nameof(Index));
        }

        // =====================================================
        // SINH VIÊN LƯU HỒ SƠ CỦA MÌNH
        // =====================================================
        [HttpPost]
        [Authorize(Roles = "Alumni")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(4 * 1024 * 1024)]
        public async Task<IActionResult> Index(ProfileInput model)
        {
            var userId = GetCurrentUserId();

            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            // Luôn lấy tài khoản từ thông tin đăng nhập.
            // Không nhận mã chủ hồ sơ từ form.
            var user = await _context.Users
                .Include(x => x.AlumniProfile)
                .SingleOrDefaultAsync(x => x.UserId == userId.Value);

            if (user == null || !user.IsActive)
            {
                return Forbid();
            }

            // Không cho form thay đổi email hoặc đường dẫn ảnh.
            ModelState.Remove(nameof(model.Email));
            ModelState.Remove(nameof(model.AvatarUrl));

            model.Email = user.Email;
            model.AvatarUrl = user.AvatarUrl;

            if (model.DateOfBirth.HasValue &&
                model.DateOfBirth.Value >
                DateOnly.FromDateTime(DateTime.Today))
            {
                ModelState.AddModelError(
                    nameof(model.DateOfBirth),
                    "Ngày sinh không được lớn hơn ngày hiện tại.");
            }

            byte[]? imageBytes = null;
            string? imageExtension = null;

            if (model.AvatarFile != null)
            {
                if (model.AvatarFile.Length == 0 ||
                    model.AvatarFile.Length > 2 * 1024 * 1024)
                {
                    ModelState.AddModelError(
                        nameof(model.AvatarFile),
                        "Vui lòng chọn ảnh có dung lượng tối đa 2 MB.");
                }
                else
                {
                    using var memory = new MemoryStream();

                    await model.AvatarFile.CopyToAsync(memory);

                    imageBytes = memory.ToArray();
                    imageExtension = GetImageExtension(imageBytes);

                    if (imageExtension == null)
                    {
                        ModelState.AddModelError(
                            nameof(model.AvatarFile),
                            "Chỉ chấp nhận ảnh JPG hoặc PNG.");
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                ViewData["GraduationState"] = await GetGraduationStateAsync(userId.Value);
                await LoadWorkMentorAsync(userId.Value);
                return View("Index", model);
            }

            string? newImagePath = null;

            try
            {
                if (imageBytes != null && imageExtension != null)
                {
                    Directory.CreateDirectory(AvatarFolder);

                    var fileName =
                        $"{Guid.NewGuid():N}{imageExtension}";

                    newImagePath = Path.Combine(
                        AvatarFolder,
                        fileName);

                    await System.IO.File.WriteAllBytesAsync(
                        newImagePath,
                        imageBytes);

                    // Database chỉ lưu tên ảnh.
                    user.AvatarUrl = fileName;
                }

                user.FullName = model.FullName.Trim();

                user.Phone = string.IsNullOrWhiteSpace(model.Phone)
                    ? null
                    : model.Phone.Trim();

                var existingProfile = user.AlumniProfile;

                if (user.AlumniProfile == null)
                {
                    user.AlumniProfile = new AlumniProfile
                    {
                        UserId = user.UserId,
                        GraduationYear = model.GraduationYear
                    };
                }

                user.AlumniProfile.DateOfBirth = model.DateOfBirth;

                user.AlumniProfile.Bio =
                    string.IsNullOrWhiteSpace(model.Bio)
                        ? null
                        : model.Bio.Trim();

                user.AlumniProfile.ProfileUpdatedAt = DateTime.Now;

                if (existingProfile != null && existingProfile.GraduationYear != model.GraduationYear)
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();

                    await _context.SaveChangesAsync();
                    await _context.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE dbo.AlumniProfiles
                        SET GraduationYear = {model.GraduationYear}
                        WHERE UserId = {userId.Value}
                          AND GraduationConfirmedAtUtc IS NULL
                          AND IsGraduationVerified = 0
                        """);

                    await transaction.CommitAsync();
                }
                else
                {
                    await _context.SaveChangesAsync();
                }
            }
            catch
            {
                // Dọn ảnh mới nếu chưa lưu hồ sơ thành công.
                if (newImagePath != null)
                {
                    try
                    {
                        System.IO.File.Delete(newImagePath);
                    }
                    catch
                    {
                        // Giữ lại lỗi lưu hồ sơ ban đầu.
                    }
                }

                throw;
            }

            TempData["ProfileSuccess"] =
                "Đã cập nhật hồ sơ của bạn.";

            return RedirectToAction(nameof(Index));
        }

        // =====================================================
        // DANH SÁCH HỒ SƠ SINH VIÊN
        // Các tài khoản đã đăng nhập được xem.
        // =====================================================
        [HttpGet]
        public async Task<IActionResult> Candidates(string? keyword)
        {
            if (!await CurrentUserIsActiveAsync())
            {
                return Forbid();
            }

            keyword = keyword?.Trim();

            var query = _context.AlumniProfiles
                .AsNoTracking()
                .Include(x => x.User)
                .Where(x =>
                    x.User.IsActive &&
                    x.User.UserRoles.Any(
                        r => r.Role.RoleName == "Alumni"));

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(x =>
                    x.User.FullName.Contains(keyword) ||
                    (x.CurrentJobTitle != null &&
                     x.CurrentJobTitle.Contains(keyword)));
            }

            ViewBag.Keyword = keyword;

            var profiles = await query
                .OrderByDescending(x => x.ProfileUpdatedAt)
                .ThenBy(x => x.User.FullName)
                .ToListAsync();

            return View(profiles);
        }

        // =====================================================
        // CHI TIẾT HỒ SƠ: CHỈ XEM
        // id là AlumniId, không phải UserId.
        // =====================================================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            if (!await CurrentUserIsActiveAsync())
            {
                return Forbid();
            }

            var profile = await _context.AlumniProfiles
                .AsNoTracking()
                .Include(x => x.User)
                .SingleOrDefaultAsync(x =>
                    x.AlumniId == id &&
                    x.User.IsActive &&
                    x.User.UserRoles.Any(
                        r => r.Role.RoleName == "Alumni"));

            if (profile == null)
            {
                return NotFound("Không tìm thấy hồ sơ.");
            }

            // View chỉ hiện nút sửa khi người xem là chủ hồ sơ.
            var isOwner = GetCurrentUserId() == profile.UserId &&
                User.IsInRole("Alumni");

            ViewBag.IsOwner = isOwner;

            if (isOwner)
            {
                ViewData["GraduationState"] = await GetGraduationStateAsync(profile.UserId);
            }

            return View(profile);
        }

        // =====================================================
        // HIỂN THỊ ẢNH ĐẠI DIỆN
        // Có alumniId: ảnh của hồ sơ đang xem.
        // Không có alumniId: ảnh của chính tài khoản.
        // =====================================================
        [HttpGet]
        public async Task<IActionResult> Avatar(int? alumniId)
        {
            if (!await CurrentUserIsActiveAsync())
            {
                return Forbid();
            }

            string? avatarName;

            if (alumniId.HasValue)
            {
                avatarName = await _context.AlumniProfiles
                    .AsNoTracking()
                    .Where(x =>
                        x.AlumniId == alumniId.Value &&
                        x.User.IsActive &&
                        x.User.UserRoles.Any(
                            r => r.Role.RoleName == "Alumni"))
                    .Select(x => x.User.AvatarUrl)
                    .SingleOrDefaultAsync();
            }
            else
            {
                var userId = GetCurrentUserId();

                avatarName = await _context.Users
                    .AsNoTracking()
                    .Where(x =>
                        x.UserId == userId &&
                        x.IsActive)
                    .Select(x => x.AvatarUrl)
                    .SingleOrDefaultAsync();
            }

            if (string.IsNullOrWhiteSpace(avatarName) ||
                avatarName != Path.GetFileName(avatarName))
            {
                return NotFound();
            }

            var extension = Path.GetExtension(avatarName)
                .ToLowerInvariant();

            if (extension != ".jpg" && extension != ".png")
            {
                return NotFound();
            }

            var path = Path.Combine(AvatarFolder, avatarName);

            if (!System.IO.File.Exists(path))
            {
                return NotFound();
            }

            Response.Headers["Cache-Control"] = "no-store";

            return PhysicalFile(
                path,
                extension == ".png" ? "image/png" : "image/jpeg");
        }

        private Task<int> GetGraduationStateAsync(int userId)
        {
            return _context.Database.SqlQuery<int>($"""
                SELECT CASE
                    WHEN IsGraduationVerified = 1 OR GraduationConfirmedAtUtc IS NOT NULL
                        THEN 2
                    ELSE 1
                END AS [Value]
                FROM dbo.AlumniProfiles
                WHERE UserId = {userId}
                """).SingleOrDefaultAsync();
        }

        // Kiểm tra tài khoản người xem còn hoạt động.
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

        // Kiểm tra dấu hiệu định dạng trong nội dung file.
        private static string? GetImageExtension(byte[] bytes)
        {
            if (bytes.Length >= 8 &&
                bytes[0] == 0x89 &&
                bytes[1] == 0x50 &&
                bytes[2] == 0x4E &&
                bytes[3] == 0x47 &&
                bytes[4] == 0x0D &&
                bytes[5] == 0x0A &&
                bytes[6] == 0x1A &&
                bytes[7] == 0x0A)
            {
                return ".png";
            }

            if (bytes.Length >= 3 &&
                bytes[0] == 0xFF &&
                bytes[1] == 0xD8 &&
                bytes[2] == 0xFF)
            {
                return ".jpg";
            }

            return null;
        }
        [HttpGet]
        [Authorize(Roles = "Alumni")]
        public async Task<IActionResult> MyProfile()
        {
            var userId = GetCurrentUserId();

            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            if (!await CurrentUserIsActiveAsync())
            {
                return Forbid();
            }

            var alumniId = await _context.AlumniProfiles
                .AsNoTracking()
                .Where(x => x.UserId == userId.Value)
                .Select(x => (int?)x.AlumniId)
                .SingleOrDefaultAsync();

            // Chưa có hồ sơ thì mở form tạo lần đầu.
            if (!alumniId.HasValue)
            {
                return RedirectToAction(nameof(Index));
            }

            // Đã có hồ sơ thì mở trang xem chi tiết.
            return RedirectToAction(
                nameof(Details),
                new { id = alumniId.Value });
        }
    }

}
