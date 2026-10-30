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

            return View(model);
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
                return View(model);
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

                if (user.AlumniProfile == null)
                {
                    user.AlumniProfile = new AlumniProfile
                    {
                        UserId = user.UserId
                    };
                }

                user.AlumniProfile.DateOfBirth = model.DateOfBirth;
                user.AlumniProfile.GraduationYear =
                    model.GraduationYear;

                user.AlumniProfile.Bio =
                    string.IsNullOrWhiteSpace(model.Bio)
                        ? null
                        : model.Bio.Trim();

                user.AlumniProfile.ProfileUpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();
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
            ViewBag.IsOwner =
                GetCurrentUserId() == profile.UserId &&
                User.IsInRole("Alumni");

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