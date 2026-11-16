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
    public class MenusController : Controller
    {
        private readonly AlumniJobConnectContext _context;

        public MenusController(AlumniJobConnectContext context)
        {
            _context = context;
        }

        // GET: Admin/Menus
        public async Task<IActionResult> Index()
        {
            var alumniJobConnectContext = _context.Menus.Include(m => m.Parent);
            return View(await alumniJobConnectContext.ToListAsync());
        }

        // GET: Admin/Menus/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var menu = await _context.Menus
                .Include(m => m.Parent)
                .FirstOrDefaultAsync(m => m.MenuId == id);
            if (menu == null)
            {
                return NotFound();
            }

            return View(menu);
        }

        // GET: Admin/Menus/Create
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewData["ParentId"] = new SelectList(
                await _context.Menus
                    .AsNoTracking()
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.MenuName)
                    .ToListAsync(),
                "MenuId",
                "MenuName");

            return View(new AlumniJobConnect.Models.Menu
            {
                IsActive = true
            });
        }

        // POST: Admin/Menus/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("MenuName,MenuUrl,Icon,SortOrder,IsActive,ParentId")]
    AlumniJobConnect.Models.Menu menu)
        {
            menu.MenuName = (menu.MenuName ?? "").Trim();
            menu.MenuUrl = menu.MenuUrl?.Trim();
            menu.Icon = menu.Icon?.Trim();

            // Menu cha được chọn bằng ParentId, không nhập đối tượng Parent.
            ModelState.Remove(nameof(menu.Parent));

            if (string.IsNullOrWhiteSpace(menu.MenuName))
            {
                ModelState.AddModelError(
                    nameof(menu.MenuName),
                    "Vui lòng nhập tên menu.");
            }

            if (menu.SortOrder < 0)
            {
                ModelState.AddModelError(
                    nameof(menu.SortOrder),
                    "Thứ tự hiển thị phải từ 0 trở lên.");
            }

            if (menu.ParentId.HasValue)
            {
                var parentExists = await _context.Menus
                    .AnyAsync(x => x.MenuId == menu.ParentId.Value);

                if (!parentExists)
                {
                    ModelState.AddModelError(
                        nameof(menu.ParentId),
                        "Menu cha không tồn tại.");
                }
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Menus.Add(menu);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = "Thêm menu thành công.";

                    return RedirectToAction(
                        "Index",
                        "Menus",
                        new { area = "Admin" });
                }
                catch (DbUpdateException)
                {
                    ModelState.AddModelError(
                        "",
                        "Chưa lưu được menu. Vui lòng kiểm tra thông tin đã nhập.");
                }
            }

            ViewData["ParentId"] = new SelectList(
                await _context.Menus
                    .AsNoTracking()
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.MenuName)
                    .ToListAsync(),
                "MenuId",
                "MenuName",
                menu.ParentId);

            return View(menu);
        }

        // GET: Admin/Menus/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var menu = await _context.Menus.FindAsync(id);
            if (menu == null)
            {
                return NotFound();
            }
            ViewData["ParentId"] = new SelectList(_context.Menus, "MenuId", "MenuId", menu.ParentId);
            return View(menu);
        }

        // POST: Admin/Menus/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("MenuId,MenuName,MenuUrl,Icon,ParentId,SortOrder,IsActive")] Menu menu)
        {
            if (id != menu.MenuId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(menu);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!MenuExists(menu.MenuId))
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
            ViewData["ParentId"] = new SelectList(_context.Menus, "MenuId", "MenuId", menu.ParentId);
            return View(menu);
        }

        // GET: Admin/Menus/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var menu = await _context.Menus
                .Include(m => m.Parent)
                .FirstOrDefaultAsync(m => m.MenuId == id);
            if (menu == null)
            {
                return NotFound();
            }

            return View(menu);
        }

        // POST: Admin/Menus/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var menu = await _context.Menus.FindAsync(id);
            if (menu != null)
            {
                _context.Menus.Remove(menu);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool MenuExists(int id)
        {
            return _context.Menus.Any(e => e.MenuId == id);
        }
    }
}
