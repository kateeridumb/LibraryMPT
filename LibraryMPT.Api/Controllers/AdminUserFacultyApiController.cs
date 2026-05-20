using LibraryMPT.Data;
using LibraryMPT.Api.Extensions;
using LibraryMPT.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LibraryMPT.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/users")]
public sealed class AdminUserFacultyApiController : ControllerBase
{
    private readonly LibraryContext _context;

    public AdminUserFacultyApiController(LibraryContext context)
    {
        _context = context;
    }

    [HttpPost("{id:int}/set-faculty")]
    public async Task<ActionResult<ApiCommandResponse>> SetFaculty([FromRoute] int id, [FromBody] SetFacultyRequest request)
    {
        var roleName = await _context.Database
            .SqlQuery<string?>($"""
                SELECT r.rolename AS "Value"
                FROM users u
                JOIN roles r ON r.roleid = u.roleid
                WHERE u.userid = {id}
            """)
            .SingleOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(roleName))
            return Ok(new ApiCommandResponse { Success = false, Message = "Пользователь не найден." });
        int? facultyIdToSet = null;
        if (!string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(roleName, "Librarian", StringComparison.OrdinalIgnoreCase) &&
            request?.FacultyID.HasValue == true &&
            request.FacultyID.Value > 0)
        {
            facultyIdToSet = request.FacultyID.Value;
        }

        await _context.Database.ExecuteSqlRawAsync("""
            UPDATE users
            SET facultyid = @facultyId
            WHERE userid = @id
        """,
            new NpgsqlParameter("@facultyId", (object?)facultyIdToSet ?? DBNull.Value),
            new NpgsqlParameter("@id", id));

        return Ok(new ApiCommandResponse { Success = true, Message = "Учебное заведение обновлено." });
    }
}

