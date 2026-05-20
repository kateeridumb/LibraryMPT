using LibraryMPT.Api.Extensions;
using LibraryMPT.Data;
using LibraryMPT.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LibraryMPT.Api.Controllers;

[ApiController]
[Authorize(Roles = "InstitutionRepresentative")]
[Route("api/institution")]
public sealed class InstitutionApiController : ControllerBase
{
    private readonly LibraryContext _context;
    private readonly ILogger<InstitutionApiController> _logger;

    public InstitutionApiController(
        LibraryContext context,
        ILogger<InstitutionApiController> logger
    )
    {
        _context = context;
        _logger = logger;
    }

    private static string EscapeForSqlLikeLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private const string SubscriptionSelectList =
        "subscriptionid AS \"SubscriptionID\", facultyid AS \"FacultyID\", name AS \"Name\", startdate AS \"StartDate\", enddate AS \"EndDate\", durationdays AS \"DurationDays\", status AS \"Status\", requestedbyuserid AS \"RequestedByUserID\"";

    [HttpGet("index")]
    public async Task<ActionResult<InstitutionIndexResponse>> Index()
    {
        var userId = User.GetUserId();
        if (userId <= 0)
        {
            return Ok(
                new InstitutionIndexResponse
                {
                    HasFaculty = false,
                    ErrorMessage = "Не удалось определить пользователя.",
                }
            );
        }

        var facultyId = await _context
            .Database.SqlQuery<int?>(
                $"""
                SELECT facultyid AS "Value"
                FROM users
                WHERE userid = {userId}
                """
            )
            .SingleOrDefaultAsync();
        if (!facultyId.HasValue)
        {
            return Ok(
                new InstitutionIndexResponse
                {
                    HasFaculty = false,
                    ErrorMessage = "У вас не назначено учебное заведение",
                }
            );
        }
        var faculty = await _context
            .Faculty.FromSqlRaw(
                "SELECT facultyid AS \"FacultyID\", facultyname AS \"FacultyName\" FROM faculty WHERE facultyid = @id",
                new NpgsqlParameter("@id", facultyId.Value)
            )
            .AsNoTracking()
            .FirstOrDefaultAsync();
        Subscription? activeSubscription;
        try
        {
            activeSubscription = await _context
                .Subscriptions.FromSqlRaw(
                    $"""
                    SELECT {SubscriptionSelectList}
                    FROM subscriptions
                    WHERE facultyid = @facultyId
                      AND (status = 'Approved' OR status IS NULL)
                      AND startdate IS NOT NULL
                      AND enddate IS NOT NULL
                      AND NOW() BETWEEN startdate AND enddate
                    ORDER BY enddate DESC
                    """,
                    new NpgsqlParameter("@facultyId", facultyId.Value)
                )
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Index fallback query for subscription.");
            activeSubscription = await _context
                .Subscriptions.FromSqlRaw(
                    $"""
                    SELECT subscriptionid AS "SubscriptionID", facultyid AS "FacultyID", name AS "Name", startdate AS "StartDate", enddate AS "EndDate",
                        CAST(NULL AS INT) AS "DurationDays",
                        CAST(NULL AS text) AS "Status",
                        CAST(NULL AS INT) AS "RequestedByUserID"
                    FROM subscriptions
                    WHERE facultyid = @facultyId
                      AND startdate IS NOT NULL
                      AND enddate IS NOT NULL
                      AND NOW() BETWEEN startdate AND enddate
                    ORDER BY enddate DESC
                    """,
                    new NpgsqlParameter("@facultyId", facultyId.Value)
                )
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }
        var totalStudents = 0;
        var totalDownloads = 0;
        var totalReads = 0;
        try
        {
            totalStudents = await _context
                .Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*) AS "Value"
                    FROM users u
                    JOIN roles r ON r.roleid = u.roleid
                    WHERE u.facultyid = {facultyId.Value}
                      AND LOWER(TRIM(r.rolename)) = 'student'
                    """
                )
                .SingleAsync();
            totalDownloads = await _context
                .Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*) AS "Value"
                    FROM booklogs bl
                    JOIN users u ON u.userid = bl.userid
                    JOIN roles r ON r.roleid = u.roleid
                    WHERE u.facultyid = {facultyId.Value}
                      AND LOWER(TRIM(r.rolename)) = 'student'
                      AND UPPER(TRIM(COALESCE(bl.actiontype, ''))) = 'DOWNLOAD'
                    """
                )
                .SingleAsync();
            totalReads = await _context
                .Database.SqlQuery<int>(
                    $"""
                    SELECT COUNT(*) AS "Value"
                    FROM booklogs bl
                    JOIN users u ON u.userid = bl.userid
                    JOIN roles r ON r.roleid = u.roleid
                    WHERE u.facultyid = {facultyId.Value}
                      AND LOWER(TRIM(r.rolename)) = 'student'
                      AND UPPER(TRIM(COALESCE(bl.actiontype, ''))) = 'READ'
                    """
                )
                .SingleAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Index fallback stats with zeros.");
        }
        return Ok(
            new InstitutionIndexResponse
            {
                HasFaculty = true,
                Faculty = faculty,
                FacultyId = facultyId.Value,
                ActiveSubscription = activeSubscription,
                TotalStudents = totalStudents,
                TotalDownloads = totalDownloads,
                TotalReads = totalReads,
            }
        );
    }

    [HttpGet("subscriptions")]
    public async Task<ActionResult<InstitutionSubscriptionsResponse>> Subscriptions()
    {
        var userId = User.GetUserId();
        if (userId <= 0)
        {
            return Ok(
                new InstitutionSubscriptionsResponse
                {
                    HasFaculty = false,
                    ErrorMessage = "Не удалось определить пользователя.",
                }
            );
        }

        var facultyId = await _context
            .Database.SqlQuery<int?>(
                $"""
                SELECT facultyid AS "Value"
                FROM users
                WHERE userid = {userId}
                """
            )
            .SingleOrDefaultAsync();
        if (!facultyId.HasValue)
        {
            return Ok(
                new InstitutionSubscriptionsResponse
                {
                    HasFaculty = false,
                    ErrorMessage = "У вас не назначено учебное заведение",
                }
            );
        }
        List<Subscription> templates;
        try
        {
            templates = await _context
                .Subscriptions.FromSqlRaw(
                    $"""
                    SELECT {SubscriptionSelectList}
                    FROM subscriptions s
                    WHERE s.facultyid IS NULL
                    ORDER BY s.durationdays
                    """
                )
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subscriptions fallback templates.");
            templates = await _context
                .Subscriptions.FromSqlRaw(
                    $"""
                    SELECT s.subscriptionid AS "SubscriptionID", s.facultyid AS "FacultyID", s.name AS "Name", s.startdate AS "StartDate", s.enddate AS "EndDate",
                        CAST(NULL AS INT) AS "DurationDays",
                        CAST(NULL AS text) AS "Status",
                        CAST(NULL AS INT) AS "RequestedByUserID"
                    FROM subscriptions s
                    WHERE s.facultyid IS NULL
                    ORDER BY s.subscriptionid
                    """
                )
                .AsNoTracking()
                .ToListAsync();
        }
        List<Subscription> mySubscriptions;
        try
        {
            mySubscriptions = await _context
                .Subscriptions.FromSqlRaw(
                    $"""
                    SELECT {SubscriptionSelectList}
                    FROM subscriptions s
                    WHERE s.facultyid = @facultyId
                    ORDER BY
                        CASE WHEN s.status = 'Pending' THEN 1
                             WHEN s.status = 'Approved' THEN 2
                             WHEN s.status = 'Rejected' THEN 3
                             ELSE 4 END,
                        s.startdate DESC
                    """,
                    new NpgsqlParameter("@facultyId", facultyId.Value)
                )
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subscriptions fallback mySubscriptions.");
            mySubscriptions = await _context
                .Subscriptions.FromSqlRaw(
                    $"""
                    SELECT s.subscriptionid AS "SubscriptionID", s.facultyid AS "FacultyID", s.name AS "Name", s.startdate AS "StartDate", s.enddate AS "EndDate",
                        CAST(NULL AS INT) AS "DurationDays",
                        CAST(NULL AS text) AS "Status",
                        CAST(NULL AS INT) AS "RequestedByUserID"
                    FROM subscriptions s
                    WHERE s.facultyid = @facultyId
                    ORDER BY s.startdate DESC
                    """,
                    new NpgsqlParameter("@facultyId", facultyId.Value)
                )
                .AsNoTracking()
                .ToListAsync();
        }
        var faculty = await _context
            .Faculty.FromSqlRaw(
                "SELECT facultyid AS \"FacultyID\", facultyname AS \"FacultyName\" FROM faculty WHERE facultyid = @id",
                new NpgsqlParameter("@id", facultyId.Value)
            )
            .AsNoTracking()
            .FirstOrDefaultAsync();
        return Ok(
            new InstitutionSubscriptionsResponse
            {
                HasFaculty = true,
                Faculty = faculty,
                Templates = templates,
                MySubscriptions = mySubscriptions,
            }
        );
    }

    [HttpPost("select-subscription")]
    public async Task<ActionResult<ApiCommandResponse>> SelectSubscription(
        [FromBody] SelectSubscriptionRequest request
    )
    {
        var userId = User.GetUserId();
        var subscriptionId = request.SubscriptionId;
        if (userId <= 0)
        {
            return Ok(
                new ApiCommandResponse
                {
                    Success = false,
                    Message = "Не удалось определить пользователя.",
                }
            );
        }

        var facultyId = await _context
            .Database.SqlQuery<int?>(
                $"""
                SELECT facultyid AS "Value"
                FROM users
                WHERE userid = {userId}
                """
            )
            .SingleOrDefaultAsync();
        if (!facultyId.HasValue)
        {
            return Ok(
                new ApiCommandResponse
                {
                    Success = false,
                    Message = "У вас не назначено учебное заведение",
                }
            );
        }
        Subscription? template;
        try
        {
            template = await _context
                .Subscriptions.FromSqlRaw(
                    $"""
                    SELECT {SubscriptionSelectList}
                    FROM subscriptions
                    WHERE subscriptionid = @id AND facultyid IS NULL
                    """,
                    new NpgsqlParameter("@id", subscriptionId)
                )
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SelectSubscription fallback template.");
            template = await _context
                .Subscriptions.FromSqlRaw(
                    $"""
                    SELECT subscriptionid AS "SubscriptionID", facultyid AS "FacultyID", name AS "Name", startdate AS "StartDate", enddate AS "EndDate",
                        CAST(NULL AS INT) AS "DurationDays",
                        CAST(NULL AS text) AS "Status",
                        CAST(NULL AS INT) AS "RequestedByUserID"
                    FROM subscriptions
                    WHERE subscriptionid = @id AND facultyid IS NULL
                    """,
                    new NpgsqlParameter("@id", subscriptionId)
                )
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }
        if (template == null || !template.DurationDays.HasValue)
        {
            return Ok(
                new ApiCommandResponse
                {
                    Success = false,
                    Message = "Шаблон подписки не найден или некорректен",
                }
            );
        }
        var existingActive = await _context
            .Subscriptions.FromSqlRaw(
                $"""
                SELECT {SubscriptionSelectList}
                FROM subscriptions
                WHERE facultyid = @facultyId
                  AND (status = 'Approved' OR status IS NULL)
                  AND NOW() BETWEEN startdate AND enddate
                """,
                new NpgsqlParameter("@facultyId", facultyId.Value)
            )
            .AsNoTracking()
            .FirstOrDefaultAsync();
        if (existingActive != null)
        {
            return Ok(
                new ApiCommandResponse
                {
                    Success = false,
                    Message =
                        $"У вашего учебного заведения уже есть активная подписка \"{existingActive.Name}\" (до {existingActive.EndDate:dd.MM.yyyy}). Вы можете оформить новую подписку только после окончания текущей.",
                }
            );
        }
        var existingPending = await _context
            .Subscriptions.FromSqlRaw(
                $"""
                SELECT {SubscriptionSelectList}
                FROM subscriptions
                WHERE facultyid = @facultyId
                  AND status = 'Pending'
                """,
                new NpgsqlParameter("@facultyId", facultyId.Value)
            )
            .AsNoTracking()
            .FirstOrDefaultAsync();
        if (existingPending != null)
        {
            return Ok(
                new ApiCommandResponse
                {
                    Success = false,
                    Message =
                        $"У вас уже есть заявка на подписку \"{existingPending.Name}\", ожидающая одобрения библиотекарем. Дождитесь рассмотрения заявки.",
                }
            );
        }
        var templatesCount = await _context
            .Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS \"Value\" FROM subscriptions WHERE facultyid IS NULL"
            )
            .SingleAsync();
        if (templatesCount > 3)
        {
            var availableTemplates = await _context
                .Subscriptions.FromSqlRaw(
                    "SELECT subscriptionid AS \"SubscriptionID\", facultyid AS \"FacultyID\", name AS \"Name\", startdate AS \"StartDate\", enddate AS \"EndDate\", durationdays AS \"DurationDays\", status AS \"Status\", requestedbyuserid AS \"RequestedByUserID\" FROM subscriptions WHERE facultyid IS NULL ORDER BY subscriptionid"
                )
                .AsNoTracking()
                .Select(s => s.SubscriptionID)
                .ToListAsync();
            if (!availableTemplates.Contains(subscriptionId))
            {
                return Ok(
                    new ApiCommandResponse
                    {
                        Success = false,
                        Message = "Вы можете выбрать подписку только из трех доступных вариантов.",
                    }
                );
            }
        }
        try
        {
            await _context.Database.ExecuteSqlRawAsync(
                @"INSERT INTO subscriptions (facultyid, name, startdate, enddate, durationdays, status, requestedbyuserid)
VALUES (@facultyId, @name, NULL, NULL, @durationDays, 'Pending', @requestedByUserId)",
                new NpgsqlParameter("@facultyId", facultyId.Value),
                new NpgsqlParameter("@name", template.Name),
                new NpgsqlParameter("@durationDays", template.DurationDays.Value),
                new NpgsqlParameter("@requestedByUserId", userId)
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SelectSubscription fallback insert.");
            var days = template.DurationDays.Value;
            await _context.Database.ExecuteSqlRawAsync(
                @"INSERT INTO subscriptions (facultyid, name, startdate, enddate)
VALUES (@facultyId, @name, NOW(), NOW() + (@durationDays || ' days')::interval)",
                new NpgsqlParameter("@facultyId", facultyId.Value),
                new NpgsqlParameter("@name", template.Name),
                new NpgsqlParameter("@durationDays", days)
            );
            return Ok(
                new ApiCommandResponse
                {
                    Success = true,
                    Message =
                        $"Подписка \"{template.Name}\" активирована сразу (старый формат базы данных).",
                }
            );
        }
        return Ok(
            new ApiCommandResponse
            {
                Success = true,
                Message =
                    $"Заявка на подписку \"{template.Name}\" успешно отправлена библиотекарю на рассмотрение. После одобрения подписка будет активирована для вашего учебного заведения.",
            }
        );
    }

    [HttpGet("student-statistics")]
    public async Task<ActionResult<InstitutionStudentStatsResponse>> StudentStatistics(
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir
    )
    {
        var userId = User.GetUserId();
        if (userId <= 0)
        {
            return Ok(
                new InstitutionStudentStatsResponse
                {
                    HasFaculty = false,
                    ErrorMessage = "Не удалось определить пользователя.",
                }
            );
        }

        var facultyId = await _context
            .Database.SqlQuery<int?>(
                $"""
                SELECT facultyid AS "Value"
                FROM users
                WHERE userid = {userId}
                """
            )
            .SingleOrDefaultAsync();
        if (!facultyId.HasValue)
        {
            return Ok(
                new InstitutionStudentStatsResponse
                {
                    HasFaculty = false,
                    ErrorMessage = "У вас не назначено учебное заведение",
                }
            );
        }
        var normalizedSortBy = (sortBy ?? "downloads").Trim().ToLowerInvariant();
        var normalizedSortDir =
            (sortDir ?? "desc").Trim().ToLowerInvariant() == "asc" ? "ASC" : "DESC";
        var orderColumn = normalizedSortBy switch
        {
            "reads" => "\"TotalReads\"",
            "books" => "\"TotalBooksRead\"",
            "last" => "\"LastActivityDate\"",
            "name" => "\"LastName\"",
            "user" => "\"Username\"",
            _ => "\"TotalDownloads\"",
        };
        var orderBySql = $"{orderColumn} {normalizedSortDir}";
        var trimmedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        List<StudentStatsDto> students;
        try
        {
            var likePat = string.IsNullOrEmpty(trimmedSearch)
                ? string.Empty
                : EscapeForSqlLikeLiteral(trimmedSearch);
            var searchCondition = string.IsNullOrEmpty(trimmedSearch)
                ? "TRUE"
                : $"u.username ILIKE '%{likePat}%' OR u.firstname ILIKE '%{likePat}%' OR convert_from(u.lastname, 'UTF8') ILIKE '%{likePat}%'";
            var studentStatsSql =
                """
                SELECT
                    u.userid AS "UserID",
                    COALESCE(u.username, '') AS "Username",
                    COALESCE(u.firstname, '') AS "FirstName",
                    COALESCE(convert_from(u.lastname, 'UTF8'), '') AS "LastName",
                    (SELECT COUNT(*) FROM booklogs bl WHERE bl.userid = u.userid AND UPPER(TRIM(COALESCE(bl.actiontype, ''))) = 'DOWNLOAD') AS "TotalDownloads",
                    (SELECT COUNT(*) FROM booklogs bl WHERE bl.userid = u.userid AND UPPER(TRIM(COALESCE(bl.actiontype, ''))) = 'READ') AS "TotalReads",
                    (SELECT COUNT(DISTINCT bookid) FROM booklogs bl WHERE bl.userid = u.userid AND UPPER(TRIM(COALESCE(bl.actiontype, ''))) = 'READ') AS "TotalBooksRead",
                    (SELECT MAX(actionat) FROM booklogs bl WHERE bl.userid = u.userid) AS "LastActivityDate"
                FROM users u
                JOIN roles r ON r.roleid = u.roleid
                WHERE u.facultyid = {0}
                  AND LOWER(TRIM(r.rolename)) = 'student'
                  AND (
                """
                + searchCondition
                + ") ORDER BY "
                + orderBySql
                + ";";
            students = await _context
                .Database.SqlQueryRaw<StudentStatsDto>(studentStatsSql, facultyId.Value)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StudentStatistics fallback no decrypt.");
            var likePatFb = string.IsNullOrEmpty(trimmedSearch)
                ? string.Empty
                : EscapeForSqlLikeLiteral(trimmedSearch);
            var searchCondition = string.IsNullOrEmpty(trimmedSearch)
                ? "TRUE"
                : $"u.username ILIKE '%{likePatFb}%' OR u.firstname ILIKE '%{likePatFb}%'";
            var studentStatsSql =
                """
                SELECT
                    u.userid AS "UserID",
                    COALESCE(u.username, '') AS "Username",
                    COALESCE(u.firstname, '') AS "FirstName",
                    CAST('' AS text) AS "LastName",
                    (SELECT COUNT(*) FROM booklogs bl WHERE bl.userid = u.userid AND UPPER(TRIM(COALESCE(bl.actiontype, ''))) = 'DOWNLOAD') AS "TotalDownloads",
                    (SELECT COUNT(*) FROM booklogs bl WHERE bl.userid = u.userid AND UPPER(TRIM(COALESCE(bl.actiontype, ''))) = 'READ') AS "TotalReads",
                    (SELECT COUNT(DISTINCT bookid) FROM booklogs bl WHERE bl.userid = u.userid AND UPPER(TRIM(COALESCE(bl.actiontype, ''))) = 'READ') AS "TotalBooksRead",
                    (SELECT MAX(actionat) FROM booklogs bl WHERE bl.userid = u.userid) AS "LastActivityDate"
                FROM users u
                JOIN roles r ON r.roleid = u.roleid
                WHERE u.facultyid = {0}
                  AND LOWER(TRIM(r.rolename)) = 'student'
                  AND (
                """
                + searchCondition
                + ") ORDER BY "
                + orderBySql
                + ";";
            students = await _context
                .Database.SqlQueryRaw<StudentStatsDto>(studentStatsSql, facultyId.Value)
                .ToListAsync();
        }
        var faculty = await _context
            .Faculty.FromSqlRaw(
                "SELECT facultyid AS \"FacultyID\", facultyname AS \"FacultyName\" FROM faculty WHERE facultyid = @id",
                new NpgsqlParameter("@id", facultyId.Value)
            )
            .AsNoTracking()
            .FirstOrDefaultAsync();
        return Ok(
            new InstitutionStudentStatsResponse
            {
                HasFaculty = true,
                Faculty = faculty,
                Search = trimmedSearch,
                SortBy = normalizedSortBy,
                SortDir = normalizedSortDir.ToLowerInvariant(),
                Students = students,
            }
        );
    }

    [HttpGet("book-statistics")]
    public async Task<ActionResult<InstitutionBookStatsResponse>> BookStatistics(
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir
    )
    {
        var userId = User.GetUserId();
        if (userId <= 0)
        {
            return Ok(
                new InstitutionBookStatsResponse
                {
                    HasFaculty = false,
                    ErrorMessage = "Не удалось определить пользователя.",
                }
            );
        }

        var facultyId = await _context
            .Database.SqlQuery<int?>(
                $"""
                SELECT facultyid AS "Value"
                FROM users
                WHERE userid = {userId}
                """
            )
            .SingleOrDefaultAsync();
        if (!facultyId.HasValue)
        {
            return Ok(
                new InstitutionBookStatsResponse
                {
                    HasFaculty = false,
                    ErrorMessage = "У вас не назначено учебное заведение",
                }
            );
        }
        var normalizedSortBy = (sortBy ?? "downloads").Trim().ToLowerInvariant();
        var normalizedSortDir =
            (sortDir ?? "desc").Trim().ToLowerInvariant() == "asc" ? "ASC" : "DESC";
        var orderColumn = normalizedSortBy switch
        {
            "reads" => "\"TotalReads\"",
            "unique" => "\"UniqueDownloaders\"",
            "title" => "\"Title\"",
            _ => "\"TotalDownloads\"",
        };
        var orderBySql = $"{orderColumn} {normalizedSortDir}";
        var trimmedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var likeBooks = string.IsNullOrEmpty(trimmedSearch)
            ? string.Empty
            : EscapeForSqlLikeLiteral(trimmedSearch);
        var searchCondition = string.IsNullOrEmpty(trimmedSearch)
            ? "TRUE"
            : $"b.title ILIKE '%{likeBooks}%'";
        var bookStatsSql =
            """
            SELECT
                b.bookid AS "BookID",
                b.title AS "Title",
                (SELECT COUNT(*) FROM booklogs bl
                 JOIN users u ON u.userid = bl.userid
                 WHERE bl.bookid = b.bookid
                   AND u.facultyid = {0}
                   AND UPPER(TRIM(COALESCE(bl.actiontype, ''))) = 'DOWNLOAD') AS "TotalDownloads",
                (SELECT COUNT(*) FROM booklogs bl
                 JOIN users u ON u.userid = bl.userid
                 WHERE bl.bookid = b.bookid
                   AND u.facultyid = {0}
                   AND UPPER(TRIM(COALESCE(bl.actiontype, ''))) = 'READ') AS "TotalReads",
                (SELECT COUNT(DISTINCT u.userid) FROM booklogs bl
                 JOIN users u ON u.userid = bl.userid
                 WHERE bl.bookid = b.bookid
                   AND u.facultyid = {0}
                   AND UPPER(TRIM(COALESCE(bl.actiontype, ''))) = 'DOWNLOAD') AS "UniqueDownloaders"
            FROM books b
            WHERE EXISTS (
                SELECT 1 FROM booklogs bl
                JOIN users u ON u.userid = bl.userid
                WHERE bl.bookid = b.bookid
                  AND u.facultyid = {0}
            )
              AND (
            """
            + searchCondition
            + ") ORDER BY "
            + orderBySql;
        var bookStats = await _context
            .Database.SqlQueryRaw<BookStatisticsDto>(bookStatsSql, facultyId.Value)
            .ToListAsync();
        var faculty = await _context
            .Faculty.FromSqlRaw(
                "SELECT facultyid AS \"FacultyID\", facultyname AS \"FacultyName\" FROM faculty WHERE facultyid = @id",
                new NpgsqlParameter("@id", facultyId.Value)
            )
            .AsNoTracking()
            .FirstOrDefaultAsync();
        return Ok(
            new InstitutionBookStatsResponse
            {
                HasFaculty = true,
                Faculty = faculty,
                Search = trimmedSearch,
                SortBy = normalizedSortBy,
                SortDir = normalizedSortDir.ToLowerInvariant(),
                BookStats = bookStats,
            }
        );
    }
}
