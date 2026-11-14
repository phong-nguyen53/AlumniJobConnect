using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace AlumniJobConnect.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AlumniProfilesController : Controller
    {
        private readonly AlumniJobConnectContext _context;

        public AlumniProfilesController(AlumniJobConnectContext context)
        {
            _context = context;
        }

        // GET: Admin/AlumniProfiles
        public async Task<IActionResult> Index()
        {
            var profiles = await _context.AlumniProfiles
                .AsNoTracking()
                .Include(a => a.AcademicCourse)
                .Include(a => a.Major)
                .Include(a => a.User)
                .OrderBy(a => a.User.FullName)
                .ToListAsync();

            var directoryInfo = await _context.Database
                .SqlQuery<AlumniDirectoryInfo>($"""
            SELECT
                profile.AlumniId,

                CAST(
                    CASE
                        WHEN profile.GraduationConfirmedAtUtc IS NOT NULL
                             OR profile.IsGraduationVerified = 1
                        THEN 1
                        ELSE 0
                    END
                AS bit) AS IsGraduate,

                COALESCE(
                    company.CompanyName,
                    currentWork.CompanyName
                ) AS CompanyName,

                currentWork.JobTitle

            FROM dbo.AlumniProfiles AS profile

            OUTER APPLY (
                SELECT TOP (1)
                    experience.CompanyId,
                    experience.CompanyName,
                    experience.JobTitle
                FROM dbo.WorkExperiences AS experience
                WHERE experience.AlumniId = profile.AlumniId
                  AND experience.IsCurrent = 1
                ORDER BY
                    experience.StartDate DESC,
                    experience.WorkExperienceId DESC
            ) AS currentWork

            LEFT JOIN dbo.Companies AS company
                ON company.CompanyId = currentWork.CompanyId
            """)
                .ToListAsync();

            ViewData["GraduateIds"] = directoryInfo
                .Where(x => x.IsGraduate)
                .Select(x => x.AlumniId)
                .ToHashSet();

            ViewData["CurrentCompanies"] = directoryInfo
                .ToDictionary(
                    x => x.AlumniId,
                    x => x.CompanyName ?? "");

            ViewData["CurrentJobTitles"] = directoryInfo
                .ToDictionary(
                    x => x.AlumniId,
                    x => x.JobTitle ?? "");

            return View(profiles);
        }

        // Đặt trong controller, ngoài action Index.
        // Nếu đã thêm lớp này rồi thì không thêm lần nữa.
        public sealed class AlumniDirectoryInfo
        {
            public int AlumniId { get; set; }

            public bool IsGraduate { get; set; }

            public string? CompanyName { get; set; }

            public string? JobTitle { get; set; }
        }
        // GET: Admin/AlumniProfiles/Details/5
        [HttpGet]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || id <= 0)
            {
                return NotFound();
            }

            var profile = await _context.AlumniProfiles
                .AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.Major)
                .Include(x => x.AcademicCourse)
                .FirstOrDefaultAsync(x => x.AlumniId == id.Value);

            if (profile == null)
            {
                return NotFound();
            }

            return View("Details", profile);
        }

        // GET: Admin/AlumniProfiles/Create
        public sealed class CreateStudentInput
        {
            [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
            public string FullName { get; set; } = "";

            [Required(ErrorMessage = "Vui lòng nhập email.")]
            [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
            public string Email { get; set; } = "";

            [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
            [StringLength(
                128,
                MinimumLength = 8,
                ErrorMessage = "Mật khẩu cần từ 8 đến 128 ký tự.")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "";

            [Required(ErrorMessage = "Vui lòng nhập lại mật khẩu.")]
            [Compare(
                nameof(Password),
                ErrorMessage = "Mật khẩu xác nhận không khớp.")]
            [DataType(DataType.Password)]
            public string ConfirmPassword { get; set; } = "";

            [StringLength(30, ErrorMessage = "Mã sinh viên tối đa 30 ký tự.")]
            public string? StudentCode { get; set; }

            public int? MajorId { get; set; }

            public int? AcademicCourseId { get; set; }

            public short? GraduationYear { get; set; }

            public DateOnly? DateOfBirth { get; set; }

            public string? Gender { get; set; }

            public string? Address { get; set; }

            [Url(ErrorMessage = "Đường dẫn LinkedIn không hợp lệ.")]
            public string? LinkedInUrl { get; set; }

            [StringLength(1000, ErrorMessage = "Giới thiệu tối đa 1.000 ký tự.")]
            public string? Bio { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var input = new CreateStudentInput();

            await LoadStudentListsAsync(input);

            return View("Create", input);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateStudentInput input)
        {
            input.FullName = (input.FullName ?? "").Trim();
            input.Email = (input.Email ?? "").Trim().ToLowerInvariant();

            input.StudentCode = string.IsNullOrWhiteSpace(input.StudentCode)
                ? null
                : input.StudentCode.Trim();

            input.Address = input.Address?.Trim();
            input.LinkedInUrl = input.LinkedInUrl?.Trim();
            input.Bio = input.Bio?.Trim();

            if (string.IsNullOrWhiteSpace(input.FullName))
            {
                ModelState.AddModelError(
                    nameof(input.FullName),
                    "Vui lòng nhập họ tên.");
            }

            if (input.StudentCode != null
                && await _context.AlumniProfiles
                    .AnyAsync(x => x.StudentCode == input.StudentCode))
            {
                ModelState.AddModelError(
                    nameof(input.StudentCode),
                    "Mã sinh viên đã được sử dụng.");
            }

            if (input.MajorId.HasValue
                && !await _context.Majors
                    .AnyAsync(x => x.MajorId == input.MajorId.Value))
            {
                ModelState.AddModelError(
                    nameof(input.MajorId),
                    "Ngành học không tồn tại.");
            }

            if (input.AcademicCourseId.HasValue
                && !await _context.AcademicCourses.AnyAsync(x =>
                    x.AcademicCourseId == input.AcademicCourseId.Value))
            {
                ModelState.AddModelError(
                    nameof(input.AcademicCourseId),
                    "Khóa học không tồn tại.");
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

            if (input.DateOfBirth.HasValue
                && input.DateOfBirth.Value > today)
            {
                ModelState.AddModelError(
                    nameof(input.DateOfBirth),
                    "Ngày sinh không được lớn hơn ngày hiện tại.");
            }

            if (input.GraduationYear.HasValue
                && (input.GraduationYear.Value < 1900
                    || input.GraduationYear.Value > today.Year + 10))
            {
                ModelState.AddModelError(
                    nameof(input.GraduationYear),
                    "Năm tốt nghiệp dự kiến không hợp lệ.");
            }

            if (!string.IsNullOrEmpty(input.Gender)
                && input.Gender != "Nam"
                && input.Gender != "Nữ"
                && input.Gender != "Khác")
            {
                ModelState.AddModelError(
                    nameof(input.Gender),
                    "Giới tính không hợp lệ.");
            }

            // Dự án hiện cho phép trùng email.
            // Chặn trường hợp trùng cả email và mật khẩu,
            // vì phần Login không thể phân biệt hai tài khoản đó.
            if (ModelState.IsValid)
            {
                var existingUsers = await _context.Users
                    .AsNoTracking()
                    .Where(x => x.Email == input.Email)
                    .ToListAsync();

                var passwordHasher =
                    new PasswordHasher<AlumniJobConnect.Models.User>();

                foreach (var existingUser in existingUsers)
                {
                    if (string.IsNullOrWhiteSpace(existingUser.PasswordHash))
                    {
                        continue;
                    }

                    try
                    {
                        var result = passwordHasher.VerifyHashedPassword(
                            existingUser,
                            existingUser.PasswordHash,
                            input.Password);

                        if (result != PasswordVerificationResult.Failed)
                        {
                            ModelState.AddModelError(
                                "",
                                "Đã có tài khoản dùng email và mật khẩu này.");

                            break;
                        }
                    }
                    catch (FormatException)
                    {
                        // Bỏ qua mật khẩu cũ có định dạng không hợp lệ.
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                await LoadStudentListsAsync(input);
                return View("Create", input);
            }

            var role = await _context.Roles
                .SingleOrDefaultAsync(x => x.RoleName == "Alumni");

            if (role == null)
            {
                ModelState.AddModelError(
                    "",
                    "Hệ thống chưa có quyền Alumni. Vui lòng kiểm tra bảng Roles.");

                await LoadStudentListsAsync(input);
                return View("Create", input);
            }

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                var now = DateTime.UtcNow.AddHours(7);

                var user = new AlumniJobConnect.Models.User
                {
                    FullName = input.FullName,
                    Email = input.Email,
                    IsActive = true,
                    CreatedAt = now
                };

                var hasher =
                    new PasswordHasher<AlumniJobConnect.Models.User>();

                user.PasswordHash = hasher.HashPassword(
                    user,
                    input.Password);

                user.UserRoles.Add(new UserRole
                {
                    User = user,
                    Role = role,
                    AssignedAt = now
                });

                user.AlumniProfile = new AlumniProfile
                {
                    User = user,
                    StudentCode = input.StudentCode,
                    MajorId = input.MajorId,
                    AcademicCourseId = input.AcademicCourseId,
                    GraduationYear = input.GraduationYear,
                    DateOfBirth = input.DateOfBirth,
                    Gender = input.Gender,
                    Address = input.Address,
                    LinkedInUrl = input.LinkedInUrl,
                    Bio = input.Bio,
                    ProfileUpdatedAt = now
                };

                _context.Users.Add(user);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["Success"] =
                    "Đã tạo tài khoản và hồ sơ sinh viên. "
                    + "Người dùng có thể đăng nhập bằng email và mật khẩu vừa tạo.";

                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync();
                _context.ChangeTracker.Clear();

                var logger = HttpContext.RequestServices
                    .GetRequiredService<ILogger<AlumniProfilesController>>();

                logger.LogError(
                    ex,
                    "Admin không thể tạo tài khoản sinh viên.");

                var environment = HttpContext.RequestServices
                    .GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>();

                var detail = ex.GetBaseException().Message;
                var message = environment.EnvironmentName == "Development"
                    ? "Lỗi lưu dữ liệu: " + detail
                    : "Chưa tạo được tài khoản. Vui lòng kiểm tra nhật ký lỗi.";

                ModelState.AddModelError("", message);
            }

            await LoadStudentListsAsync(input);

            return View("Create", input);
        }

        private async Task LoadStudentListsAsync(CreateStudentInput input)
        {
            var majors = await _context.Majors
                .AsNoTracking()
                .OrderBy(x => x.MajorId)
                .ToListAsync();

            ViewData["MajorId"] = new SelectList(
                majors,
                "MajorId",
                "MajorId",
                input.MajorId);

            var courses = await _context.AcademicCourses
                .AsNoTracking()
                .OrderBy(x => x.AcademicCourseId)
                .ToListAsync();

            ViewData["AcademicCourseId"] = new SelectList(
                courses,
                "AcademicCourseId",
                "AcademicCourseId",
                input.AcademicCourseId);
        }
        // GET: Admin/AlumniProfiles/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var alumniProfile = await _context.AlumniProfiles.FindAsync(id);
            if (alumniProfile == null)
            {
                return NotFound();
            }
            ViewData["AcademicCourseId"] = new SelectList(_context.AcademicCourses, "AcademicCourseId", "AcademicCourseId", alumniProfile.AcademicCourseId);
            ViewData["MajorId"] = new SelectList(_context.Majors, "MajorId", "MajorId", alumniProfile.MajorId);
            ViewData["UserId"] = new SelectList(_context.Users, "UserId", "UserId", alumniProfile.UserId);
            return View(alumniProfile);
        }

        // POST: Admin/AlumniProfiles/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("AlumniId,UserId,StudentCode,MajorId,AcademicCourseId,GraduationYear,DateOfBirth,Gender,Address,LinkedInUrl,CurrentJobTitle,CurrentCompanyName,EmploymentStatus,Bio,ProfileUpdatedAt")] AlumniProfile alumniProfile)
        {
            if (id != alumniProfile.AlumniId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(alumniProfile);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!AlumniProfileExists(alumniProfile.AlumniId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["AcademicCourseId"] = new SelectList(_context.AcademicCourses, "AcademicCourseId", "AcademicCourseId", alumniProfile.AcademicCourseId);
            ViewData["MajorId"] = new SelectList(_context.Majors, "MajorId", "MajorId", alumniProfile.MajorId);
            ViewData["UserId"] = new SelectList(_context.Users, "UserId", "UserId", alumniProfile.UserId);
            return View(alumniProfile);
        }

        // GET: Admin/AlumniProfiles/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var alumniProfile = await _context.AlumniProfiles
                .Include(a => a.AcademicCourse)
                .Include(a => a.Major)
                .Include(a => a.User)
                .FirstOrDefaultAsync(m => m.AlumniId == id);
            if (alumniProfile == null)
            {
                return NotFound();
            }

            return View(alumniProfile);
        }

        // POST: Admin/AlumniProfiles/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var alumniProfile = await _context.AlumniProfiles.FindAsync(id);
            if (alumniProfile != null)
            {
                _context.AlumniProfiles.Remove(alumniProfile);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool AlumniProfileExists(int id)
        {
            return _context.AlumniProfiles.Any(e => e.AlumniId == id);
        }
    }
}
