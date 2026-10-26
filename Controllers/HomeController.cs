using System.Diagnostics;
using AlumniJobConnect.Models;
using Microsoft.AspNetCore.Mvc;

namespace AlumniJobConnect.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }
        public IActionResult DoanhNghiep()
        {
            return View();
        }
        public IActionResult Alumni()
        {
            return View();
        }
        public IActionResult jobs()
        {
            return View();
        }
        public IActionResult Events()
        {
            return View();
        }
        public IActionResult Profile()
        {
            return View();
        }
        public IActionResult ungtuyen()
        {
            return View();
        }
        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
////khong