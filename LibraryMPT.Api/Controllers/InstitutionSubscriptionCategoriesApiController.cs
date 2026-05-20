using LibraryMPT.Api.Extensions;
using LibraryMPT.Data;
using LibraryMPT.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Npgsql;

namespace LibraryMPT.Api.Controllers;

[ApiController]
[Authorize(Roles = "InstitutionRepresentative")]
[Route("api/institution")]
public sealed class InstitutionSubscriptionCategoriesApiController : ControllerBase
{
    private readonly LibraryContext _context;
    private readonly ILogger<InstitutionSubscriptionCategoriesApiController> _logger;

    public InstitutionSubscriptionCategoriesApiController(
        LibraryContext context,
        ILogger<InstitutionSubscriptionCategoriesApiController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("subscriptions-with-categories")]
    public async Task<ActionResult<InstitutionSubscriptionsResponse>> SubscriptionsWithCategories()
    {
        var userId = User.GetUserId();
        var facultyId = await _context.Database
            .SqlQuery<int?>($"""
                SELECT facultyid AS "Value"
                FROM users
                WHERE userid = {userId}
            """)
            .SingleOrDefaultAsync();

        if (!facultyId.HasValue)
        {
            return Ok(new InstitutionSubscriptionsResponse
            {
                HasFaculty = false,
                ErrorMessage = "У вас не назначено учебное заведение"
            });
        }

        var faculty = await _context.Faculty
            .FromSqlRaw(
                "SELECT facultyid AS \"FacultyID\", facultyname AS \"FacultyName\" FROM faculty WHERE facultyid = @id",
                new NpgsqlParameter("@id", facultyId.Value))
            .AsNoTracking()
            .FirstOrDefaultAsync();

        var templates = await _context.Subscriptions
            .FromSqlRaw("""
                SELECT
                    s.subscriptionid AS "SubscriptionID",
                    s.facultyid AS "FacultyID",
                    s.name AS "Name",
                    s.startdate AS "StartDate",
                    s.enddate AS "EndDate",
                    s.durationdays AS "DurationDays",
                    s.status AS "Status",
                    s.requestedbyuserid AS "RequestedByUserID"
                FROM subscriptions s
                WHERE s.facultyid IS NULL
                ORDER BY s.durationdays
            """)
            .AsNoTracking()
            .ToListAsync();

        var mySubscriptions = await _context.Subscriptions
            .FromSqlRaw("""
                SELECT
                    s.subscriptionid AS "SubscriptionID",
                    s.facultyid AS "FacultyID",
                    s.name AS "Name",
                    s.startdate AS "StartDate",
                    s.enddate AS "EndDate",
                    s.durationdays AS "DurationDays",
                    s.status AS "Status",
                    s.requestedbyuserid AS "RequestedByUserID"
                FROM subscriptions s
                WHERE s.facultyid = @facultyId
                ORDER BY
                    CASE WHEN s.status = 'Pending' THEN 1
                         WHEN s.status = 'Approved' THEN 2
                         WHEN s.status = 'Rejected' THEN 3
                         ELSE 4 END,
                    s.startdate DESC
            """, new NpgsqlParameter("@facultyId", facultyId.Value))
            .AsNoTracking()
            .ToListAsync();
        var categories = await _context.Database
            .SqlQuery<Category>($"""
                SELECT
                    categoryid AS "CategoryID",
                    categoryname AS "CategoryName"
                FROM categories
            """)
            .AsNoTracking()
            .ToListAsync();

        return Ok(new InstitutionSubscriptionsResponse
        {
            HasFaculty = true,
            Faculty = faculty,
            Templates = templates,
            MySubscriptions = mySubscriptions,
            Categories = categories
        });
    }

