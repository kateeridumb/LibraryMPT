using LibraryMPT.Data;
using LibraryMPT.Extensions;
using LibraryMPT.Helpers;
using LibraryMPT.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace LibraryMPT.Controllers
{
    [Authorize(Roles = "Student,Admin,Librarian,InstitutionRepresentative,Guest")]
    public class ClientController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ClientController> _logger;
        private readonly LibraryContext _db;
        private readonly IWebHostEnvironment _environment;

        public ClientController(
            IHttpClientFactory httpClientFactory,
            ILogger<ClientController> logger,
            LibraryContext db,
            IWebHostEnvironment environment)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _db = db;
            _environment = environment;
        }

        public async Task<IActionResult> Index(string? search, int[]? categoryIds)
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            var requestUri = string.IsNullOrWhiteSpace(search)
                ? "api/client/index"
                : $"api/client/index?search={Uri.EscapeDataString(search)}";
            if (categoryIds != null && categoryIds.Length > 0)
            {
                foreach (var id in categoryIds.Where(x => x > 0).Distinct())
                {
                    requestUri += requestUri.Contains('?') ? "&" : "?";
                    requestUri += $"categoryIds={id}";
                }
            }

            ClientIndexResponse data;
            try
            {
                data = await api.GetFromJsonAsync<ClientIndexResponse>(requestUri)
                    ?? new ClientIndexResponse();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Client index API request failed");
                TempData["Error"] = "Не удалось загрузить список книг. Попробуйте еще раз.";
                data = new ClientIndexResponse();
            }

            ViewBag.Categories = data.Categories;
            ViewBag.HasSubscription = data.HasSubscription;
            ViewBag.SubscriptionStatus = data.SubscriptionStatus;
            ViewBag.ReadedBookIds = data.ReadedBookIds;
            ViewBag.PersonalPendingBookIds = data.PersonalPendingBookIds;
            ViewBag.PersonalApprovedBookIds = data.PersonalApprovedBookIds;
            ViewBag.TotalBooks = data.TotalBooks;
            ViewBag.Readed = data.Readed;
            ViewBag.Search = search;
            ViewBag.CategoryIds = categoryIds ?? Array.Empty<int>();

            return View(data.Books);
        }

        /// <summary>Прокси обложки к API; при сбое — файл с диска или заглушка. [AllowAnonymous]: запрос &lt;img&gt; не всегда несёт cookie сеанса.</summary>
        [HttpGet]
        [AllowAnonymous]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> BookCover(int id)
        {
            if (id <= 0)
                return LocalPlaceholderOrNotFound();

            try
            {
                var api = _httpClientFactory.CreateClient("LibraryApi");
                using var response = await api.GetAsync($"api/client/cover/{id}");
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
                    return File(bytes, contentType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Book cover API proxy failed for bookId={BookId}, using local fallback", id);
            }

            var imagePath = await _db.Books.AsNoTracking()
                .Where(b => b.BookID == id)
                .Select(b => b.ImagePath)
                .FirstOrDefaultAsync();

            var normalizedPath = BookCoverWebUrl.NormalizeStoredImagePath(imagePath);
            if (!string.IsNullOrWhiteSpace(normalizedPath))
            {
                var trimmed = normalizedPath;
                if (Uri.TryCreate(trimmed, UriKind.Absolute, out var remote) &&
                    (remote.Scheme == Uri.UriSchemeHttp || remote.Scheme == Uri.UriSchemeHttps))
                    return Redirect(trimmed);

                var full = BookCoverPhysicalFilePaths.ResolveBookImage(_environment, normalizedPath);
                if (!string.IsNullOrWhiteSpace(full) && System.IO.File.Exists(full))
                    return PhysicalFile(full, BookCoverPhysicalFilePaths.GetImageContentType(full));
            }

            return LocalPlaceholderOrNotFound();
        }

        private IActionResult LocalPlaceholderOrNotFound()
        {
            var ph = BookCoverPhysicalFilePaths.ResolvePlaceholder(_environment);
            if (ph != null && System.IO.File.Exists(ph))
                return PhysicalFile(ph, "image/png");
            return NotFound();
        }

        public async Task<IActionResult> BookDetails(int id)
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            ClientBookDetailsResponse? data = null;
            try
            {
                data = await api.GetFromJsonAsync<ClientBookDetailsResponse>($"api/client/book-details/{id}");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Client book-details API request failed for bookId={BookId}", id);
                TempData["Error"] = "Не удалось загрузить информацию о книге.";
                return RedirectToAction(nameof(Index));
            }
            if (data?.Book == null)
                return NotFound();

            ViewBag.HasSubscription = data.HasSubscription;
            ViewBag.CanRead = data.CanRead;
            ViewBag.PersonalRequestStatus = data.PersonalRequestStatus;
            return View(data.Book);
        }

        [HttpPost]
        public async Task<IActionResult> RequestBookAccess(int bookId)
        {
            if (User.IsInRole("Guest"))
                return Forbid();

            if (bookId <= 0)
                return RedirectToAction(nameof(BookDetails), new { id = bookId });

            var api = _httpClientFactory.CreateClient("LibraryApi");
            var response = await api.PostAsJsonAsync("api/client/book-requests", new BookRequestCreateRequest
            {
                BookId = bookId
            });

            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<ApiCommandResponse>();
                if (payload?.Success == true)
                    TempData["Success"] = payload.Message;
                else
                    TempData["Error"] = payload?.Message ?? "Не удалось отправить заявку.";
            }
            else
            {
                TempData["Error"] = "Не удалось отправить заявку. Попробуйте еще раз.";
            }

            return RedirectToAction(nameof(BookDetails), new { id = bookId });
        }

        [HttpPost]
        public async Task<IActionResult> MarkAsRead(int bookId)
        {
            if (User.IsInRole("Guest"))
                return Forbid();
            if (bookId <= 0)
            {
                TempData["Error"] = "Некорректная книга.";
                return RedirectToAction(nameof(Index));
            }

            var userId = User.GetUserId();
            var api = _httpClientFactory.CreateClient("LibraryApi");
            var response = await api.PostAsJsonAsync("api/client/mark-read", new MarkAsReadRequest
            {
                UserId = userId,
                BookId = bookId
            });
            var payload = await response.Content.ReadFromJsonAsync<ApiCommandResponse>();
            if (response.IsSuccessStatusCode && payload?.Success == true)
                TempData["Success"] = "Книга отмечена как прочитанная.";
            else
                TempData["Error"] = payload?.Message ?? "Не удалось отметить книгу как прочитанную.";

            return RedirectToAction(nameof(BookDetails), new { id = bookId });
        }

        [HttpGet]
        public async Task<IActionResult> ReadOnline(int id)
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            ClientReadOnlineResponse? data = null;
            try
            {
                data = await api.GetFromJsonAsync<ClientReadOnlineResponse>($"api/client/read-online/{id}");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Client read-online API request failed for bookId={BookId}", id);
                TempData["Error"] = "Не удалось открыть книгу для чтения.";
                return RedirectToAction(nameof(BookDetails), new { id });
            }
            if (data?.Book == null)
                return NotFound();
            if (string.IsNullOrWhiteSpace(data.FilePath))
            {
                TempData["Error"] = "Файл книги не найден.";
                return RedirectToAction("BookDetails", new { id });
            }
            if (!data.CanRead)
            {
                TempData["Error"] = "Эта книга доступна только по подписке.";
                return RedirectToAction("BookDetails", new { id });
            }

            ViewBag.HasSubscription = data.HasSubscription;
            ViewBag.CanRead = data.CanRead;
            ViewBag.FilePath = data.FilePath;
            ViewBag.FileType = data.FileType;
            ViewBag.FileUrl = data.FileType == "epub" || data.FileType == "fb2"
                ? Url.Action("GetReaderFile", "Client", new { id })
                : Url.Action("GetBookFile", "Client", new { id });
            ViewBag.SavedProgress = data.SavedProgress;

            return View(data.Book);
        }

        public class ReaderErrorDto
        {
            public int BookId { get; set; }
            public string? FileType { get; set; }
            public string? FileUrl { get; set; }
            public string? Stage { get; set; }
            public string? Message { get; set; }
            public string? Detail { get; set; }
            public string? Stack { get; set; }
            public string? ClientDump { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> SaveProgress([FromBody] SaveProgressRequest request)
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            var response = await api.PostAsJsonAsync("api/client/save-progress", request);
            return response.IsSuccessStatusCode ? Ok() : StatusCode((int)response.StatusCode);
        }

        [HttpPost]
        public IActionResult LogReaderError([FromBody] ReaderErrorDto dto)
        {
            _logger.LogWarning(
                "Reader error. BookId={BookId}, FileType={FileType}, FileUrl={FileUrl}, Stage={Stage}, Message={Message}, Detail={Detail}",
                dto.BookId, dto.FileType, dto.FileUrl, dto.Stage, dto.Message, dto.Detail);
            if (!string.IsNullOrWhiteSpace(dto.Stack))
                _logger.LogWarning("Reader error stack (BookId={BookId}, Stage={Stage}): {Stack}", dto.BookId, dto.Stage, dto.Stack);
            if (!string.IsNullOrWhiteSpace(dto.ClientDump))
                _logger.LogWarning("Reader error client dump (BookId={BookId}, Stage={Stage}):\n{ClientDump}", dto.BookId, dto.Stage, dto.ClientDump);
            return Ok();
        }

        [HttpGet]
        public Task<IActionResult> GetBookFile(int id) => ServeBookFileFromDiskAsync(id);

        [HttpGet]
        public Task<IActionResult> GetReaderFile(int id) => ServeBookFileFromDiskAsync(id);

        public async Task<IActionResult> Download(int id)
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            var response = await api.GetAsync($"api/client/download/{id}", HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var payload = await response.Content.ReadFromJsonAsync<ApiCommandResponse>();
                    TempData["Error"] = payload?.Message ?? "Не удалось скачать файл.";
                }
                else if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    TempData["Error"] = "Недостаточно прав для скачивания файла.";
                }
                else if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    TempData["Error"] = "Файл не найден.";
                }
                else
                {
                    TempData["Error"] = $"Ошибка загрузки файла ({(int)response.StatusCode}).";
                }

                return RedirectToAction(nameof(BookDetails), new { id });
            }

            HttpContext.Response.RegisterForDispose(response);
            var stream = await response.Content.ReadAsStreamAsync();
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                           ?? response.Content.Headers.ContentDisposition?.FileName;
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                fileName = fileName.Trim('"');
            }

            var result = string.IsNullOrWhiteSpace(fileName)
                ? File(stream, contentType)
                : File(stream, contentType, fileName);
            result.EnableRangeProcessing = true;
            return result;
        }

        public async Task<IActionResult> Readed(string? search)
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            ClientReadedResponse data;
            try
            {
                data = await api.GetFromJsonAsync<ClientReadedResponse>(
                    $"api/client/readed?search={Uri.EscapeDataString(search ?? string.Empty)}")
                    ?? new ClientReadedResponse();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Readed API request failed");
                TempData["Error"] = "Не удалось загрузить список прочитанных книг.";
                data = new ClientReadedResponse();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Readed loading failed");
                TempData["Error"] = "Не удалось загрузить список прочитанных книг.";
                data = new ClientReadedResponse();
            }

            ViewBag.Search = search;
            ViewBag.IsReadedPage = true;
            return View(data.Books);
        }

        public async Task<IActionResult> Cabinet()
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            ClientCabinetResponse? data = null;
            try
            {
                data = await api.GetFromJsonAsync<ClientCabinetResponse>("api/client/personal-cabinet")
                    ?? new ClientCabinetResponse();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Personal cabinet API request failed");
                TempData["Error"] = "Не удалось загрузить личный кабинет.";
                data = new ClientCabinetResponse();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Personal cabinet loading failed");
                TempData["Error"] = "Не удалось загрузить личный кабинет.";
                data = new ClientCabinetResponse();
            }
            return View(data);
        }

        [HttpGet]
        public async Task<IActionResult> GetBookmarks(int bookId)
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            var bookmarks = await api.GetFromJsonAsync<List<Bookmark>>($"api/client/bookmarks?bookId={bookId}")
                ?? new List<Bookmark>();
            return Json(bookmarks);
        }

        [HttpPost]
        public async Task<IActionResult> AddBookmark([FromBody] BookmarkDto bookmarkDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var api = _httpClientFactory.CreateClient("LibraryApi");
            var response = await api.PostAsJsonAsync("api/client/bookmarks", new ClientBookmarkRequest
            {
                Bookmark = bookmarkDto
            });
            var payload = await ReadApiCommandResponseSafeAsync(response);
            return Json(new { success = response.IsSuccessStatusCode && payload?.Success == true });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteBookmark(int bookmarkId)
        {
            var api = _httpClientFactory.CreateClient("LibraryApi");
            var response = await api.DeleteAsync($"api/client/bookmarks/{bookmarkId}");
            var payload = await ReadApiCommandResponseSafeAsync(response);
            if (payload?.Success != true)
                return NotFound();

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateBookmark([FromBody] BookmarkDto bookmarkDto)
        {
            if (!ModelState.IsValid || bookmarkDto.BookmarkID == 0)
                return BadRequest(ModelState);

            var api = _httpClientFactory.CreateClient("LibraryApi");
            var response = await api.PutAsJsonAsync("api/client/bookmarks", new ClientBookmarkRequest
            {
                Bookmark = bookmarkDto
            });
            var payload = await ReadApiCommandResponseSafeAsync(response);
            if (payload?.Success != true)
                return NotFound();

            return Json(new { success = true });
        }
        private async Task<IActionResult> ServeBookFileFromDiskAsync(int id)
        {
            var userId = User.GetUserId();
            var book = await _db.Database.SqlQuery<BookDownloadDto>($"""
                SELECT
                    bookid AS "BookID",
                    title AS "Title",
                    filepath AS "FilePath",
                    requiressubscription AS "RequiresSubscription"
                FROM books
                WHERE bookid = {id}
            """).AsNoTracking().SingleOrDefaultAsync();

            if (book == null || string.IsNullOrWhiteSpace(book.FilePath))
                return NotFound();

            if (book.RequiresSubscription)
            {
                var facultyId = await _db.Database.SqlQuery<int?>($"""
                    SELECT facultyid AS "Value" FROM users WHERE userid = {userId}
                """).SingleOrDefaultAsync();
                var hasActiveSubscription = false;
                if (facultyId.HasValue)
                {
                    var subCount = await _db.Database.SqlQuery<int>($"""
                        SELECT COUNT(*) AS "Value"
                        FROM subscriptions
                        WHERE facultyid = {facultyId.Value}
                          AND (status = 'Approved' OR status IS NULL)
                          AND NOW() BETWEEN startdate AND enddate
                    """).SingleAsync();
                    hasActiveSubscription = subCount > 0;
                }

                var allowedCategoryIds = new List<int>();
                var hasCategoryMappings = false;
                if (facultyId.HasValue && hasActiveSubscription)
                {
                    try
                    {
                        var activeSubscriptionIds = await _db.Database.SqlQuery<int>($"""
                            SELECT subscriptionid AS "Value"
                            FROM subscriptions
                            WHERE facultyid = {facultyId.Value}
                              AND (status = 'Approved' OR status IS NULL)
                              AND NOW() BETWEEN startdate AND enddate
                        """).ToListAsync();

                        if (activeSubscriptionIds.Count > 0)
                        {
                            allowedCategoryIds = await _db.Database
                                .SqlQuery<int>($"""
                                    SELECT DISTINCT categoryid AS "Value"
                                    FROM subscriptioncategories
                                    WHERE subscriptionid = ANY({activeSubscriptionIds.ToArray()})
                                """)
                                .ToListAsync();
                            hasCategoryMappings = allowedCategoryIds.Count > 0;
                        }
                    }
                    catch
                    {
                        allowedCategoryIds = new List<int>();
                        hasCategoryMappings = false;
                    }
                }

                var bookCategoryIds = await _db.Database.SqlQuery<int>($"""
                    SELECT DISTINCT categoryid AS "Value"
                    FROM bookcategories
                    WHERE bookid = {id}
                """).ToListAsync();
                var bookCategoryMatch = !hasCategoryMappings || bookCategoryIds.Any(cid => allowedCategoryIds.Contains(cid));

                var personalRequestStatus = await _db.Database
                    .SqlQuery<string?>($"""
                        SELECT status
                        FROM bookrequests
                        WHERE userid = {userId}
                          AND bookid = {id}
                    """)
                    .SingleOrDefaultAsync();

                if (personalRequestStatus is not ("Pending" or "Approved"))
                    personalRequestStatus = null;

                if (personalRequestStatus != "Approved" && (!hasActiveSubscription || !bookCategoryMatch))
                    return Forbid();
            }

            var fullPath = ResolveBookFullPath(book.FilePath);
            if (string.IsNullOrWhiteSpace(fullPath) || !System.IO.File.Exists(fullPath))
                return NotFound();

            var contentType = BookFileFormatHelper.GetDownloadContentType(fullPath);

            var result = PhysicalFile(fullPath, contentType);
            result.EnableRangeProcessing = true;
            return result;
        }

        private string? ResolveBookFullPath(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            var trimmed = filePath.Trim();
            var root = Path.GetPathRoot(trimmed) ?? string.Empty;
            var isWindowsRoot = root.Contains(':');
            var isUnc = root.StartsWith(@"\\");

            if (isWindowsRoot || isUnc)
                return trimmed;

            var relativePath = trimmed.Replace("\\", "/").TrimStart('~').TrimStart('/', '\\');

            var wwwrootCandidate = Path.Combine(_environment.ContentRootPath, "wwwroot", relativePath);
            if (System.IO.File.Exists(wwwrootCandidate))
                return wwwrootCandidate;

            var parent = Directory.GetParent(_environment.ContentRootPath)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
            {
                var apiSiblingWwwroot = Path.Combine(parent, "LibraryMPT.Api", "wwwroot", relativePath);
                if (System.IO.File.Exists(apiSiblingWwwroot))
                    return apiSiblingWwwroot;
            }

            return wwwrootCandidate;
        }

        private static async Task<ApiCommandResponse?> ReadApiCommandResponseSafeAsync(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!IsJsonContent(response.Content.Headers.ContentType))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<ApiCommandResponse>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool IsJsonContent(MediaTypeHeaderValue? contentType)
        {
            var mediaType = contentType?.MediaType;
            return !string.IsNullOrWhiteSpace(mediaType) &&
                   mediaType.Contains("json", StringComparison.OrdinalIgnoreCase);
        }
    }
}

