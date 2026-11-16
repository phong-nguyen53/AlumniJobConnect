using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AlumniJobConnect.Models;


namespace AlumniJobConnect.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class JobCategoriesController : Controller
    {
        private readonly AlumniJobConnectContext _context;

        public JobCategoriesController(AlumniJobConnectContext context)
        {
            _context = context;
        }

        // GET: Admin/JobCategories
        public async Task<IActionResult> Index()
        {
            var categories = await _context.JobCategories
                .AsNoTracking()
                .OrderBy(x => x.CategoryName)
                .ToListAsync();

            return View(categories);
        }

        // GET: Admin/JobCategories/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var jobCategory = await _context.JobCategories
                .FirstOrDefaultAsync(m => m.JobCategoryId == id);
            if (jobCategory == null)
            {
                return NotFound();
            }

            return View(jobCategory);
        }

        // GET: Admin/JobCategories/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View(new JobCategory());
        }

        // POST: Admin/JobCategories/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
           [Bind("CategoryName,Description")] JobCategory jobCategory)
        {
            jobCategory.CategoryName =
                (jobCategory.CategoryName ?? "").Trim();

            jobCategory.Description =
                jobCategory.Description?.Trim();

            if (string.IsNullOrWhiteSpace(jobCategory.CategoryName))
            {
                ModelState.AddModelError(
                    nameof(jobCategory.CategoryName),
                    "Vui lòng nhập tên ngành nghề.");
            }

            if (!ModelState.IsValid)
            {
                return View(jobCategory);
            }

            var exists = await _context.JobCategories
                .AnyAsync(x => x.CategoryName == jobCategory.CategoryName);

            if (exists)
            {
                ModelState.AddModelError(
                    nameof(jobCategory.CategoryName),
                    "Tên ngành nghề đã tồn tại.");

                return View(jobCategory);
            }

            try
            {
                var category = new JobCategory
                {
                    CategoryName = jobCategory.CategoryName,
                    Description = jobCategory.Description
                };

                _context.JobCategories.Add(category);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Thêm ngành nghề thành công.";

                return RedirectToAction(
                    "Index",
                    "JobCategories",
                    new { area = "Admin" });
            }
            catch (DbUpdateException ex)
            {
                // Tạm hiển thị lỗi chi tiết khi kiểm tra trên localhost.
                ModelState.AddModelError(
                    "",
                    "Không lưu được: " + ex.GetBaseException().Message);

                return View(jobCategory);
            }
        }
        // GET: Admin/JobCategories/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var jobCategory = await _context.JobCategories.FindAsync(id);
            if (jobCategory == null)
            {
                return NotFound();
            }
            return View(jobCategory);
        }

        // POST: Admin/JobCategories/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("JobCategoryId,CategoryName,Description")] JobCategory jobCategory)
        {
            if (id != jobCategory.JobCategoryId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(jobCategory);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!JobCategoryExists(jobCategory.JobCategoryId))
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
            return View(jobCategory);
        }

        // GET: Admin/JobCategories/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var jobCategory = await _context.JobCategories
                .FirstOrDefaultAsync(m => m.JobCategoryId == id);
            if (jobCategory == null)
            {
                return NotFound();
            }

            return View(jobCategory);
        }

        // POST: Admin/JobCategories/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var jobCategory = await _context.JobCategories.FindAsync(id);
            if (jobCategory != null)
            {
                _context.JobCategories.Remove(jobCategory);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool JobCategoryExists(int id)
        {
            return _context.JobCategories.Any(e => e.JobCategoryId == id);
        }
    }
}
