using System.Security.Claims;
using AlumniJobConnect.Models;
using AlumniJobConnect.Features.EmploymentSurveys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class EmploymentSurveysController : Controller
{
    private readonly AlumniJobConnectContext _db;
    private readonly SurveyStore _store;
    public EmploymentSurveysController(AlumniJobConnectContext db) { _db=db; _store=new(db); }
    private async Task<int?> ActiveAdminAsync(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        return await _db.Users.AsNoTracking().AnyAsync(u => u.UserId==id && u.IsActive
            && u.UserRoles.Any(r => r.Role.RoleName=="Admin"), ct) ? id : null;
    }
    private async Task FillCoursesAsync(SurveyForm form, CancellationToken ct) =>
        form.Courses=await _db.AcademicCourses.AsNoTracking().OrderByDescending(c=>c.StartYear).ThenBy(c=>c.CourseName).ToListAsync(ct);
    private void ValidateForm(SurveyForm input)
    {
        input.Title=input.Title?.Trim() ?? "";
        input.Description=input.Description?.Trim();
        if (input.Title.Length == 0) ModelState.AddModelError("Title","Nhập tên đợt khảo sát.");
        if (input.StartsOn.Year < 2000 || input.EndsOn.Year > 2100 || input.StartsOn > input.EndsOn)
            ModelState.AddModelError("EndsOn","Ngày kết thúc phải từ ngày bắt đầu; thời gian thuộc năm 2000–2100.");
    }
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        if (!(await ActiveAdminAsync(ct)).HasValue) return Forbid();
        page=Math.Clamp(page,1,10000);
        var rows=await _store.AdminListAsync(page,ct);
        return View(new SurveyListPage { Items=rows.Take(15).ToList(), Page=page, HasMore=rows.Count>15 });
    }
    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        if (!(await ActiveAdminAsync(ct)).HasValue) return Forbid();
        var form=new SurveyForm(); await FillCoursesAsync(form,ct); return View("Form",form);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Title,Description,StartsOn,EndsOn,TargetCourseId")] SurveyForm input, CancellationToken ct)
    {
        var adminId=await ActiveAdminAsync(ct); if (!adminId.HasValue) return Forbid();
        ValidateForm(input);
        if (input.TargetCourseId.HasValue && !await _db.AcademicCourses.AnyAsync(c=>c.AcademicCourseId==input.TargetCourseId,ct))
            ModelState.AddModelError("TargetCourseId","Khóa học không tồn tại.");
        if (ModelState.IsValid)
        {
            try {
                var id=await _store.CreateAsync(input,adminId.Value,
                    surveyId=>Url.Action("Answer","EmploymentSurveys",new { area="",id=surveyId })!,ct);
                TempData["SurveySuccess"]="Đã tạo đợt, chốt danh sách và gửi thông báo trong website.";
                return RedirectToAction(nameof(Results),new { id });
            } catch (SurveyOperationException ex) { ModelState.AddModelError("",ex.Message); }
        }
        await FillCoursesAsync(input,ct); return View("Form",input);
    }
    [HttpGet]
    public async Task<IActionResult> Edit(int id,CancellationToken ct)
    {
        if (!(await ActiveAdminAsync(ct)).HasValue) return Forbid();
        var s=await _store.GetAsync(id,ct); if (s==null) return NotFound();
        return View("Form",new SurveyForm { SurveyId=s.SurveyId,Version=s.Version,Title=s.Title,
            Description=s.Description,StartsOn=s.StartsOn,EndsOn=s.EndsOn,TargetCourseName=s.TargetCourseName });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id,[Bind("SurveyId,Version,Title,Description,StartsOn,EndsOn")] SurveyForm input,CancellationToken ct)
    {
        if (!(await ActiveAdminAsync(ct)).HasValue) return Forbid();
        if (id!=input.SurveyId) return BadRequest();
        var s=await _store.GetAsync(id,ct); if (s==null) return NotFound();
        input.TargetCourseName=s.TargetCourseName; ValidateForm(input);
        if (ModelState.IsValid) {
            try { await _store.EditAsync(input,ct); TempData["SurveySuccess"]="Đã cập nhật đợt khảo sát.";
                return RedirectToAction(nameof(Results),new { id }); }
            catch(SurveyOperationException ex) { ModelState.AddModelError("",ex.Message); }
        }
        return View("Form",input);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetClosed(int id,int version,bool closed,CancellationToken ct)
    {
        if (!(await ActiveAdminAsync(ct)).HasValue) return Forbid();
        try { await _store.SetClosedAsync(id,version,closed,ct);
            TempData["SurveySuccess"]=closed ? "Đã đóng đợt khảo sát." : "Đã bỏ đóng thủ công. Chỉ nhận trả lời trong thời gian đã đặt."; }
        catch(SurveyOperationException ex) { TempData["SurveyError"]=ex.Message; }
        return RedirectToAction(nameof(Results),new { id });
    }
    [HttpGet]
    public async Task<IActionResult> Results(int id,string filter="all",int page=1,CancellationToken ct=default)
    {
        if (!(await ActiveAdminAsync(ct)).HasValue) return Forbid();
        var survey=await _store.GetAsync(id,ct); if(survey==null) return NotFound();
        var rows=await _store.ParticipantsAsync(id,ct);
        if(filter is not ("all" or "responded" or "missing" or "support")) filter="all";
        var filtered=rows.Where(p=>filter switch { "responded"=>p.HasResponded,"missing"=>!p.HasResponded,
            "support"=>p.HasResponded && p.NeedsSupport==true,_=>true }).ToList();
        var pages=Math.Max(1,(int)Math.Ceiling(filtered.Count/20m)); page=Math.Clamp(page,1,pages);
        return View(new SurveyResultsPage { Survey=survey,Participants=rows,
            FilteredParticipants=filtered.Skip((page-1)*20).Take(20).ToList(),Filter=filter,
            Page=page,TotalPages=pages,FilteredCount=filtered.Count });
    }
    [HttpGet]
    public async Task<IActionResult> Response(int id,int userId,CancellationToken ct)
    {
        if (!(await ActiveAdminAsync(ct)).HasValue) return Forbid();
        var survey=await _store.GetAsync(id,ct); if(survey==null) return NotFound();
        var participant=(await _store.ParticipantsAsync(id,ct)).SingleOrDefault(p=>p.UserId==userId);
        if(participant==null || !participant.HasResponded) return NotFound();
        ViewBag.Survey=survey; return View(participant);
    }
}
