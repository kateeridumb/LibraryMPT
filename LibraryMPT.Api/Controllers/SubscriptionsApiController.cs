using LibraryMPT.Data;
using LibraryMPT.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using Microsoft.EntityFrameworkCore;

namespace LibraryMPT.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/subscriptions")]
public sealed class SubscriptionsApiController : ControllerBase
{
    private readonly LibraryContext _context;

    public SubscriptionsApiController(LibraryContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<Subscription>>> GetAll()
    {
        var subscriptions = await _context.Subscriptions
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
                ORDER BY s.facultyid, s.name
            """)
            .AsNoTracking()
            .ToListAsync();

        var facultyIds = subscriptions
            .Where(s => s.FacultyID.HasValue)
            .Select(s => s.FacultyID!.Value)
            .Distinct()
            .ToList();

        // IN ({0}, join) через FromSqlRaw даёт один параметр со строкой "1,2,3" — в PostgreSQL это неверно.
        var faculties = new List<Faculty>();
        if (facultyIds.Count > 0)
        {
            var fidsParam = new NpgsqlParameter("@fids", NpgsqlDbType.Array | NpgsqlDbType.Integer)
            {
                Value = facultyIds.ToArray()
            };
            faculties = await _context.Faculty
                .FromSqlRaw("""
                    SELECT facultyid AS "FacultyID", facultyname AS "FacultyName"
                    FROM faculty
                    WHERE facultyid = ANY(@fids)
                    """, fidsParam)
                .AsNoTracking()
                .ToListAsync();
        }

        foreach (var sub in subscriptions.Where(s => s.FacultyID.HasValue))
        {
            sub.Faculty = faculties.FirstOrDefault(f => f.FacultyID == sub.FacultyID!.Value);
        }

        return Ok(subscriptions);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Subscription>> GetById(int id)
    {
        var subscription = await _context.Subscriptions
            .FromSqlRaw("SELECT subscriptionid AS \"SubscriptionID\", facultyid AS \"FacultyID\", name AS \"Name\", startdate AS \"StartDate\", enddate AS \"EndDate\", durationdays AS \"DurationDays\", status AS \"Status\", requestedbyuserid AS \"RequestedByUserID\" FROM subscriptions WHERE subscriptionid = @id",
                new NpgsqlParameter("@id", id))
            .AsNoTracking()
            .FirstOrDefaultAsync();

        if (subscription == null)
        {
            return NotFound();
        }

        if (subscription.FacultyID > 0)
        {
            subscription.Faculty = await _context.Faculty
                .FromSqlRaw("SELECT facultyid AS \"FacultyID\", facultyname AS \"FacultyName\" FROM faculty WHERE facultyid = @id",
                    new NpgsqlParameter("@id", subscription.FacultyID))
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }

        return Ok(subscription);
    }

    [HttpPost("template")]
    public async Task<IActionResult> CreateTemplate([FromBody] Subscription subscription)
    {
        await _context.Database.ExecuteSqlRawAsync("""
            INSERT INTO subscriptions (facultyid, name, startdate, enddate, durationdays)
            VALUES (@facultyId, @name, @startDate, @endDate, @durationDays)
        """,
            new NpgsqlParameter("@facultyId", DBNull.Value),
            new NpgsqlParameter("@name", subscription.Name.Trim()),
            new NpgsqlParameter("@startDate", DBNull.Value),
            new NpgsqlParameter("@endDate", DBNull.Value),
            new NpgsqlParameter("@durationDays", (object?)subscription.DurationDays ?? DBNull.Value)
        );

        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM subscriptions WHERE subscriptionid = @id",
            new NpgsqlParameter("@id", id)
        );

        return Ok();
    }
}

