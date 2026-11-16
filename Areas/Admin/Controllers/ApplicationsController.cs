using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Authorization;

namespace AlumniJobConnect.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class ApplicationsController : Controller
    {
        private readonly AlumniJobConnectContext _context;

        public ApplicationsController(AlumniJobConnectContext context)
        {
            _context = context;
        }

        // GET: Admin/Applications
        public async Task<IActionResult> Index()
        {
            var applications = await _context.Applications
                .AsNoTracking()
                .Include(a => a.Alumni)
                    .ThenInclude(a => a.User)
                .Include(a => a.ApplicationStatus)
                .Include(a => a.JobPost)
                    .ThenInclude(j => j.Company)
                .OrderByDescending(a => a.AppliedAt)
                .ThenByDescending(a => a.ApplicationId)
                .ToListAsync();

            // Lấy tên trạng thái theo cấu hình của EF,
            // tránh đoán tên thuộc tính trong model ApplicationStatus.
            var statusEntity = _context.Model
                .FindEntityType(typeof(ApplicationStatus));

            var statusNameProperty =
                statusEntity?.FindProperty("StatusName")
                ?? statusEntity?.FindProperty("ApplicationStatusName");

            var statusNames = new Dictionary<int, string>();

            if (statusNameProperty != null
                && statusNameProperty.ClrType == typeof(string))
            {
                var propertyName = statusNameProperty.Name;

                var statusRows = await _context.ApplicationStatuses
     .AsNoTracking()
     .Select(s => new
     {
         Id = s.ApplicationStatusId,
         Name = EF.Property<string>(s, propertyName)
     })
     .ToListAsync();

                statusNames = statusRows.ToDictionary(
                    s => s.Id,
                    s => s.Name ?? $"Trạng thái #{s.Id}");
            }

            ViewData["StatusNames"] = statusNames;

            return View(applications);
        }

        // GET: Admin/Applications/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var application = await _context.Applications
                .Include(a => a.Alumni)
                .Include(a => a.ApplicationStatus)
                .Include(a => a.JobPost)
                .FirstOrDefaultAsync(m => m.ApplicationId == id);
            if (application == null)
            {
                return NotFound();
            }

            return View(application);
        }

        // GET: Admin/Applications/Create
        public IActionResult Create()
        {
            ViewData["AlumniId"] = new SelectList(_context.AlumniProfiles, "AlumniId", "AlumniId");
            ViewData["ApplicationStatusId"] = new SelectList(_context.ApplicationStatuses, "ApplicationStatusId", "ApplicationStatusId");
            ViewData["JobPostId"] = new SelectList(_context.JobPosts, "JobPostId", "JobPostId");
            return View();
        }

        // POST: Admin/Applications/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ApplicationId,JobPostId,AlumniId,ApplicationStatusId,Cvurl,CoverLetter,AppliedAt,UpdatedAt,EmployerNote,InterviewAt,InterviewLocation,InterviewNote,InterviewResponse,InterviewDeclineReason,InterviewRespondedAt")] Application application)
        {
            if (ModelState.IsValid)
            {
                _context.Add(application);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["AlumniId"] = new SelectList(_context.AlumniProfiles, "AlumniId", "AlumniId", application.AlumniId);
            ViewData["ApplicationStatusId"] = new SelectList(_context.ApplicationStatuses, "ApplicationStatusId", "ApplicationStatusId", application.ApplicationStatusId);
            ViewData["JobPostId"] = new SelectList(_context.JobPosts, "JobPostId", "JobPostId", application.JobPostId);
            return View(application);
        }

        // GET: Admin/Applications/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var application = await _context.Applications.FindAsync(id);
            if (application == null)
            {
                return NotFound();
            }
            ViewData["AlumniId"] = new SelectList(_context.AlumniProfiles, "AlumniId", "AlumniId", application.AlumniId);
            ViewData["ApplicationStatusId"] = new SelectList(_context.ApplicationStatuses, "ApplicationStatusId", "ApplicationStatusId", application.ApplicationStatusId);
            ViewData["JobPostId"] = new SelectList(_context.JobPosts, "JobPostId", "JobPostId", application.JobPostId);
            return View(application);
        }

        // POST: Admin/Applications/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ApplicationId,JobPostId,AlumniId,ApplicationStatusId,Cvurl,CoverLetter,AppliedAt,UpdatedAt,EmployerNote,InterviewAt,InterviewLocation,InterviewNote,InterviewResponse,InterviewDeclineReason,InterviewRespondedAt")] Application application)
        {
            if (id != application.ApplicationId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(application);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ApplicationExists(application.ApplicationId))
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
            ViewData["AlumniId"] = new SelectList(_context.AlumniProfiles, "AlumniId", "AlumniId", application.AlumniId);
            ViewData["ApplicationStatusId"] = new SelectList(_context.ApplicationStatuses, "ApplicationStatusId", "ApplicationStatusId", application.ApplicationStatusId);
            ViewData["JobPostId"] = new SelectList(_context.JobPosts, "JobPostId", "JobPostId", application.JobPostId);
            return View(application);
        }

        // GET: Admin/Applications/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var application = await _context.Applications
                .Include(a => a.Alumni)
                .Include(a => a.ApplicationStatus)
                .Include(a => a.JobPost)
                .FirstOrDefaultAsync(m => m.ApplicationId == id);
            if (application == null)
            {
                return NotFound();
            }

            return View(application);
        }

        // POST: Admin/Applications/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var application = await _context.Applications.FindAsync(id);
            if (application != null)
            {
                _context.Applications.Remove(application);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool ApplicationExists(int id)
        {
            return _context.Applications.Any(e => e.ApplicationId == id);
        }
    }
}
