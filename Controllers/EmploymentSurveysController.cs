using System.Security.Claims;
using AlumniJobConnect.Models;
using AlumniJobConnect.Features.EmploymentSurveys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlumniJobConnect.Controllers;

[Authorize(Roles = "Alumni,Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class EmploymentSurveysController : Controller
{
    private readonly AlumniJobConnectContext _db;
    private readonly SurveyStore _store;
    public EmploymentSurveysController(AlumniJobConnectContext db) { _db=db; _store=new(db); }
    private async Task<int?> ActiveUserAsync(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)) return null;
        return await _db.Users.AsNoTracking().AnyAsync(u=>u.UserId==id && u.IsActive && u.UserRoles.Any(r=>r.Role.RoleName=="Alumni" || r.Role.RoleName=="Admin"),ct) ? id : null;
    }
    [HttpGet]
    public async Task<IActionResult> Index(int page=1,CancellationToken ct=default)
    {
        var userId=await ActiveUserAsync(ct); if(!userId.HasValue) return Forbid();
        page=Math.Clamp(page,1,10000); var rows=await _store.UserListAsync(userId.Value,page,ct);
        return View(new SurveyListPage { Items=rows.Take(15).ToList(),Page=page,HasMore=rows.Count>15 });
    }
    private static ResponseInput InputFrom(ResponseRow? r,int id) => r==null ? new() { SurveyId=id } : new() {
        SurveyId=id,Version=r.Version,EmploymentStatus=r.EmploymentStatus,CompanyName=r.CompanyName,
        JobTitle=r.JobTitle,IsMajorRelated=r.IsMajorRelated,SalaryRange=r.SalaryRange,NeedsSupport=r.NeedsSupport,Notes=r.Notes
    };
    [HttpGet]
    public async Task<IActionResult> Answer(int id,CancellationToken ct)
    {
        var userId=await ActiveUserAsync(ct); if(!userId.HasValue) return Forbid();
        if(!await _store.IsRecipientAsync(id,userId.Value,ct)) return NotFound("Bạn không thuộc danh sách của đợt khảo sát này.");
        var survey=await _store.GetAsync(id,ct); if(survey==null) return NotFound();
        var response=await _store.ResponseAsync(id,userId.Value,ct);
        return View(new SurveyAnswerPage { Survey=survey,Input=InputFrom(response,id),UpdatedAt=response?.UpdatedAt });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Answer(int id,[Bind(Prefix="Input")] ResponseInput input,CancellationToken ct)
    {
        var userId=await ActiveUserAsync(ct); if(!userId.HasValue) return Forbid();
        if(id!=input.SurveyId) return BadRequest();
        if(!await _store.IsRecipientAsync(id,userId.Value,ct)) return NotFound();
        var survey=await _store.GetAsync(id,ct); if(survey==null) return NotFound();
        if(!survey.IsOpen) ModelState.AddModelError("","Đợt khảo sát chưa mở, đã đóng hoặc hết hạn.");
        input.CompanyName=input.CompanyName?.Trim(); input.JobTitle=input.JobTitle?.Trim(); input.Notes=input.Notes?.Trim();
        input.SalaryRange=string.IsNullOrWhiteSpace(input.SalaryRange) ? null : input.SalaryRange;
        if(!SurveyLabels.Statuses.ContainsKey(input.EmploymentStatus ?? ""))
            ModelState.AddModelError("Input.EmploymentStatus","Chọn tình trạng việc làm hợp lệ.");
        if(input.EmploymentStatus=="Employed") {
            if(string.IsNullOrWhiteSpace(input.CompanyName)) ModelState.AddModelError("Input.CompanyName","Nhập tên công ty hoặc đơn vị đang làm việc.");
            if(string.IsNullOrWhiteSpace(input.JobTitle)) ModelState.AddModelError("Input.JobTitle","Nhập vị trí công việc.");
            if(!input.IsMajorRelated.HasValue) ModelState.AddModelError("Input.IsMajorRelated","Chọn công việc có phù hợp ngành học không.");
            if(input.SalaryRange!=null && !SurveyLabels.Salaries.ContainsKey(input.SalaryRange)) ModelState.AddModelError("Input.SalaryRange","Khoảng thu nhập không hợp lệ.");
        } else { input.CompanyName=null;input.JobTitle=null;input.IsMajorRelated=null;input.SalaryRange=null; }
        if(ModelState.IsValid) {
            try { await _store.SaveResponseAsync(input,userId.Value,ct);TempData["SurveySuccess"]="Đã lưu câu trả lời. Bạn có thể sửa khi đợt khảo sát còn mở.";
                return RedirectToAction(nameof(Answer),new { id }); }
            catch(SurveyOperationException ex) { ModelState.AddModelError("",ex.Message); }
        }
        return View(new SurveyAnswerPage { Survey=survey,Input=input });
    }
}
