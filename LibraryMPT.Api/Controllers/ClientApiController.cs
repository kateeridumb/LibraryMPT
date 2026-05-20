using System.Net.Http;
using LibraryMPT.Data;
using LibraryMPT.Api.Extensions;
using LibraryMPT.Api.Helpers;
using LibraryMPT.Api.Infrastructure;
using LibraryMPT.Helpers;
using LibraryMPT.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace LibraryMPT.Api.Controllers;

[ApiController]
[Authorize(Roles = "Student,Admin,Librarian,InstitutionRepresentative,Guest")]
[Route("api/client")]
public sealed class ClientApiController : ControllerBase
{
    private const string BookCoverBrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    private readonly LibraryContext _context;
    private readonly ILogger<ClientApiController> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpClientFactory _httpClientFactory;

    public ClientApiController(
        LibraryContext context,
        ILogger<ClientApiController> logger,
        IWebHostEnvironment environment,
        IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _logger = logger;
        _environment = environment;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("index")]
    public async Task<ActionResult<ClientIndexResponse>> Index([FromQuery] string? search, [FromQuery] int[]? categoryIds)
    {
        var userId = User.GetUserId();
        var searchParam = new NpgsqlParameter("@search", NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim()
        };
        var ids = categoryIds?.Where(x => x > 0).Distinct().ToArray();
        var categoryParam = new NpgsqlParameter("@categoryIds", NpgsqlDbType.Array | NpgsqlDbType.Integer)
        {
            Value = (ids != null && ids.Length > 0) ? ids : DBNull.Value
        };

        var books = await _context.Books
            .FromSqlRaw("""
                SELECT
                    b.bookid AS "BookID",
                    b.title AS "Title",
                    b.description AS "Description",
                    b.publishyear AS "PublishYear",
                    b.authorid AS "AuthorID",
                    b.publisherid AS "PublisherID",
                    b.imagepath AS "ImagePath",
                    b.filepath AS "FilePath",
                    b.requiressubscription AS "RequiresSubscription"
                FROM books b
                WHERE (@search IS NULL OR b.title ILIKE '%' || @search || '%' OR b.description ILIKE '%' || @search || '%')
                  AND (@categoryIds IS NULL OR EXISTS (
                    SELECT 1 FROM bookcategories bc
                    WHERE bc.bookid = b.bookid AND bc.categoryid = ANY(@categoryIds)
                  ))
            """, searchParam, categoryParam)
            .AsNoTracking()
            .ToListAsync();

        if (books.Any())
        {
            var authorIds = books.Where(b => b.AuthorID > 0).Select(b => b.AuthorID).Distinct().ToList();
            var publisherIds = books.Where(b => b.PublisherID.HasValue).Select(b => b.PublisherID!.Value).Distinct().ToList();
            var bookIds = books.Select(b => b.BookID).ToList();

            var authors = authorIds.Any()
                ? await _context.Authors
                    .FromSqlRaw($"""
                        SELECT authorid AS "AuthorID", firstname AS "FirstName", lastname AS "LastName"
                        FROM authors
                        WHERE authorid IN ({string.Join(",", authorIds)})
                    """)
                    .AsNoTracking()
                    .ToListAsync()
                : new List<Author>();
            var publishers = publisherIds.Any()
                ? await _context.Publisher
                    .FromSqlRaw($"""
                        SELECT publisherid AS "PublisherID", publishername AS "PublisherName"
                        FROM publisher
                        WHERE publisherid IN ({string.Join(",", publisherIds)})
                    """)
                    .AsNoTracking()
                    .ToListAsync()
                : new List<Publisher>();

            var bcRows = await _context.Database
                .SqlQuery<BookCategoryRow>($"""
                    SELECT bookid AS "BookID", categoryid AS "CategoryID"
                    FROM bookcategories
                    WHERE bookid = ANY({bookIds.ToArray()})
                """)
                .ToListAsync();
            var catIds = bcRows.Select(r => r.CategoryID).Distinct().ToList();
            var categoriesById = catIds.Count > 0
                ? await _context.Categories
                    .FromSqlRaw($"""
                        SELECT categoryid AS "CategoryID", categoryname AS "CategoryName"
                        FROM categories
                        WHERE categoryid IN ({string.Join(",", catIds)})
                        """)
                    .AsNoTracking()
                    .ToListAsync()
                : new List<Category>();

            foreach (var book in books)
            {
                book.Author = authors.FirstOrDefault(a => a.AuthorID == book.AuthorID);
                book.Publisher = publishers.FirstOrDefault(p => p.PublisherID == book.PublisherID);
                book.CategoryIds = bcRows.Where(r => r.BookID == book.BookID).Select(r => r.CategoryID).ToList();
                book.Categories = categoriesById.Where(c => book.CategoryIds.Contains(c.CategoryID)).ToList();
            }
        }
        var categories = await _context.Database.SqlQuery<Category>($"""
            SELECT
                categoryid AS "CategoryID",
                categoryname AS "CategoryName"
            FROM categories
        """).ToListAsync();

        var facultyId = await _context.Database
            .SqlQuery<int?>($"""
                SELECT facultyid AS "Value"
                FROM users
                WHERE userid = {userId}
            """)
            .SingleOrDefaultAsync();

        var hasActiveSubscription = false;
        if (facultyId.HasValue)
        {
            var subCount = await _context.Database
                .SqlQuery<int>($"""
                    SELECT COUNT(*) AS "Value"
                    FROM subscriptions
                    WHERE facultyid = {facultyId.Value}
                      AND (status = 'Approved' OR status IS NULL)
                      AND NOW() BETWEEN startdate AND enddate
                """)
                .SingleAsync();
            hasActiveSubscription = subCount > 0;
        }
        var allowedCategoryIds = new List<int>();
        var hasCategoryMappings = false;
        if (facultyId.HasValue && hasActiveSubscription)
        {
            try
            {
                var activeSubscriptionIds = await _context.Database.SqlQuery<int>($"""
                    SELECT subscriptionid AS "Value"
                    FROM subscriptions
                    WHERE facultyid = {facultyId.Value}
                      AND (status = 'Approved' OR status IS NULL)
                      AND NOW() BETWEEN startdate AND enddate
                """).ToListAsync();

                if (activeSubscriptionIds.Any())
                {
                    allowedCategoryIds = await _context.Database
                        .SqlQuery<int>($"""
                            SELECT DISTINCT categoryid AS "Value"
                            FROM subscriptioncategories
                            WHERE subscriptionid = ANY({activeSubscriptionIds.ToArray()})
                        """)
                        .ToListAsync();

                    hasCategoryMappings = allowedCategoryIds.Any();
                }
            }
            catch
            {
                allowedCategoryIds = new List<int>();
                hasCategoryMappings = false;
            }
        }
        var personalPendingBookIds = await _context.Database
            .SqlQuery<int>($"""
                SELECT DISTINCT bookid AS "Value"
                FROM bookrequests
                WHERE userid = {userId}
                  AND status = 'Pending'
            """)
            .ToListAsync();

        var personalApprovedBookIds = await _context.Database
            .SqlQuery<int>($"""
                SELECT DISTINCT bookid AS "Value"
                FROM bookrequests
                WHERE userid = {userId}
                  AND status = 'Approved'
            """)
            .ToListAsync();

        // В каталоге показываем все книги, включая подписочные.
        // Фактический доступ по подписке/категории проверяется в BookDetails/ReadOnline/Download.

        var readedBookIds = await _context.Database
            .SqlQuery<int>($"""
                SELECT DISTINCT bookid AS "Value"
                FROM booklogs
                WHERE userid = {userId}
                  AND LOWER(TRIM(COALESCE(actiontype, ''))) = 'read'
            """)
            .ToListAsync();

        var readed = await _context.Database
            .SqlQuery<int>($"""
                SELECT COUNT(DISTINCT BookID) AS "Value"
                FROM booklogs
                WHERE userid = {userId}
                  AND LOWER(TRIM(COALESCE(actiontype, ''))) = 'read'
            """)
            .SingleOrDefaultAsync();

        return Ok(new ClientIndexResponse
        {
            Books = books,
            Categories = categories,
            HasSubscription = hasActiveSubscription,
            SubscriptionStatus = hasActiveSubscription ? "Активна" : "Нет активной подписки",
            PersonalPendingBookIds = personalPendingBookIds,
            PersonalApprovedBookIds = personalApprovedBookIds,
            ReadedBookIds = readedBookIds,
            TotalBooks = books.Count,
            Readed = readed
        });
    }

