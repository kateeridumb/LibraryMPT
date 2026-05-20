using LibraryMPT.Extensions;
using LibraryMPT.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;

namespace LibraryMPT.Controllers
{
    [Authorize(Roles = "InstitutionRepresentative")]
    public class InstitutionRepresentativeController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public InstitutionRepresentativeController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<IActionResult> Index()
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            var data = await api.GetFromJsonAsync<InstitutionIndexResponse>("api/institution/index")
                ?? new InstitutionIndexResponse { HasFaculty = false, ErrorMessage = "Ошибка API." };

            if (!data.HasFaculty)
                TempData["Error"] = data.ErrorMessage;

            ViewBag.Faculty = data.Faculty;
            ViewBag.FacultyId = data.FacultyId;
            ViewBag.ActiveSubscription = data.ActiveSubscription;
            ViewBag.TotalStudents = data.TotalStudents;
            ViewBag.TotalDownloads = data.TotalDownloads;
            ViewBag.TotalReads = data.TotalReads;
            return View();
        }

        public async Task<IActionResult> Subscriptions()
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            var data = await api.GetFromJsonAsync<InstitutionSubscriptionsResponse>("api/institution/subscriptions-with-categories")
                ?? new InstitutionSubscriptionsResponse { HasFaculty = false, ErrorMessage = "Ошибка API." };

            if (!data.HasFaculty)
            {
                TempData["Error"] = data.ErrorMessage;
                ViewBag.Faculty = null;
                ViewBag.MySubscriptions = new List<Subscription>();
                return View(new List<Subscription>());
            }

            ViewBag.Faculty = data.Faculty;
            ViewBag.MySubscriptions = data.MySubscriptions;
            ViewBag.Categories = data.Categories;
            return View(data.Templates);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SelectSubscription(int subscriptionId, int categoryId)
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            ApiCommandResponse? payload = null;
            try
            {
                var response = await api.PostAsJsonAsync("api/institution/select-subscription-with-category", new SelectSubscriptionRequest
                {
                    SubscriptionId = subscriptionId,
                    CategoryId = categoryId
                });

                payload = await response.Content.ReadFromJsonAsync<ApiCommandResponse>();
                if (!response.IsSuccessStatusCode && payload == null)
                {
                    TempData["Error"] = $"Не удалось оформить подписку (HTTP {(int)response.StatusCode}).";
                    return RedirectToAction(nameof(Subscriptions));
                }
            }
            catch (Exception)
            {
                TempData["Error"] = "Не удалось оформить подписку из-за внутренней ошибки. Попробуйте позже.";
                return RedirectToAction(nameof(Subscriptions));
            }

            if (payload?.Success == true)
            {
                TempData["Success"] = payload.Message;
                TempData["ShowContractButton"] = true;
                return RedirectToAction(nameof(Subscriptions));
            }

            TempData["Error"] = payload?.Message ?? "Не удалось оформить подписку.";
            return RedirectToAction(nameof(Subscriptions));
        }

        public async Task<IActionResult> Contract()
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            try
            {
                var response = await api.GetAsync("api/institution/contract/pending");
                if (!response.IsSuccessStatusCode)
                {
                    TempData["Error"] = "Заявка отправлена, но договор пока недоступен. Попробуйте открыть его чуть позже.";
                    return RedirectToAction(nameof(Subscriptions));
                }

                var contract = await response.Content.ReadFromJsonAsync<InstitutionContractResponse>()
                    ?? new InstitutionContractResponse();

                if (string.IsNullOrWhiteSpace(contract.FacultyName))
                {
                    TempData["Error"] = "Заявка отправлена, но договор пока не найден.";
                    return RedirectToAction(nameof(Subscriptions));
                }

                return View(contract);
            }
            catch
            {
                TempData["Error"] = "Заявка отправлена, но договор временно недоступен из-за внутренней ошибки.";
                return RedirectToAction(nameof(Subscriptions));
            }
        }

        public async Task<IActionResult> StudentStatistics(string? search, string? sortBy, string? sortDir)
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            var data = await api.GetFromJsonAsync<InstitutionStudentStatsResponse>(
                $"api/institution/student-statistics?search={Uri.EscapeDataString(search ?? string.Empty)}" +
                $"&sortBy={Uri.EscapeDataString(sortBy ?? string.Empty)}" +
                $"&sortDir={Uri.EscapeDataString(sortDir ?? string.Empty)}")
                ?? new InstitutionStudentStatsResponse { HasFaculty = false, ErrorMessage = "Ошибка API." };

            if (!data.HasFaculty)
            {
                TempData["Error"] = data.ErrorMessage;
                ViewBag.Faculty = null;
                return View(new List<StudentStatsDto>());
            }

            ViewBag.Faculty = data.Faculty;
            ViewBag.Search = data.Search;
            ViewBag.SortBy = data.SortBy;
            ViewBag.SortDir = data.SortDir;
            return View(data.Students);
        }

        public async Task<IActionResult> BookStatistics(string? search, string? sortBy, string? sortDir)
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            var data = await api.GetFromJsonAsync<InstitutionBookStatsResponse>(
                $"api/institution/book-statistics?search={Uri.EscapeDataString(search ?? string.Empty)}" +
                $"&sortBy={Uri.EscapeDataString(sortBy ?? string.Empty)}" +
                $"&sortDir={Uri.EscapeDataString(sortDir ?? string.Empty)}")
                ?? new InstitutionBookStatsResponse { HasFaculty = false, ErrorMessage = "Ошибка API." };

            if (!data.HasFaculty)
            {
                TempData["Error"] = data.ErrorMessage;
                return RedirectToAction("Index", "Home");
            }

            ViewBag.Faculty = data.Faculty;
            ViewBag.Search = data.Search;
            ViewBag.SortBy = data.SortBy;
            ViewBag.SortDir = data.SortDir;
            return View(data.BookStats);
        }
    }
}

