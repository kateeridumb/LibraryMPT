using LibraryMPT.Data;
using LibraryMPT.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryMPT.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/stats")]
public sealed class AdminStatsController : ControllerBase
{
    private readonly LibraryContext _context;

    public AdminStatsController(LibraryContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<LibraryMPT.Models.AdminDashboardStatsDto>> GetDashboardStats()
    {
        var row = await _context.Database
            .SqlQuery<AdminDashboardStatsSqlRow>(
                $"""
                SELECT
                    (SELECT COUNT(*)::int FROM users) AS "TotalUsers",
                    (
                        SELECT COUNT(*)::int
                        FROM users u
                        JOIN roles r ON r.roleid = u.roleid
                        WHERE r.rolename = 'Admin'
                    ) AS "AdminCount",
                    (
                        SELECT COUNT(*)::int
                        FROM users u
                        JOIN roles r ON r.roleid = u.roleid
                        WHERE r.rolename = 'Librarian'
                    ) AS "LibrarianCount",
                    (
                        SELECT COUNT(*)::int
                        FROM users u
                        JOIN roles r ON r.roleid = u.roleid
                        WHERE LOWER(TRIM(r.rolename)) IN ('student', 'reader')
                    ) AS "ReaderCount"
                """
            )
            .SingleAsync();

        return Ok(
            new LibraryMPT.Models.AdminDashboardStatsDto
            {
                TotalUsers = row.TotalUsers,
                AdminCount = row.AdminCount,
                LibrarianCount = row.LibrarianCount,
                ReaderCount = row.ReaderCount,
            }
        );
    }
}