    [HttpGet("book-details/{id:int}")]
    public async Task<ActionResult<ClientBookDetailsResponse>> BookDetails([FromRoute] int id)
    {
        var userId = User.GetUserId();
        var book = await _context.Books
            .FromSqlRaw("""
                SELECT
                    bookid AS "BookID",
                    title AS "Title",
                    description AS "Description",
                    publishyear AS "PublishYear",
                    authorid AS "AuthorID",
                    publisherid AS "PublisherID",
                    imagepath AS "ImagePath",
                    filepath AS "FilePath",
                    requiressubscription AS "RequiresSubscription"
                FROM books
                WHERE bookid = @id
            """, new NpgsqlParameter("@id", id))
            .AsNoTracking()
            .FirstOrDefaultAsync();
        if (book == null)
        {
            return NotFound();
        }

        if (book.AuthorID > 0)
        {
            book.Author = await _context.Authors
                .FromSqlRaw("""
                    SELECT authorid AS "AuthorID", firstname AS "FirstName", lastname AS "LastName"
                    FROM authors
                    WHERE authorid = @id
                """, new NpgsqlParameter("@id", book.AuthorID))
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }
        var bcRows = await _context.Database
            .SqlQuery<BookCategoryRow>($"""
                SELECT bookid AS "BookID", categoryid AS "CategoryID"
                FROM bookcategories
                WHERE bookid = {id}
            """)
            .ToListAsync();
        book.CategoryIds = bcRows.Select(r => r.CategoryID).ToList();
        if (book.CategoryIds.Count > 0)
        {
            var catIdsParam = new NpgsqlParameter("@ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = book.CategoryIds.ToArray() };
            book.Categories = await _context.Categories
                .FromSqlRaw("""
                    SELECT categoryid AS "CategoryID", categoryname AS "CategoryName"
                    FROM categories
                    WHERE categoryid = ANY(@ids)
                    """, catIdsParam)
                .AsNoTracking()
                .ToListAsync();
        }
        if (book.PublisherID.HasValue)
        {
            book.Publisher = await _context.Publisher
                .FromSqlRaw("""
                    SELECT
                        publisherid AS "PublisherID",
                        publishername AS "PublisherName"
                    FROM publisher
                    WHERE publisherid = @id
                """, new NpgsqlParameter("@id", book.PublisherID.Value))
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }

        var facultyId = await _context.Database
            .SqlQuery<int?>($"SELECT facultyid AS \"Value\" FROM users WHERE userid = {userId}")
            .SingleOrDefaultAsync();
        var hasActiveSubscription = false;
        if (facultyId.HasValue)
        {
            var subCount = await _context.Database.SqlQuery<int>($"""
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
                var activeSubscriptionIds = await _context.Database.SqlQuery<int>($"""
                    SELECT subscriptionid AS "Value"
                    FROM subscriptions
                    WHERE facultyid = {facultyId.Value}
                      AND (status = 'Approved' OR status IS NULL)
                      AND NOW() BETWEEN startdate AND enddate
                """).ToListAsync();

                if (activeSubscriptionIds.Any())
                {
                    allowedCategoryIds = await _context.Database
                        .SqlQuery<int>($"""
                            SELECT DISTINCT categoryid AS "Value"
                            FROM subscriptioncategories
                            WHERE subscriptionid = ANY({activeSubscriptionIds.ToArray()})
                        """)
                        .ToListAsync();

                    hasCategoryMappings = allowedCategoryIds.Any();
                }
            }
            catch
            {
                allowedCategoryIds = new List<int>();
                hasCategoryMappings = false;
            }
        }
        string? personalRequestStatus = null;
        if (book.RequiresSubscription)
        {
            personalRequestStatus = await _context.Database
                .SqlQuery<string?>($"""
                    SELECT status AS "Value"
                    FROM bookrequests
                    WHERE userid = {userId}
                      AND bookid = {id}
                """)
                .SingleOrDefaultAsync();

            if (personalRequestStatus is not ("Pending" or "Approved"))
                personalRequestStatus = null;
        }

        var bookCategoryMatch = !hasCategoryMappings || book.CategoryIds.Any(cid => allowedCategoryIds.Contains(cid));
        var canRead = !book.RequiresSubscription
            || personalRequestStatus == "Approved"
            || (hasActiveSubscription && bookCategoryMatch);

        return Ok(new ClientBookDetailsResponse
        {
            Book = book,
            HasSubscription = hasActiveSubscription,
            CanRead = canRead,
            PersonalRequestStatus = personalRequestStatus,
            FileType = BookFileFormatHelper.GetReaderFileType(book.FilePath)
        });
    }

    [HttpPost("book-requests")]
    public async Task<ActionResult<ApiCommandResponse>> CreateBookRequest([FromBody] BookRequestCreateRequest request)
    {
        if (User.IsInRole("Guest"))
            return Forbid();

        var userId = User.GetUserId();
        if (request == null || request.BookId <= 0)
            return Ok(new ApiCommandResponse { Success = false, Message = "Некорректный идентификатор книги." });

        var book = await _context.Database.SqlQuery<BookDownloadDto>($"""
            SELECT
                bookid AS "BookID",
                title AS "Title",
                filepath AS "FilePath",
                requiressubscription AS "RequiresSubscription"
            FROM books
            WHERE bookid = {request.BookId}
        """).AsNoTracking().SingleOrDefaultAsync();

        if (book == null)
            return Ok(new ApiCommandResponse { Success = false, Message = "Книга не найдена." });

        if (!book.RequiresSubscription)
            return Ok(new ApiCommandResponse { Success = true, Message = "Книга доступна без подписки." });
        var facultyId = await _context.Database
            .SqlQuery<int?>($"""
                SELECT facultyid AS "Value"
                FROM users
                WHERE userid = {userId}
            """)
            .SingleOrDefaultAsync();

        if (facultyId.HasValue)
        {
            var subCount = await _context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS "Value"
                FROM subscriptions
                WHERE facultyid = {facultyId.Value}
                  AND (status = 'Approved' OR status IS NULL)
                  AND NOW() BETWEEN startdate AND enddate
            """).SingleAsync();

            if (subCount > 0)
            {
                return Ok(new ApiCommandResponse { Success = true, Message = "Доступ к книге предоставлен активной подпиской." });
            }
        }

        var existingStatus = await _context.Database
            .SqlQuery<string?>($"""
                SELECT status AS "Value"
                FROM bookrequests
                WHERE userid = {userId}
                  AND bookid = {request.BookId}
            """)
            .SingleOrDefaultAsync();

        if (existingStatus is not null)
        {
            if (existingStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                return Ok(new ApiCommandResponse { Success = true, Message = "Заявка уже отправлена на рассмотрение." });

            if (existingStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                return Ok(new ApiCommandResponse { Success = true, Message = "Заявка уже одобрена. Доступ к книге предоставлен." });
            await _context.Database.ExecuteSqlRawAsync("""
                UPDATE bookrequests
                SET status = 'Pending',
                    createdat = NOW(),
                    decidedat = NULL,
                    decisionuserid = NULL
                WHERE userid = @userId AND bookid = @bookId
            """, new NpgsqlParameter("@userId", userId), new NpgsqlParameter("@bookId", request.BookId));
            return Ok(new ApiCommandResponse { Success = true, Message = "Заявка отправлена повторно на рассмотрение." });
        }

        await _context.Database.ExecuteSqlRawAsync("""
            INSERT INTO bookrequests (userid, bookid, status, createdat)
            VALUES (@userId, @bookId, 'Pending', NOW())
        """,
            new NpgsqlParameter("@userId", userId),
            new NpgsqlParameter("@bookId", request.BookId));

        return Ok(new ApiCommandResponse { Success = true, Message = "Заявка на доступ к книге отправлена." });
    }

    [HttpPost("mark-read")]
    public async Task<ActionResult<ApiCommandResponse>> MarkRead([FromBody] MarkAsReadRequest request)
    {
        var userId = User.GetUserId();
        if (request.BookId <= 0)
        {
            return Ok(new ApiCommandResponse { Success = false, Message = "Некорректный ID книги." });
        }

        var bookExists = await _context.Database
            .SqlQuery<int>($"""
                SELECT COUNT(*) AS "Value"
                FROM books
                WHERE bookid = {request.BookId}
            """)
            .SingleAsync();
        if (bookExists == 0)
        {
            return Ok(new ApiCommandResponse { Success = false, Message = "Книга не найдена." });
        }

        await _context.Database.ExecuteSqlRawAsync("""
            INSERT INTO booklogs (userid, bookid, actiontype, actionat)
            VALUES (@userId, @bookId, 'READ', NOW())
        """, new NpgsqlParameter("@userId", userId), new NpgsqlParameter("@bookId", request.BookId));
        await _context.Database.ExecuteSqlRawAsync("""
            INSERT INTO readingprogress (userid, bookid, lastpage, lastposition, percent, updatedat)
            VALUES (@userId, @bookId, NULL, NULL, 100, NOW())
            ON CONFLICT (userid, bookid) DO UPDATE SET
                percent = GREATEST(COALESCE(readingprogress.percent, 0), 100),
                updatedat = NOW()
        """, new NpgsqlParameter("@userId", userId), new NpgsqlParameter("@bookId", request.BookId));

        return Ok(new ApiCommandResponse { Success = true });
    }

    [HttpGet("read-online/{id:int}")]
    public async Task<ActionResult<ClientReadOnlineResponse>> ReadOnline([FromRoute] int id)
    {
        var userId = User.GetUserId();
        var book = await _context.Books
            .FromSqlRaw("""
                SELECT
                    bookid AS "BookID",
                    title AS "Title",
                    description AS "Description",
                    publishyear AS "PublishYear",
                    authorid AS "AuthorID",
                    publisherid AS "PublisherID",
                    filepath AS "FilePath",
                    imagepath AS "ImagePath",
                    requiressubscription AS "RequiresSubscription"
                FROM books
                WHERE bookid = @id
            """, new NpgsqlParameter("@id", id))
            .AsNoTracking()
            .FirstOrDefaultAsync();
        if (book == null)
        {
            return NotFound();
        }

        if (book.AuthorID > 0)
        {
            book.Author = await _context.Authors
                .FromSqlRaw("""
                    SELECT authorid AS "AuthorID", firstname AS "FirstName", lastname AS "LastName"
                    FROM authors
                    WHERE authorid = @id
                """, new NpgsqlParameter("@id", book.AuthorID))
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }

        var facultyId = await _context.Database
            .SqlQuery<int?>($"SELECT facultyid AS \"Value\" FROM users WHERE userid = {userId}")
            .SingleOrDefaultAsync();
        var hasActiveSubscription = false;
        if (facultyId.HasValue)
        {
            var subCount = await _context.Database.SqlQuery<int>($"""
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
                var activeSubscriptionIds = await _context.Database.SqlQuery<int>($"""
                    SELECT subscriptionid AS "Value"
                    FROM subscriptions
                    WHERE facultyid = {facultyId.Value}
                      AND (status = 'Approved' OR status IS NULL)
                      AND NOW() BETWEEN startdate AND enddate
                """).ToListAsync();

                if (activeSubscriptionIds.Any())
                {
                    allowedCategoryIds = await _context.Database
                        .SqlQuery<int>($"""
                            SELECT DISTINCT categoryid AS "Value"
                            FROM subscriptioncategories
                            WHERE subscriptionid = ANY({activeSubscriptionIds.ToArray()})
                        """)
                        .ToListAsync();
                    hasCategoryMappings = allowedCategoryIds.Any();
                }
            }
            catch
            {
                allowedCategoryIds = new List<int>();
                hasCategoryMappings = false;
            }
        }

        var bookCategoryIds = book.RequiresSubscription
            ? await _context.Database.SqlQuery<int>($"""
                SELECT DISTINCT categoryid AS "Value"
                FROM bookcategories
                WHERE bookid = {id}
            """).ToListAsync()
            : new List<int>();

        var bookCategoryMatch = !hasCategoryMappings || bookCategoryIds.Any(cid => allowedCategoryIds.Contains(cid));

        string? personalRequestStatus = null;
        if (book.RequiresSubscription)
        {
            personalRequestStatus = await _context.Database
                .SqlQuery<string?>($"""
                    SELECT status AS "Value"
                    FROM bookrequests
                    WHERE userid = {userId}
                      AND bookid = {id}
                """)
                .SingleOrDefaultAsync();

            if (personalRequestStatus is not ("Pending" or "Approved"))
                personalRequestStatus = null;
        }

        var canRead = !book.RequiresSubscription
            || personalRequestStatus == "Approved"
            || (hasActiveSubscription && bookCategoryMatch);
        if (canRead)
        {
            var hasReadLog = await _context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS "Value"
                FROM booklogs
                WHERE userid = {userId}
                  AND bookid = {id}
                  AND LOWER(TRIM(COALESCE(actiontype, ''))) = 'read'
                  AND actionat >= NOW() - INTERVAL '5 minutes'
            """).SingleAsync();

            if (hasReadLog == 0)
            {
                await _context.Database.ExecuteSqlRawAsync("""
                    INSERT INTO booklogs (userid, bookid, actiontype, actionat)
                    VALUES (@userId, @bookId, 'READ', NOW())
                """, new NpgsqlParameter("@userId", userId), new NpgsqlParameter("@bookId", id));
            }
        }

        var fileType = BookFileFormatHelper.GetReaderFileType(book.FilePath);

        ReadingProgressDto? savedProgress = null;
        var progressRow = await _context.Database.SqlQuery<ReadingProgressDto>($"""
            SELECT bookid AS "BookID", lastpage AS "LastPage", lastposition AS "LastPosition", percent AS "Percent", updatedat AS "UpdatedAt"
            FROM readingprogress
            WHERE userid = {userId} AND bookid = {id}
        """).AsNoTracking().FirstOrDefaultAsync();
        if (progressRow != null)
            savedProgress = progressRow;

        return Ok(new ClientReadOnlineResponse
        {
            Book = book,
            HasSubscription = hasActiveSubscription,
            CanRead = canRead,
            FilePath = book.FilePath,
            FileType = fileType,
            SavedProgress = savedProgress
        });
    }

    [HttpGet("reading-progress")]
    public async Task<ActionResult<ReadingProgressDto?>> GetReadingProgress([FromQuery] int bookId)
    {
        var userId = User.GetUserId();
        var progress = await _context.Database.SqlQuery<ReadingProgressDto>($"""
            SELECT bookid AS "BookID", lastpage AS "LastPage", lastposition AS "LastPosition", percent AS "Percent", updatedat AS "UpdatedAt"
            FROM readingprogress
            WHERE userid = {userId} AND bookid = {bookId}
        """).AsNoTracking().FirstOrDefaultAsync();
        return Ok(progress);
    }

    [HttpPost("save-progress")]
    public async Task<ActionResult<ApiCommandResponse>> SaveProgress([FromBody] SaveProgressRequest request)
    {
        var userId = User.GetUserId();
        if (request.BookId <= 0)
            return Ok(new ApiCommandResponse { Success = false, Message = "Некорректный ID книги." });

        await _context.Database.ExecuteSqlRawAsync("""
            INSERT INTO readingprogress (userid, bookid, lastpage, lastposition, percent, updatedat)
            VALUES (@userId, @bookId, @lastPage, @lastPosition, @percent, NOW())
            ON CONFLICT (userid, bookid) DO UPDATE SET
                lastpage = COALESCE(EXCLUDED.lastpage, readingprogress.lastpage),
                lastposition = COALESCE(EXCLUDED.lastposition, readingprogress.lastposition),
                percent = COALESCE(EXCLUDED.percent, readingprogress.percent),
                updatedat = NOW()
            WHERE
                readingprogress.lastpage IS DISTINCT FROM COALESCE(EXCLUDED.lastpage, readingprogress.lastpage)
                OR readingprogress.lastposition IS DISTINCT FROM COALESCE(EXCLUDED.lastposition, readingprogress.lastposition)
                OR readingprogress.percent IS DISTINCT FROM COALESCE(EXCLUDED.percent, readingprogress.percent)
            """,
            new NpgsqlParameter("@userId", userId),
            new NpgsqlParameter("@bookId", request.BookId),
            new NpgsqlParameter("@lastPage", (object?)request.Page ?? DBNull.Value),
            new NpgsqlParameter("@lastPosition", (object?)request.Position ?? DBNull.Value),
            new NpgsqlParameter("@percent", (object?)request.Percent ?? DBNull.Value));
        return Ok(new ApiCommandResponse { Success = true });
    }

    [HttpGet("personal-cabinet")]
    public async Task<ActionResult<ClientCabinetResponse>> PersonalCabinet()
    {
        var userId = User.GetUserId();
        try
        {
            List<ReadingProgressDto> progressRows;
            try
            {
                progressRows = await _context.Database.SqlQuery<ReadingProgressDto>($"""
                    SELECT rp.bookid AS "BookID", rp.lastpage AS "LastPage", rp.lastposition AS "LastPosition", rp.percent AS "Percent", rp.updatedat AS "UpdatedAt"
                    FROM readingprogress rp
                    WHERE rp.userid = {userId}
                    ORDER BY rp.updatedat DESC
                    LIMIT 20
                """).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PersonalCabinet: failed to load readingprogress for userId={UserId}", userId);
                progressRows = new List<ReadingProgressDto>();
            }

            List<int> readedBookIds;
            try
            {
                readedBookIds = await _context.Database.SqlQuery<int>($"""
                    SELECT t.bookid AS "Value"
                    FROM (
                        SELECT bookid, MAX(actionat) AS lastread
                        FROM booklogs
                        WHERE userid = {userId}
                          AND LOWER(TRIM(COALESCE(actiontype, ''))) = 'read'
                        GROUP BY bookid
                        ORDER BY lastread DESC
                        LIMIT 40
                    ) t
                    ORDER BY t.lastread DESC
                """).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PersonalCabinet: failed to load read logs for userId={UserId}", userId);
                readedBookIds = new List<int>();
            }

            List<int> requestableBookIds;
            try
            {
                requestableBookIds = await _context.Database.SqlQuery<int>($"""
                    SELECT b.bookid AS "Value"
                    FROM books b
                    WHERE b.requiressubscription = TRUE
                      AND NOT EXISTS (
                          SELECT 1
                          FROM bookrequests br
                          WHERE br.userid = {userId}
                            AND br.bookid = b.bookid
                            AND br.status IN ('Pending', 'Approved')
                      )
                    ORDER BY b.bookid DESC
                    LIMIT 24
                """).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PersonalCabinet: failed to load requestable books for userId={UserId}", userId);
                requestableBookIds = new List<int>();
            }

            var allBookIds = progressRows.Select(p => p.BookID)
                .Concat(readedBookIds)
                .Concat(requestableBookIds)
                .Distinct()
                .ToList();

            List<Book> books;
            try
            {
                if (allBookIds.Count == 0)
                {
                    books = new List<Book>();
                }
                else
                {
                    var bookIdsParam = new NpgsqlParameter("@ids", NpgsqlDbType.Array | NpgsqlDbType.Integer)
                    {
                        Value = allBookIds.ToArray()
                    };
                    books = await _context.Books
                        .FromSqlRaw("""
                            SELECT
                                b.bookid AS "BookID",
                                b.title AS "Title",
                                b.description AS "Description",
                                b.publishyear AS "PublishYear",
                                b.authorid AS "AuthorID",
                                b.publisherid AS "PublisherID",
                                b.filepath AS "FilePath",
                                b.imagepath AS "ImagePath",
                                b.requiressubscription AS "RequiresSubscription"
                            FROM books b
                            WHERE b.bookid = ANY(@ids)
                        """, bookIdsParam)
                        .AsNoTracking()
                        .ToListAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PersonalCabinet: failed to load books for userId={UserId}", userId);
                books = new List<Book>();
            }

            if (books.Any())
            {
            var authorIds = books.Where(b => b.AuthorID > 0).Select(b => b.AuthorID).Distinct().ToList();
            List<Author> authors;
            try
            {
                if (!authorIds.Any())
                {
                    authors = new List<Author>();
                }
                else
                {
                    var authorIdsParam = new NpgsqlParameter("@ids", NpgsqlDbType.Array | NpgsqlDbType.Integer)
                    {
                        Value = authorIds.ToArray()
                    };
                    authors = await _context.Authors
                        .FromSqlRaw("""
                            SELECT
                                authorid AS "AuthorID",
                                firstname AS "FirstName",
                                lastname AS "LastName"
                            FROM authors
                            WHERE authorid = ANY(@ids)
                        """, authorIdsParam)
                        .AsNoTracking()
                        .ToListAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PersonalCabinet: failed to load authors for userId={UserId}", userId);
                authors = new List<Author>();
            }

            List<BookCategoryRow> bcRows;
            try
            {
                bcRows = await _context.Database
                    .SqlQuery<BookCategoryRow>($"""
                        SELECT bookid AS "BookID", categoryid AS "CategoryID"
                        FROM bookcategories
                        WHERE bookid = ANY({allBookIds.ToArray()})
                    """)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PersonalCabinet: failed to load bookcategories for userId={UserId}", userId);
                bcRows = new List<BookCategoryRow>();
            }

            var catIds = bcRows.Select(r => r.CategoryID).Distinct().ToList();
            List<Category> categories;
            try
            {
                categories = catIds.Count == 0
                    ? new List<Category>()
                    : await _context.Database.SqlQuery<Category>($"""
                        SELECT categoryid AS "CategoryID", categoryname AS "CategoryName"
                        FROM categories
                        WHERE categoryid = ANY({catIds.ToArray()})
                    """).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PersonalCabinet: failed to load categories for userId={UserId}", userId);
                categories = new List<Category>();
            }

            foreach (var book in books)
            {
                book.Author = authors.FirstOrDefault(a => a.AuthorID == book.AuthorID);
                book.CategoryIds = bcRows.Where(r => r.BookID == book.BookID).Select(r => r.CategoryID).ToList();
                book.Categories = categories.Where(c => book.CategoryIds.Contains(c.CategoryID)).ToList();
            }
            }

            var booksById = books
                .GroupBy(b => b.BookID)
                .Select(g => g.First())
                .ToDictionary(b => b.BookID, b => b);

            var lastRead = progressRows
                .Select(p =>
                {
                    if (!booksById.TryGetValue(p.BookID, out var book))
                        return null;
                    return new BookWithProgressDto
                    {
                        Book = book,
                        LastPage = p.LastPage,
                        LastPosition = p.LastPosition,
                        Percent = p.Percent,
                        UpdatedAt = p.UpdatedAt
                    };
                })
                .Where(x => x != null)
                .Cast<BookWithProgressDto>()
                .ToList();

            var readedBooks = readedBookIds
                .Where(id => booksById.ContainsKey(id))
                .Select(id => booksById[id])
                .ToList();

            var requestableBooks = requestableBookIds
                .Where(id => booksById.ContainsKey(id))
                .Select(id => booksById[id])
                .ToList();

            List<BookRequestDto> myBookRequests;
            try
            {
                myBookRequests = await _context.BookRequests
                    .FromSqlRaw("""
                        SELECT
                            br.bookrequestid AS "BookRequestID",
                            br.userid AS "UserID",
                            br.bookid AS "BookID",
                            b.title AS "BookTitle",
                            br.status AS "Status",
                            br.createdat AS "CreatedAt",
                            br.decidedat AS "DecidedAt",
                            br.decisionuserid AS "DecisionByUserID",
                            u.username AS "RequestedByUsername",
                            u.email AS "RequestedByEmail"
                        FROM bookrequests br
                        JOIN books b ON b.bookid = br.bookid
                        JOIN users u ON u.userid = br.userid
                        WHERE br.userid = @userId
                        ORDER BY br.createdat DESC
                        LIMIT 20
                    """, new NpgsqlParameter("@userId", userId))
                    .AsNoTracking()
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PersonalCabinet: failed to load book requests for userId={UserId}", userId);
                myBookRequests = new List<BookRequestDto>();
            }

            return Ok(new ClientCabinetResponse
            {
                LastRead = lastRead,
                ReadedBooks = readedBooks,
                RequestableBooks = requestableBooks,
                MyBookRequests = myBookRequests
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PersonalCabinet failed for userId={UserId}", userId);
            return Ok(new ClientCabinetResponse());
        }
    }

    [HttpGet("file/{id:int}")]
    public async Task<IActionResult> GetFile([FromRoute] int id)
    {
        var userId = User.GetUserId();
        var book = await _context.Database.SqlQuery<BookDownloadDto>($"""
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
            var facultyId = await _context.Database.SqlQuery<int?>($"""
                SELECT facultyid AS "Value" FROM users WHERE userid = {userId}
            """).SingleOrDefaultAsync();
            var hasActiveSubscription = false;
            if (facultyId.HasValue)
            {
                var subCount = await _context.Database.SqlQuery<int>($"""
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
                    var activeSubscriptionIds = await _context.Database.SqlQuery<int>($"""
                        SELECT subscriptionid AS "Value"
                        FROM subscriptions
                        WHERE facultyid = {facultyId.Value}
                          AND (status = 'Approved' OR status IS NULL)
                          AND NOW() BETWEEN startdate AND enddate
                    """).ToListAsync();

                    if (activeSubscriptionIds.Any())
                    {
                        allowedCategoryIds = await _context.Database
                            .SqlQuery<int>($"""
                                SELECT DISTINCT categoryid AS "Value"
                                FROM subscriptioncategories
                                WHERE subscriptionid = ANY({activeSubscriptionIds.ToArray()})
                            """)
                            .ToListAsync();
                        hasCategoryMappings = allowedCategoryIds.Any();
                    }
                }
                catch
                {
                    allowedCategoryIds = new List<int>();
                    hasCategoryMappings = false;
                }
            }

            var bookCategoryIds = await _context.Database.SqlQuery<int>($"""
                SELECT DISTINCT categoryid AS "Value"
                FROM bookcategories
                WHERE bookid = {id}
            """).ToListAsync();
            var bookCategoryMatch = !hasCategoryMappings || bookCategoryIds.Any(cid => allowedCategoryIds.Contains(cid));

            string? personalRequestStatus = await _context.Database
                .SqlQuery<string?>($"""
                    SELECT status AS "Value"
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

    [AllowAnonymous]
    [HttpGet("cover/{id:int}")]
    public async Task<IActionResult> GetCover([FromRoute] int id)
    {
        var imagePath = await _context.Database
            .SqlQuery<string?>($"""
                SELECT imagepath AS "Value"
                FROM books
                WHERE bookid = {id}
            """)
            .SingleOrDefaultAsync();

        return await CoverPhysicalFileOrPlaceholderAsync(imagePath, HttpContext.RequestAborted);
    }

    /// <summary>Обложка для мобильного клиента — учёт полных URL в imagepath и доп. корней поиска.</summary>
    [AllowAnonymous]
    [HttpGet("mobile/cover/{id:int}")]
    public async Task<IActionResult> GetCoverMobile([FromRoute] int id)
    {
        var imagePath = await _context.Database
            .SqlQuery<string?>($"""
                SELECT imagepath AS "Value"
                FROM books
                WHERE bookid = {id}
            """)
            .SingleOrDefaultAsync();

        return await CoverPhysicalFileOrPlaceholderAsync(imagePath, HttpContext.RequestAborted);
    }

    [HttpGet("download/{id:int}")]
    public async Task<IActionResult> Download([FromRoute] int id)
    {
        var userId = User.GetUserId();
        var isGuest = User.IsInRole("Guest");
        var book = await _context.Database.SqlQuery<BookDownloadDto>($"""
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

        if (isGuest)
            return BadRequest(new ApiCommandResponse { Success = false, Message = "Гостевой доступ не позволяет скачивать книги." });
        if (book.RequiresSubscription)
        {
            var facultyId = await _context.Database.SqlQuery<int?>($"""
                SELECT facultyid AS "Value"
                FROM users
                WHERE userid = {userId}
            """).SingleOrDefaultAsync();

            var hasActiveSubscription = false;
            if (facultyId.HasValue)
            {
                var subCount = await _context.Database.SqlQuery<int>($"""
                    SELECT COUNT(*) AS "Value"
                    FROM subscriptions
                    WHERE facultyid = {facultyId.Value}
                      AND (status = 'Approved' OR status IS NULL)
                      AND NOW() BETWEEN startdate AND enddate
                """).SingleAsync();
                hasActiveSubscription = subCount > 0;
            }

            var personalRequestStatus = await _context.Database
                .SqlQuery<string?>($"""
                    SELECT status AS "Value"
                    FROM bookrequests
                    WHERE userid = {userId}
                      AND bookid = {id}
                """).SingleOrDefaultAsync();

            if (personalRequestStatus is not ("Pending" or "Approved"))
                personalRequestStatus = null;

            var allowedCategoryIds = new List<int>();
            var hasCategoryMappings = false;
            if (facultyId.HasValue && hasActiveSubscription)
            {
                try
                {
                    var activeSubscriptionIds = await _context.Database.SqlQuery<int>($"""
                        SELECT subscriptionid AS "Value"
                        FROM subscriptions
                        WHERE facultyid = {facultyId.Value}
                          AND (status = 'Approved' OR status IS NULL)
                          AND NOW() BETWEEN startdate AND enddate
                    """).ToListAsync();

                    if (activeSubscriptionIds.Any())
                    {
                        allowedCategoryIds = await _context.Database
                            .SqlQuery<int>($"""
                                SELECT DISTINCT categoryid AS "Value"
                                FROM subscriptioncategories
                                WHERE subscriptionid = ANY({activeSubscriptionIds.ToArray()})
                            """)
                            .ToListAsync();

                        hasCategoryMappings = allowedCategoryIds.Any();
                    }
                }
                catch
                {
                    allowedCategoryIds = new List<int>();
                    hasCategoryMappings = false;
                }
            }

            var bookCategoryIds = await _context.Database.SqlQuery<int>($"""
                SELECT DISTINCT categoryid AS "Value"
                FROM bookcategories
                WHERE bookid = {id}
            """).ToListAsync();

            var bookCategoryMatch = !hasCategoryMappings || bookCategoryIds.Any(cid => allowedCategoryIds.Contains(cid));

            if (personalRequestStatus != "Approved" && (!hasActiveSubscription || !bookCategoryMatch))
            {
                var message = personalRequestStatus == "Pending"
                    ? "Доступ по этой книге предоставляется после одобрения вашей заявки."
                    : "Скачивание доступно только по активной подписке и выбранным категориям книг.";
                return BadRequest(new ApiCommandResponse { Success = false, Message = message });
            }
        }

        var fullPath = ResolveBookFullPath(book.FilePath);
        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        await _context.Database.ExecuteSqlRawAsync("""
            INSERT INTO booklogs (userid, bookid, actiontype, actionat)
            VALUES (@userId, @bookId, 'DOWNLOAD', NOW())
        """, new NpgsqlParameter("@userId", userId), new NpgsqlParameter("@bookId", id));

        var contentType = BookFileFormatHelper.GetDownloadContentType(fullPath);
        var downloadName = BookFileFormatHelper.BuildDownloadFileName(book.Title ?? "book", fullPath);
        return PhysicalFile(fullPath, contentType, downloadName);
    }

    [HttpGet("readed")]
    public async Task<ActionResult<ClientReadedResponse>> Readed([FromQuery] string? search)
    {
        var userId = User.GetUserId();
        try
        {
            var readedBookIds = await _context.Database.SqlQuery<int>($"""
                SELECT DISTINCT bookid AS "Value"
                FROM booklogs
                WHERE userid = {userId}
                  AND LOWER(TRIM(COALESCE(actiontype, ''))) = 'read'
            """).ToListAsync();

            if (!readedBookIds.Any())
                return Ok(new ClientReadedResponse { Books = new List<Book>() });

            var idsParam = new NpgsqlParameter("@ids", NpgsqlDbType.Array | NpgsqlDbType.Integer)
            {
                Value = readedBookIds.ToArray()
            };
            var searchParam = new NpgsqlParameter("@search", NpgsqlDbType.Text)
            {
                Value = string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim()
            };

            var books = await _context.Books
                .FromSqlRaw("""
                    SELECT DISTINCT
                        b.bookid AS "BookID",
                        b.title AS "Title",
                        b.description AS "Description",
                        b.publishyear AS "PublishYear",
                        b.authorid AS "AuthorID",
                        b.publisherid AS "PublisherID",
                        b.filepath AS "FilePath",
                        b.imagepath AS "ImagePath",
                        b.requiressubscription AS "RequiresSubscription"
                    FROM books b
                    WHERE b.bookid = ANY(@ids)
                      AND (
                        @search IS NULL
                        OR b.title ILIKE ('%' || @search || '%')
                        OR COALESCE(b.description, '') ILIKE ('%' || @search || '%')
                      )
                """, idsParam, searchParam)
                .AsNoTracking()
                .ToListAsync();
            if (books.Any())
            {
                var authorIds = books.Where(b => b.AuthorID > 0).Select(b => b.AuthorID).Distinct().ToList();
                var bookIds = books.Select(b => b.BookID).ToList();
                List<Author> authors;
                if (!authorIds.Any())
                {
                    authors = new List<Author>();
                }
                else
                {
                    var authorIdsParam = new NpgsqlParameter("@ids", NpgsqlDbType.Array | NpgsqlDbType.Integer)
                    {
                        Value = authorIds.ToArray()
                    };
                    authors = await _context.Authors
                        .FromSqlRaw("""
                            SELECT
                                authorid AS "AuthorID",
                                firstname AS "FirstName",
                                lastname AS "LastName"
                            FROM authors
                            WHERE authorid = ANY(@ids)
                        """, authorIdsParam)
                        .AsNoTracking()
                        .ToListAsync();
                }
                var bcRows = await _context.Database
                    .SqlQuery<BookCategoryRow>($"""
                        SELECT bookid AS "BookID", categoryid AS "CategoryID"
                        FROM bookcategories
                        WHERE bookid = ANY({bookIds.ToArray()})
                    """)
                    .ToListAsync();
                var catIds = bcRows.Select(r => r.CategoryID).Distinct().ToList();
                var categories = new List<Category>();
                if (catIds.Count > 0)
                {
                    categories = await _context.Database.SqlQuery<Category>($"""
                        SELECT
                            categoryid AS "CategoryID",
                            categoryname AS "CategoryName"
                        FROM categories
                        WHERE categoryid = ANY({catIds.ToArray()})
                    """).ToListAsync();
                }

                foreach (var book in books)
                {
                    book.Author = authors.FirstOrDefault(a => a.AuthorID == book.AuthorID);
                    book.CategoryIds = bcRows.Where(r => r.BookID == book.BookID).Select(r => r.CategoryID).ToList();
                    book.Categories = categories.Where(c => book.CategoryIds.Contains(c.CategoryID)).ToList();
                }
            }

            return Ok(new ClientReadedResponse { Books = books });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Readed endpoint failed for userId={UserId}", userId);
            return Ok(new ClientReadedResponse { Books = new List<Book>() });
        }
    }

    [HttpGet("bookmarks")]
    public async Task<ActionResult<List<Bookmark>>> GetBookmarks([FromQuery] int bookId)
    {
        var userId = User.GetUserId();
        var bookmarks = await _context.Database.SqlQuery<Bookmark>($"""
            SELECT
                bookmarkid AS "BookmarkID",
                userid AS "UserID",
                bookid AS "BookID",
                COALESCE(page::text, '') AS "Page",
                position AS "Position",
                title AS "Title",
                note AS "Note",
                createdat AS "CreatedAt"
            FROM bookmarks
            WHERE userid = {userId} AND bookid = {bookId}
            ORDER BY createdat DESC
        """).ToListAsync();
        return Ok(bookmarks);
    }

    [HttpPost("bookmarks")]
    public async Task<ActionResult<ApiCommandResponse>> AddBookmark([FromBody] ClientBookmarkRequest request)
    {
        var userId = User.GetUserId();
        var parsedPage = PageHelper.ParsePageNumber(request.Bookmark.Page);
        await _context.Database.ExecuteSqlRawAsync("""
            INSERT INTO bookmarks (userid, bookid, page, position, title, note, createdat)
            VALUES (@userId, @bookId, @page, @position, @title, @note, NOW())
        """,
            new NpgsqlParameter("@userId", userId),
            new NpgsqlParameter("@bookId", request.Bookmark.BookID),
            new NpgsqlParameter("@page", NpgsqlDbType.Integer) { Value = parsedPage.HasValue ? parsedPage.Value : DBNull.Value },
            new NpgsqlParameter("@position", (object?)request.Bookmark.Position ?? DBNull.Value),
            new NpgsqlParameter("@title", (object?)request.Bookmark.Title ?? DBNull.Value),
            new NpgsqlParameter("@note", (object?)request.Bookmark.Note ?? DBNull.Value));
        return Ok(new ApiCommandResponse { Success = true });
    }

    [HttpDelete("bookmarks/{bookmarkId:int}")]
    public async Task<ActionResult<ApiCommandResponse>> DeleteBookmark([FromRoute] int bookmarkId)
    {
        var userId = User.GetUserId();
        var deleted = await _context.Database.ExecuteSqlRawAsync("""
            DELETE FROM bookmarks
            WHERE bookmarkid = @bookmarkId AND userid = @userId
        """, new NpgsqlParameter("@bookmarkId", bookmarkId), new NpgsqlParameter("@userId", userId));
        return Ok(new ApiCommandResponse { Success = deleted > 0 });
    }

    [HttpPut("bookmarks")]
    public async Task<ActionResult<ApiCommandResponse>> UpdateBookmark([FromBody] ClientBookmarkRequest request)
    {
        var userId = User.GetUserId();
        var parsedPage = PageHelper.ParsePageNumber(request.Bookmark.Page);
        var updated = await _context.Database.ExecuteSqlRawAsync("""
            UPDATE bookmarks
            SET page = @page, position = @position, title = @title, note = @note
            WHERE bookmarkid = @bookmarkId AND userid = @userId
        """,
            new NpgsqlParameter("@bookmarkId", request.Bookmark.BookmarkID),
            new NpgsqlParameter("@userId", userId),
            new NpgsqlParameter("@page", NpgsqlDbType.Integer) { Value = parsedPage.HasValue ? parsedPage.Value : DBNull.Value },
            new NpgsqlParameter("@position", (object?)request.Bookmark.Position ?? DBNull.Value),
            new NpgsqlParameter("@title", (object?)request.Bookmark.Title ?? DBNull.Value),
            new NpgsqlParameter("@note", (object?)request.Bookmark.Note ?? DBNull.Value));
        return Ok(new ApiCommandResponse { Success = updated > 0 });
    }

    /// <remarks>Логика путей как до доработок под мобилку (Swagger, интеграции).</remarks>
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

        var apiRootCandidate = Path.Combine(_environment.ContentRootPath, "wwwroot", relativePath);
        if (System.IO.File.Exists(apiRootCandidate))
            return apiRootCandidate;

        var parent = Directory.GetParent(_environment.ContentRootPath)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            var legacyRootCandidate = Path.Combine(parent, "wwwroot", relativePath);
            if (System.IO.File.Exists(legacyRootCandidate))
                return legacyRootCandidate;
        }

        return apiRootCandidate;
    }

    /// <summary>Локальный файл, загрузка внешнего URL на сервере (CDN) или заглушка.</summary>
    private async Task<IActionResult> CoverPhysicalFileOrPlaceholderAsync(string? imagePath, CancellationToken cancellationToken)
    {
        var normalized = BookCoverWebUrl.NormalizeStoredImagePath(imagePath);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var trimmed = normalized;
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var remote) &&
                (remote.Scheme == Uri.UriSchemeHttp || remote.Scheme == Uri.UriSchemeHttps))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient("BookCoverFetch");
                    using var req = new HttpRequestMessage(HttpMethod.Get, remote);
                    req.Headers.TryAddWithoutValidation("User-Agent", BookCoverBrowserUserAgent);
                    req.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
                    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (resp.IsSuccessStatusCode)
                    {
                        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                        if (bytes.Length > 0)
                        {
                            var ct = resp.Content.Headers.ContentType?.MediaType;
                            if (string.IsNullOrWhiteSpace(ct) || ct.Contains("octet-stream", StringComparison.OrdinalIgnoreCase))
                                ct = GetImageContentTypeFromUrl(trimmed);
                            return File(bytes, ct);
                        }
                    }
                    else
                        _logger.LogWarning("Remote book cover {Url} returned status {Status}", remote, (int)resp.StatusCode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Remote book cover fetch failed for {Url}", remote);
                }
            }
            else
            {
                var fullPath = MobileBookPhysicalPathResolver.ResolveBookFullPath(_environment, normalized);
                if (!string.IsNullOrWhiteSpace(fullPath) && System.IO.File.Exists(fullPath))
                    return PhysicalFile(fullPath, GetImageContentType(fullPath));
            }
        }

        var placeholder = MobileBookPhysicalPathResolver.ResolvePlaceholderBookCoverPath(_environment);
        if (!string.IsNullOrWhiteSpace(placeholder) && System.IO.File.Exists(placeholder))
            return PhysicalFile(placeholder, "image/png");

        return NotFound();
    }

    private static string GetImageContentTypeFromUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u))
            return GetImageContentType(u.AbsolutePath);
        return "application/octet-stream";
    }

    private static string GetImageContentType(string fullPath)
    {
        var ext = Path.GetExtension(fullPath)?.ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}

