using LibraryMPT.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;

namespace LibraryMPT.Controllers
{
    public class HomeController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IWebHostEnvironment _env;

        public HomeController(IHttpClientFactory httpClientFactory, IWebHostEnvironment env)
        {
            _httpClientFactory = httpClientFactory;
            _env = env;
        }

        public async Task<IActionResult> Index()
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            var data = await api.GetFromJsonAsync<HomeIndexResponse>("api/home/index")
                ?? new HomeIndexResponse();

            ViewBag.TotalUsers = data.TotalUsers;
            ViewBag.TotalBooks = data.TotalBooks;
            ViewBag.Downloads = data.Downloads;
            ViewBag.Availability = data.Availability;
            if (data.IsTwoFactorEnabled.HasValue)
            {
                ViewBag.IsTwoFactorEnabled = data.IsTwoFactorEnabled.Value;
            }

            var root = _env.WebRootPath;
            if (!string.IsNullOrEmpty(root))
            {
                var apk = Path.Combine(root, "downloads", "library-mpt-reader.apk");
                ViewBag.MobileApkAvailable = System.IO.File.Exists(apk);
            }
            else
            {
                ViewBag.MobileApkAvailable = false;
            }

            return View();
        }

        /// <summary>
        /// PDF руководства по роли из wwwroot/guides (URL без кириллицы). Открывается в отдельной вкладке — см. главную.
        /// </summary>
        [Authorize]
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult UserGuide()
        {
            string? fileName = null;
            if (User.IsInRole("Admin"))
                fileName = "Руководство пользователя для администратора.pdf";
            else if (User.IsInRole("Librarian"))
                fileName = "Руководство пользователя для библиотекаря.pdf";
            else if (User.IsInRole("Student"))
                fileName = "Руководство пользователя для студента.pdf";
            else if (User.IsInRole("InstitutionRepresentative"))
                fileName = "Руководство пользователя для представителя уч. заведения.pdf";

            if (string.IsNullOrEmpty(fileName))
                return NotFound();

            var root = _env.WebRootPath;
            if (string.IsNullOrEmpty(root))
                return NotFound();

            var path = Path.Combine(root, "guides", fileName);
            if (!System.IO.File.Exists(path))
                return NotFound();

            return PhysicalFile(path, "application/pdf");
        }

        public IActionResult AccessDenied()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult Error(int? statusCode = null, string? message = null)
        {
            var status = statusCode ?? 500;
            ViewBag.StatusCode = status;
            ViewBag.RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            ViewBag.ErrorTitle = status switch
            {
                404 => "Страница не найдена",
                403 => "Доступ запрещен",
                500 => "Внутренняя ошибка сервера",
                _ => "Произошла ошибка"
            };
            ViewBag.ErrorMessage = !string.IsNullOrWhiteSpace(message)
                ? message
                : (status switch
                {
                    404 => "Запрашиваемая страница не существует.",
                    403 => "У вас нет доступа к этому ресурсу.",
                    500 => "Произошла внутренняя ошибка сервера. Пожалуйста, попробуйте позже.",
                    _ => "Произошла непредвиденная ошибка."
                });
            return View();
        }
    }
}
