using System.Text.Json;
using AlumniJobConnect.Models;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Features.EmploymentSurveys;

// Dùng bảng mới qua SQL tham số hóa, không cần sửa DbContext hoặc đăng ký thêm service.
public sealed class SurveyStore
{
    private readonly AlumniJobConnectContext _db;
    public SurveyStore(AlumniJobConnectContext db) => _db = db;
    public sealed class InsertedSurvey { public int SurveyId { get; set; } }
    public async Task<SurveyRow?> GetAsync(int id, CancellationToken ct, bool locked = false)
    {
        var rows = locked
            ? await _db.Database.SqlQuery<SurveyRow>($"""
                SELECT SurveyId, Title, Description, StartsOn, EndsOn, TargetCourseId,
                    TargetCourseName, CreatedAt, IsClosed, Version
                FROM dbo.EmploymentSurveys WITH(UPDLOCK, HOLDLOCK) WHERE SurveyId = {id}
                """).ToListAsync(ct)
            : await _db.Database.SqlQuery<SurveyRow>($"""
                SELECT SurveyId, Title, Description, StartsOn, EndsOn, TargetCourseId,
                    TargetCourseName, CreatedAt, IsClosed, Version
                FROM dbo.EmploymentSurveys WHERE SurveyId = {id}
                """).ToListAsync(ct);
        return rows.SingleOrDefault();
    }
    public async Task<List<SurveyListRow>> AdminListAsync(int page, CancellationToken ct)
    {
        var skip = (page - 1) * 15;
        return await _db.Database.SqlQuery<SurveyListRow>($"""
            SELECT s.SurveyId, s.Title, s.Description, s.StartsOn, s.EndsOn, s.TargetCourseId,
                s.TargetCourseName, s.CreatedAt, s.IsClosed, s.Version,
                (SELECT COUNT(*) FROM dbo.EmploymentSurveyRecipients p WHERE p.SurveyId=s.SurveyId) AS RecipientCount,
                (SELECT COUNT(*) FROM dbo.EmploymentSurveyResponses r WHERE r.SurveyId=s.SurveyId) AS ResponseCount,
                CAST(0 AS bit) AS HasResponded
            FROM dbo.EmploymentSurveys s ORDER BY s.SurveyId DESC
            OFFSET {skip} ROWS FETCH NEXT 16 ROWS ONLY
            """).ToListAsync(ct);
    }
    public async Task<List<SurveyListRow>> UserListAsync(int userId, int page, CancellationToken ct)
    {
        var skip = (page - 1) * 15;
        return await _db.Database.SqlQuery<SurveyListRow>($"""
            SELECT s.SurveyId, s.Title, s.Description, s.StartsOn, s.EndsOn, s.TargetCourseId,
                s.TargetCourseName, s.CreatedAt, s.IsClosed, s.Version,
                0 AS RecipientCount, 0 AS ResponseCount,
                CAST(CASE WHEN r.UserId IS NULL THEN 0 ELSE 1 END AS bit) AS HasResponded
            FROM dbo.EmploymentSurveys s
            JOIN dbo.EmploymentSurveyRecipients p ON p.SurveyId=s.SurveyId AND p.UserId={userId}
            LEFT JOIN dbo.EmploymentSurveyResponses r ON r.SurveyId=s.SurveyId AND r.UserId=p.UserId
            ORDER BY s.SurveyId DESC OFFSET {skip} ROWS FETCH NEXT 16 ROWS ONLY
            """).ToListAsync(ct);
    }
    public async Task<bool> IsRecipientAsync(int id, int userId, CancellationToken ct) =>
        (await _db.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS Value FROM dbo.EmploymentSurveyRecipients
            WHERE SurveyId={id} AND UserId={userId}
            """).ToListAsync(ct)).Single() == 1;

    public async Task<ResponseRow?> ResponseAsync(int id, int userId, CancellationToken ct) =>
        (await _db.Database.SqlQuery<ResponseRow>($"""
            SELECT SurveyId, UserId, EmploymentStatus, CompanyName, JobTitle,
                IsMajorRelated, SalaryRange, NeedsSupport, Notes, SubmittedAt, UpdatedAt, Version
            FROM dbo.EmploymentSurveyResponses WHERE SurveyId={id} AND UserId={userId}
            """).ToListAsync(ct)).SingleOrDefault();

    public async Task<List<ParticipantRow>> ParticipantsAsync(int id, CancellationToken ct) =>
        await _db.Database.SqlQuery<ParticipantRow>($"""
            SELECT p.UserId, p.FullName, p.Email, p.StudentCode, p.CourseName,
                CAST(CASE WHEN r.UserId IS NULL THEN 0 ELSE 1 END AS bit) AS HasResponded,
                r.EmploymentStatus, r.CompanyName, r.JobTitle, r.IsMajorRelated, r.SalaryRange,
                r.NeedsSupport, r.Notes, r.UpdatedAt
            FROM dbo.EmploymentSurveyRecipients p
            LEFT JOIN dbo.EmploymentSurveyResponses r ON r.SurveyId=p.SurveyId AND r.UserId=p.UserId
            WHERE p.SurveyId={id} ORDER BY p.FullName, p.UserId
            """).ToListAsync(ct);

    public async Task<int> CreateAsync(SurveyForm input, int adminId, Func<int,string> url, CancellationToken ct)
    {
        var year = SurveyLabels.Today.Year;
        var target = input.TargetCourseId;
        // Năm tốt nghiệp trong hồ sơ được ưu tiên; nếu trống mới dùng năm của khóa.
        var recipients = await _db.AlumniProfiles.AsNoTracking()
            .Where(p => p.User.IsActive && p.User.UserRoles.Any(r => r.Role.RoleName == "Alumni")
                && (!target.HasValue || p.AcademicCourseId == target.Value)
                && (p.GraduationYear ?? (p.AcademicCourse != null ? p.AcademicCourse.GraduationYear : null)) != null
                && (p.GraduationYear ?? p.AcademicCourse!.GraduationYear) <= year
                && (p.GraduationYear ?? p.AcademicCourse!.GraduationYear) >= 1900)
            .Select(p => new RecipientRow {
                UserId=p.UserId, AlumniId=p.AlumniId, FullName=p.User.FullName, Email=p.User.Email,
                StudentCode=p.StudentCode, CourseName=p.AcademicCourse == null ? "Chưa cập nhật khóa" : p.AcademicCourse.CourseName,
                GraduationYear=(p.GraduationYear ?? p.AcademicCourse!.GraduationYear)!.Value
            }).ToListAsync(ct);
        if (recipients.Count == 0) throw new SurveyOperationException("Không có cựu sinh viên phù hợp. Kiểm tra khóa, năm tốt nghiệp và tài khoản Alumni đang hoạt động.");
        var courseName = "Tất cả khóa đủ điều kiện";
        if (target.HasValue)
        {
            courseName = await _db.AcademicCourses.Where(c => c.AcademicCourseId == target.Value)
                .Select(c => c.CourseName).SingleOrDefaultAsync(ct)
                ?? throw new SurveyOperationException("Khóa học không còn tồn tại.");
        }
        var now = SurveyLabels.Now;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var inserted = await _db.Database.SqlQuery<InsertedSurvey>($"""
            INSERT INTO dbo.EmploymentSurveys
                (Title, Description, StartsOn, EndsOn, TargetCourseId, TargetCourseName, CreatedByUserId, CreatedAt, IsClosed, Version)
            OUTPUT INSERTED.SurveyId
            VALUES ({input.Title}, {input.Description}, {input.StartsOn}, {input.EndsOn}, {target}, {courseName}, {adminId}, {now}, 0, 1)
            """).ToListAsync(ct);
        var id = inserted.Single().SurveyId;
        var json = JsonSerializer.Serialize(recipients);
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.EmploymentSurveyRecipients(SurveyId, UserId, AlumniId, FullName, Email, StudentCode, CourseName, GraduationYear)
            SELECT {id}, UserId, AlumniId, FullName, Email, StudentCode, CourseName, GraduationYear
            FROM OPENJSON({json}) WITH (
                UserId int '$.UserId', AlumniId int '$.AlumniId', FullName nvarchar(150) '$.FullName',
                Email nvarchar(256) '$.Email', StudentCode nvarchar(30) '$.StudentCode',
                CourseName nvarchar(400) '$.CourseName', GraduationYear smallint '$.GraduationYear')
            """, ct);
        var relatedUrl = url(id);
        _db.Notifications.AddRange(recipients.Select(p => new Notification {
            UserId=p.UserId, Title="Mời tham gia khảo sát việc làm",
            Message=$"{input.Title}. Thời gian: {input.StartsOn:dd/MM/yyyy} đến hết {input.EndsOn:dd/MM/yyyy}.",
            NotificationType="EmploymentSurvey", RelatedUrl=relatedUrl, IsRead=false, CreatedAt=now
        }));
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return id;
    }
    public async Task EditAsync(SurveyForm input, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var survey = await GetAsync(input.SurveyId, ct, true);
        if (survey == null || survey.Version != input.Version)
            throw new SurveyOperationException("Đợt khảo sát đã thay đổi. Hãy tải lại trang để sửa.");
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EmploymentSurveys SET Title={input.Title}, Description={input.Description},
                StartsOn={input.StartsOn}, EndsOn={input.EndsOn}, Version=Version+1
            WHERE SurveyId={input.SurveyId}
            """, ct);
        await tx.CommitAsync(ct);
    }
    public async Task SetClosedAsync(int id, int version, bool closed, CancellationToken ct)
    {
        var changed = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.EmploymentSurveys SET IsClosed={closed}, Version=Version+1
            WHERE SurveyId={id} AND Version={version}
            """, ct);
        if (changed != 1) throw new SurveyOperationException("Đợt khảo sát đã thay đổi. Hãy tải lại trang.");
    }
    public async Task SaveResponseAsync(ResponseInput input, int userId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        // Khóa hàng đợt khảo sát trong lúc kiểm tra hạn và ghi, tránh đóng đồng thời.
        var survey = await GetAsync(input.SurveyId, ct, true);
        if (survey == null || !survey.IsOpen) throw new SurveyOperationException("Đợt khảo sát chưa mở, đã đóng hoặc hết hạn.");
        if (!await IsRecipientAsync(input.SurveyId, userId, ct)) throw new SurveyOperationException("Bạn không thuộc danh sách nhận khảo sát này.");
        var existing = await ResponseAsync(input.SurveyId, userId, ct);
        if ((existing?.Version ?? 0) != input.Version)
            throw new SurveyOperationException("Câu trả lời đã được cập nhật ở cửa sổ khác. Hãy tải lại trang.");
        var now = SurveyLabels.Now;
        if (existing == null)
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.EmploymentSurveyResponses
                    (SurveyId, UserId, EmploymentStatus, CompanyName, JobTitle, IsMajorRelated, SalaryRange,
                     NeedsSupport, Notes, SubmittedAt, UpdatedAt, Version)
                VALUES ({input.SurveyId}, {userId}, {input.EmploymentStatus}, {input.CompanyName}, {input.JobTitle},
                    {input.IsMajorRelated}, {input.SalaryRange}, {input.NeedsSupport}, {input.Notes}, {now}, {now}, 1)
                """, ct);
        else
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.EmploymentSurveyResponses SET EmploymentStatus={input.EmploymentStatus},
                    CompanyName={input.CompanyName}, JobTitle={input.JobTitle}, IsMajorRelated={input.IsMajorRelated},
                    SalaryRange={input.SalaryRange}, NeedsSupport={input.NeedsSupport}, Notes={input.Notes},
                    UpdatedAt={now}, Version=Version+1 WHERE SurveyId={input.SurveyId} AND UserId={userId}
                """, ct);
        await tx.CommitAsync(ct);
    }
}