    [HttpPost("select-subscription-with-category")]
    public async Task<ActionResult<ApiCommandResponse>> SelectSubscriptionWithCategory([FromBody] SelectSubscriptionRequest request)
    {
        try
        {
            var userId = User.GetUserId();

            if (request.SubscriptionId <= 0 || request.CategoryId <= 0)
            {
                return Ok(new ApiCommandResponse { Success = false, Message = "Некорректные данные подписки." });
            }

            var facultyId = await _context.Database
                .SqlQuery<int?>($"""
                    SELECT facultyid AS "Value"
                    FROM users
                    WHERE userid = {userId}
                """)
                .SingleOrDefaultAsync();

            if (!facultyId.HasValue)
            {
                return Ok(new ApiCommandResponse { Success = false, Message = "У вас не назначено учебное заведение" });
            }

            var template = await _context.Subscriptions
                .FromSqlRaw("""
                    SELECT subscriptionid AS "SubscriptionID",
                           facultyid AS "FacultyID",
                           name AS "Name",
                           startdate AS "StartDate",
                           enddate AS "EndDate",
                           durationdays AS "DurationDays",
                           status AS "Status",
                           requestedbyuserid AS "RequestedByUserID"
                    FROM subscriptions
                    WHERE subscriptionid = @id AND facultyid IS NULL
                """, new NpgsqlParameter("@id", request.SubscriptionId))
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (template == null || !template.DurationDays.HasValue)
            {
                return Ok(new ApiCommandResponse { Success = false, Message = "Шаблон подписки не найден или некорректен." });
            }

            var categoryExists = await _context.Database
                .SqlQuery<int>($"""
                    SELECT COUNT(*) AS "Value"
                    FROM categories
                    WHERE categoryid = {request.CategoryId}
                """)
                .SingleAsync();

            if (categoryExists == 0)
            {
                return Ok(new ApiCommandResponse { Success = false, Message = "Выбранная категория не найдена." });
            }

            var existingActive = await _context.Subscriptions
                .FromSqlRaw("""
                    SELECT subscriptionid AS "SubscriptionID",
                           facultyid AS "FacultyID",
                           name AS "Name",
                           startdate AS "StartDate",
                           enddate AS "EndDate",
                           durationdays AS "DurationDays",
                           status AS "Status",
                           requestedbyuserid AS "RequestedByUserID"
                    FROM subscriptions
                    WHERE facultyid = @facultyId
                      AND (status = 'Approved' OR status IS NULL)
                      AND NOW() BETWEEN startdate AND enddate
                    ORDER BY enddate DESC
                    LIMIT 1
                """, new NpgsqlParameter("@facultyId", facultyId.Value))
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (existingActive != null)
            {
                return Ok(new ApiCommandResponse
                {
                    Success = false,
                    Message = $"У вашего учебного заведения уже есть активная подписка \"{existingActive.Name}\" (до {existingActive.EndDate:dd.MM.yyyy}). Вы можете оформить новую подписку только после окончания текущей."
                });
            }

            var existingPending = await _context.Subscriptions
                .FromSqlRaw("""
                    SELECT subscriptionid AS "SubscriptionID",
                           facultyid AS "FacultyID",
                           name AS "Name",
                           startdate AS "StartDate",
                           enddate AS "EndDate",
                           durationdays AS "DurationDays",
                           status AS "Status",
                           requestedbyuserid AS "RequestedByUserID"
                    FROM subscriptions
                    WHERE facultyid = @facultyId
                      AND LOWER(TRIM(COALESCE(status, ''))) = 'pending'
                    ORDER BY subscriptionid DESC
                    LIMIT 1
                """, new NpgsqlParameter("@facultyId", facultyId.Value))
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (existingPending != null)
            {
                return Ok(new ApiCommandResponse
                {
                    Success = false,
                    Message = $"У вас уже есть заявка на подписку \"{existingPending.Name}\", ожидающая одобрения библиотекарем. Дождитесь рассмотрения заявки."
                });
            }

            var templatesCount = await _context.Database
                .SqlQuery<int>($"""SELECT COUNT(*) AS "Value" FROM subscriptions WHERE facultyid IS NULL""")
                .SingleAsync();

            if (templatesCount > 3)
            {
                var availableTemplates = await _context.Database
                    .SqlQuery<int>($"""
                        SELECT subscriptionid AS "Value"
                        FROM subscriptions
                        WHERE facultyid IS NULL
                        ORDER BY subscriptionid
                    """)
                    .ToListAsync();

                if (!availableTemplates.Contains(request.SubscriptionId))
                {
                    return Ok(new ApiCommandResponse
                    {
                        Success = false,
                        Message = "Вы можете выбрать подписку только из трех доступных вариантов."
                    });
                }
            }

            await _context.Database.ExecuteSqlRawAsync("""
                INSERT INTO subscriptions (facultyid, name, startdate, enddate, durationdays, status, requestedbyuserid)
                VALUES (@facultyId, @name, NULL, NULL, @durationDays, 'Pending', @requestedByUserId)
            """,
                new NpgsqlParameter("@facultyId", facultyId.Value),
                new NpgsqlParameter("@name", template.Name),
                new NpgsqlParameter("@durationDays", template.DurationDays.Value),
                new NpgsqlParameter("@requestedByUserId", userId));
            var createdSubscriptionId = await _context.Database
                .SqlQuery<int>($"""
                    SELECT subscriptionid AS "Value"
                    FROM subscriptions
                    WHERE facultyid = {facultyId.Value}
                      AND name = {template.Name}
                      AND durationdays = {template.DurationDays.Value}
                      AND requestedbyuserid = {userId}
                      AND LOWER(TRIM(COALESCE(status, ''))) = 'pending'
                    ORDER BY subscriptionid DESC
                    LIMIT 1
                """)
                .SingleAsync();

            try
            {
                await _context.Database.ExecuteSqlRawAsync("""
                    INSERT INTO subscriptioncategories (subscriptionid, categoryid)
                    VALUES (@subscriptionId, @categoryId)
                    ON CONFLICT DO NOTHING
                """,
                    new NpgsqlParameter("@subscriptionId", createdSubscriptionId),
                    new NpgsqlParameter("@categoryId", request.CategoryId));

                return Ok(new ApiCommandResponse
                {
                    Success = true,
                    Message = $"Заявка на подписку \"{template.Name}\" с категорией доступа отправлена библиотекарю на рассмотрение."
                });
            }
            catch
            {
                return Ok(new ApiCommandResponse
                {
                    Success = true,
                    Message = $"Заявка на подписку \"{template.Name}\" отправлена библиотекарю на рассмотрение."
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SelectSubscriptionWithCategory failed for subscriptionId={SubscriptionId}, categoryId={CategoryId}", request.SubscriptionId, request.CategoryId);
            return Ok(new ApiCommandResponse
            {
                Success = false,
                Message = "Не удалось оформить подписку из-за внутренней ошибки. Попробуйте позже."
            });
        }
    }

    [HttpGet("contract/pending")]
    public async Task<ActionResult<InstitutionContractResponse>> ContractPending()
    {
        try
        {
            var userId = User.GetUserId();

            var facultyId = await _context.Database
                .SqlQuery<int?>($"""
                    SELECT facultyid AS "Value"
                    FROM users
                    WHERE userid = {userId}
                """)
                .SingleOrDefaultAsync();

            if (!facultyId.HasValue)
            {
                return Ok(new InstitutionContractResponse { FacultyName = null });
            }

            var subscription = await _context.Subscriptions
                .FromSqlRaw("""
                    SELECT subscriptionid AS "SubscriptionID",
                           facultyid AS "FacultyID",
                           name AS "Name",
                           startdate AS "StartDate",
                           enddate AS "EndDate",
                           durationdays AS "DurationDays",
                           status AS "Status",
                           requestedbyuserid AS "RequestedByUserID"
                    FROM subscriptions
                    WHERE facultyid = @facultyId
                      AND LOWER(TRIM(COALESCE(status, ''))) = 'pending'
                    ORDER BY subscriptionid DESC
                    LIMIT 1
                """, new NpgsqlParameter("@facultyId", facultyId.Value))
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (subscription == null || !subscription.DurationDays.HasValue)
            {
                return Ok(new InstitutionContractResponse { FacultyName = null });
            }

            var facultyName = await _context.Database
                .SqlQuery<string?>($"""
                    SELECT facultyname AS "Value"
                    FROM faculty
                    WHERE facultyid = {facultyId.Value}
                    LIMIT 1
                """)
                .SingleOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(facultyName))
            {
                return Ok(new InstitutionContractResponse { FacultyName = null });
            }

            var representativeUsername = await _context.Database
                .SqlQuery<string?>($"""
                    SELECT username AS "Value"
                    FROM users
                    WHERE userid = {userId}
                    LIMIT 1
                """)
                .SingleOrDefaultAsync();

            var representativeEmail = await _context.Database
                .SqlQuery<string?>($"""
                    SELECT email AS "Value"
                    FROM users
                    WHERE userid = {userId}
                    LIMIT 1
                """)
                .SingleOrDefaultAsync();

            var representativeFirstName = await _context.Database
                .SqlQuery<string?>($"""
                    SELECT firstname AS "Value"
                    FROM users
                    WHERE userid = {userId}
                    LIMIT 1
                """)
                .SingleOrDefaultAsync();

            List<Category> categories;
            try
            {
                var categoriesAllowed = await _context.Database
                    .SqlQuery<int>($"""
                        SELECT DISTINCT categoryid AS "Value"
                        FROM subscriptioncategories
                        WHERE subscriptionid = {subscription.SubscriptionID}
                    """)
                    .ToListAsync();

                categories = categoriesAllowed.Any()
                    ? await _context.Categories
                        .Where(c => categoriesAllowed.Contains(c.CategoryID))
                        .AsNoTracking()
                        .ToListAsync()
                    : new List<Category>();
            }
            catch
            {
                categories = new List<Category>();
            }

            var studentsCount = await _context.Database
                .SqlQuery<int>($"""
                    SELECT COUNT(*) AS "Value"
                    FROM users u
                    JOIN roles r ON r.roleid = u.roleid
                    WHERE u.facultyid = {facultyId.Value}
                      AND r.rolename = 'Student'
                """)
                .SingleAsync();

            var startDate = DateTime.Today;
            var endDate = startDate.AddDays(subscription.DurationDays.Value);

            var fullName = string.Join(" ",
                new[] { representativeFirstName, representativeUsername }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(fullName))
            {
                fullName = null;
            }

            return Ok(new InstitutionContractResponse
            {
                SubscriptionId = subscription.SubscriptionID,
                FacultyName = facultyName,
                RepresentativeEmail = representativeEmail,
                RepresentativeFullName = fullName,
                SubscriptionName = subscription.Name,
                StudentsCount = studentsCount,
                Categories = categories,
                StartDate = startDate,
                EndDate = endDate
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ContractPending failed for institution representative userId={UserId}", User.GetUserId());
            return Ok(new InstitutionContractResponse { FacultyName = null });
        }
    }
}

