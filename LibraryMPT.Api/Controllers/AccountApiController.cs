using LibraryMPT.Data;
using LibraryMPT.Api.Extensions;
using LibraryMPT.Api.Helpers;
using LibraryMPT.Models;
using LibraryMPT.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace LibraryMPT.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/account")]
public sealed class AccountApiController : ControllerBase
{
    private readonly LibraryContext _context;
    private readonly IConfiguration _configuration;
    private readonly EmailService _emailService;

    public AccountApiController(LibraryContext context, IConfiguration configuration, EmailService emailService)
    {
        _context = context;
        _configuration = configuration;
        _emailService = emailService;
    }

    [HttpGet("roles")]
    public async Task<ActionResult<List<Role>>> GetRoles()
    {
        var roles = await _context.Roles
            .FromSqlRaw("SELECT roleid AS \"RoleID\", rolename AS \"RoleName\" FROM roles")
            .AsNoTracking()
            .ToListAsync();
        return Ok(roles);
    }

    [HttpPost("forgot-password")]
    public async Task<ActionResult<ForgotPasswordResult>> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var userId = await _context.Database
            .SqlQuery<int?>($"SELECT userid AS \"Value\" FROM users WHERE email = '{request.Email ?? string.Empty}'")
            .SingleOrDefaultAsync();
        if (!userId.HasValue)
        {
            return Ok(new ForgotPasswordResult { UserExists = false });
        }
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
        var expiryDate = DateTime.UtcNow.AddHours(24);
        await _context.Database.ExecuteSqlRawAsync("""
            UPDATE users
            SET passwordresettoken = @token,
                passwordresettokenexpiry = @expiry
            WHERE userid = @userId
            """,
            new NpgsqlParameter("@token", token),
            new NpgsqlParameter("@expiry", expiryDate),
            new NpgsqlParameter("@userId", userId.Value));

        if (request.SendEmail)
        {
            var baseUrl = (_configuration["LibraryWeb:PublicBaseUrl"] ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                var corsOrigins = _configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
                baseUrl = corsOrigins?.FirstOrDefault(static o => !string.IsNullOrWhiteSpace(o))?.TrimEnd('/') ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return Ok(new ForgotPasswordResult
                {
                    UserExists = true,
                    EmailSent = false,
                    Error = "Не настроен публичный URL сайта для ссылки восстановления: задайте LibraryWeb:PublicBaseUrl или хотя бы один адрес в Cors:AllowedOrigins."
                });
            }

            var resetLink = $"{baseUrl}/Account/ResetPassword?token={Uri.EscapeDataString(token)}";
            try
            {
                await _emailService.SendPasswordResetEmailAsync(request.Email.Trim(), resetLink);
                return Ok(new ForgotPasswordResult { UserExists = true, EmailSent = true });
            }
            catch (Exception ex)
            {
                return Ok(new ForgotPasswordResult
                {
                    UserExists = true,
                    EmailSent = false,
                    Error = $"Ошибка при отправке письма: {ex.Message}"
                });
            }
        }

        return Ok(new ForgotPasswordResult
        {
            UserExists = true,
            UserId = userId.Value,
            Token = token
        });
    }

    [HttpGet("validate-reset-token")]
    public async Task<ActionResult<ApiCommandResponse>> ValidateResetToken([FromQuery] string token)
    {
        var userId = await _context.Database
            .SqlQuery<int?>($"SELECT userid AS \"Value\" FROM users WHERE passwordresettoken = '{token ?? string.Empty}' AND passwordresettokenexpiry > NOW()")
            .SingleOrDefaultAsync();
        return Ok(new ApiCommandResponse { Success = userId.HasValue });
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult<ApiCommandResponse>> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var userId = await _context.Database
            .SqlQuery<int?>($"SELECT userid AS \"Value\" FROM users WHERE passwordresettoken = '{request.Token ?? string.Empty}' AND passwordresettokenexpiry > NOW()")
            .SingleOrDefaultAsync();
        if (!userId.HasValue)
        {
            return Ok(new ApiCommandResponse { Success = false, Message = "Ссылка недействительна или устарела." });
        }
        CreatePasswordHash(request.Password, out var hash, out var salt);
        await _context.Database.ExecuteSqlRawAsync("""
            UPDATE users
            SET passwordhash = @hash,
                passwordsalt = @salt,
                passwordresettoken = NULL,
                passwordresettokenexpiry = NULL
            WHERE userid = @userId
            """,
            new NpgsqlParameter("@hash", hash),
            new NpgsqlParameter("@salt", salt),
            new NpgsqlParameter("@userId", userId.Value));
        return Ok(new ApiCommandResponse { Success = true });
    }

    [HttpPost("register")]
    public async Task<ActionResult<ApiCommandResponse>> Register([FromBody] AccountRegisterRequest request)
    {
        var roles = await _context.Roles
            .FromSqlRaw("SELECT roleid AS \"RoleID\", rolename AS \"RoleName\" FROM roles")
            .AsNoTracking()
            .ToListAsync();
        const string studentRoleName = "Student";
        const string institutionRoleName = "InstitutionRepresentative";
        var requested = (request.RegistrationRole ?? string.Empty).Trim();
        var roleToAssign = string.Equals(requested, institutionRoleName, StringComparison.OrdinalIgnoreCase)
            ? roles.FirstOrDefault(r => r.RoleName == institutionRoleName)
            : roles.FirstOrDefault(r => r.RoleName == studentRoleName);
        if (roleToAssign == null)
        {
            return Ok(new ApiCommandResponse { Success = false, Message = "Не найдена роль для регистрации." });
        }
        int? facultyId = null;
        if (string.Equals(roleToAssign.RoleName, institutionRoleName, StringComparison.OrdinalIgnoreCase))
        {
            var normalizedFaculty = (request.FacultyName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedFaculty))
            {
                return Ok(new ApiCommandResponse
                {
                    Success = false,
                    Message = "Для представителя необходимо указать название учебного заведения."
                });
            }
            if (normalizedFaculty.Length > 200)
            {
                return Ok(new ApiCommandResponse
                {
                    Success = false,
                    Message = "Название учебного заведения не должно превышать 200 символов."
                });
            }

            facultyId = await _context.Database
                .SqlQuery<int?>($"""
                    SELECT facultyid AS "Value"
                    FROM faculty
                    WHERE LOWER(TRIM(facultyname)) = LOWER(TRIM({normalizedFaculty}))
                    LIMIT 1
                """)
                .SingleOrDefaultAsync();

            if (!facultyId.HasValue)
            {
                await _context.Database.ExecuteSqlRawAsync("""
                    INSERT INTO faculty (facultyname)
                    VALUES (@facultyName)
                """, new NpgsqlParameter("@facultyName", normalizedFaculty));

                facultyId = await _context.Database
                    .SqlQuery<int?>($"""
                        SELECT facultyid AS "Value"
                        FROM faculty
                        WHERE LOWER(TRIM(facultyname)) = LOWER(TRIM({normalizedFaculty}))
                        LIMIT 1
                    """)
                    .SingleOrDefaultAsync();
            }
        }
        CreatePasswordHash(request.Password, out var hash, out var salt);
        await _context.Database.ExecuteSqlRawAsync("""
            INSERT INTO users
            (username, passwordhash, passwordsalt, firstname, lastname, email, roleid, facultyid)
            VALUES
            (@un, @ph, @ps, @fn, @ln, @em, @roleId, @facultyId);
            """,
            new NpgsqlParameter("@un", request.Username),
            new NpgsqlParameter("@ph", hash),
            new NpgsqlParameter("@ps", salt),
            new NpgsqlParameter("@fn", request.FirstName),
            new NpgsqlParameter("@ln", Encoding.UTF8.GetBytes(request.LastName ?? string.Empty)),
            new NpgsqlParameter("@em", request.Email),
            new NpgsqlParameter("@roleId", roleToAssign.RoleID),
            new NpgsqlParameter("@facultyId", (object?)facultyId ?? DBNull.Value));
        return Ok(new ApiCommandResponse { Success = true });
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<AccountLoginResult>> Login([FromBody] AccountLoginRequest request)
    {
        var user = await _context.LoginUsers
            .FromSqlRaw("""
                SELECT
                    u.userid AS "UserID",
                    u.username AS "Username",
                    u.passwordhash AS "PasswordHash",
                    u.passwordsalt AS "PasswordSalt",
                    u.roleid AS "RoleID",
                    r.rolename AS "RoleName",
                    u.isblocked AS "IsBlocked",
                    u.istwofactorenabled AS "IsTwoFactorEnabled",
                    u.email AS "Email",
                    u.firstname AS "FirstName",
                    u.lockoutendutc AS "LockoutEndUtc"
                FROM users u
                JOIN roles r ON r.roleid = u.roleid
                WHERE u.username = @username
                """, new NpgsqlParameter("@username", request.Username ?? string.Empty))
            .AsNoTracking()
            .FirstOrDefaultAsync();

        if (user == null || user.IsBlocked)
        {
            return Ok(new AccountLoginResult { Success = false, Error = "Неверный логин или пароль" });
        }

        if (user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > DateTime.UtcNow)
        {
            var remain = (user.LockoutEndUtc.Value - DateTime.UtcNow).TotalMinutes;
            return Ok(new AccountLoginResult { Success = false, Error = $"Слишком много неудачных попыток. Повторите через {Math.Ceiling(remain)} мин." });
        }

        var hash = HashPassword(request.Password, user.PasswordSalt);
        if (hash != user.PasswordHash)
        {
            try
            {
                await _context.Database.ExecuteSqlRawAsync("""
                    UPDATE users
                    SET failedloginattemptcount = COALESCE(failedloginattemptcount, 0) + 1,
                        lockoutendutc = CASE WHEN COALESCE(failedloginattemptcount, 0) + 1 >= 5
                            THEN NOW() + INTERVAL '15 minutes'
                            ELSE lockoutendutc END
                    WHERE userid = @userId
                    """, new NpgsqlParameter("@userId", user.UserID));
            }
            catch { }

            return Ok(new AccountLoginResult { Success = false, Error = "Неверный логин или пароль" });
        }

        try
        {
            await _context.Database.ExecuteSqlRawAsync(
                "UPDATE users SET failedloginattemptcount = 0, lockoutendutc = NULL WHERE userid = @userId",
                new NpgsqlParameter("@userId", user.UserID));
        }
        catch { }

        return Ok(new AccountLoginResult
        {
            Success = true,
            RequiresTwoFactor = user.IsTwoFactorEnabled && user.RoleName == "Student",
            UserId = user.UserID,
            AccessToken = user.IsTwoFactorEnabled && user.RoleName == "Student"
                ? null
                : CreateAccessToken(user.UserID, user.Username, user.RoleName),
            TwoFactorToken = user.IsTwoFactorEnabled && user.RoleName == "Student"
                ? CreateTwoFactorToken(user.UserID, user.Username, user.RoleName)
                : null,
            Username = user.Username,
            RoleName = user.RoleName,
            Email = user.Email,
            FirstName = user.FirstName
        });
    }

    [HttpPost("set-twofactor-code")]
    public async Task<ActionResult<ApiCommandResponse>> SetTwoFactorCode([FromBody] SetTwoFactorCodeRequest request)
    {
        if (!TryValidateTwoFactorJwt(request.TwoFactorToken, out var userId, out _, out _))
        {
            return Ok(new ApiCommandResponse { Success = false, Message = "Недействительный 2FA токен." });
        }
        await _context.Database.ExecuteSqlRawAsync("""
            UPDATE users
            SET twofactorcode = @code,
                twofactorcodeexpiry = @expiry
            WHERE userid = @userId
            """,
            new NpgsqlParameter("@code", request.Code),
            new NpgsqlParameter("@expiry", request.ExpiryUtc),
            new NpgsqlParameter("@userId", userId));
        return Ok(new ApiCommandResponse { Success = true });
    }

    [HttpPost("verify-twofactor-code")]
    public async Task<ActionResult<ApiCommandResponse>> VerifyTwoFactorCode([FromBody] TwoFactorCodeRequest request)
    {
        if (!TryValidateTwoFactorJwt(request.TwoFactorToken, out var userId, out _, out _))
        {
            return Ok(new ApiCommandResponse { Success = false, Message = "Недействительный или устаревший 2FA токен." });
        }

        var trimmedCode = (request.Code ?? string.Empty).Trim();
        var (success, message) = await VerifyStoredTwoFactorCodeAsync(userId, trimmedCode);
        return Ok(new ApiCommandResponse
        {
            Success = success,
            Message = message
        });
    }

    [HttpPost("clear-twofactor-code")]
    public async Task<ActionResult<ApiCommandResponse>> ClearTwoFactorCode([FromBody] TwoFactorCodeRequest request)
    {
        if (!TryValidateTwoFactorJwt(request.TwoFactorToken, out var userId, out _, out _))
        {
            return Ok(new ApiCommandResponse { Success = false, Message = "Недействительный 2FA токен." });
        }
        await _context.Database.ExecuteSqlRawAsync("""
            UPDATE users
            SET twofactorcode = NULL,
                twofactorcodeexpiry = NULL
            WHERE userid = @userId
            """, new NpgsqlParameter("@userId", userId));
        return Ok(new ApiCommandResponse { Success = true });
    }

    [HttpPost("request-twofactor-code")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ApiCommandResponse>> RequestTwoFactorCode([FromBody] TwoFactorTokenBodyRequest request)
    {
        if (!TryValidateTwoFactorJwt(request.TwoFactorToken, out var userId, out _, out _))
        {
            return Ok(new ApiCommandResponse { Success = false, Message = "Недействительный 2FA токен." });
        }

        var email = await _context.Database
            .SqlQuery<string?>($"SELECT email AS \"Value\" FROM users WHERE userid = {userId} AND istwofactorenabled = TRUE")
            .SingleOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(email))
        {
            return Ok(new ApiCommandResponse { Success = false, Message = "Для аккаунта не включена двухфакторная аутентификация или не указан email." });
        }

        var firstName = await _context.Database
            .SqlQuery<string?>($"SELECT firstname AS \"Value\" FROM users WHERE userid = {userId}")
            .SingleOrDefaultAsync();

        var code = GenerateTwoFactorCode();
        await _context.Database.ExecuteSqlRawAsync("""
            UPDATE users
            SET twofactorcode = @code,
                twofactorcodeexpiry = @expiry
            WHERE userid = @userId
            """,
            new NpgsqlParameter("@code", code),
            new NpgsqlParameter("@expiry", DateTime.UtcNow.AddMinutes(20)),
            new NpgsqlParameter("@userId", userId));

        try
        {
            await _emailService.SendTwoFactorCodeEmailAsync(email.Trim(), code, firstName);
            return Ok(new ApiCommandResponse { Success = true });
        }
        catch (Exception ex)
        {
            return Ok(new ApiCommandResponse { Success = false, Message = $"Не удалось отправить код: {ex.Message}" });
        }
    }

    [HttpPost("complete-twofactor-login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<TwoFactorCompleteLoginResult>> CompleteTwoFactorLogin([FromBody] TwoFactorCompleteLoginRequest request)
    {
        if (!TryValidateTwoFactorJwt(request.TwoFactorToken, out var userId, out var username, out var role))
        {
            return Ok(new TwoFactorCompleteLoginResult { Success = false, Message = "Недействительный или устаревший 2FA токен." });
        }

        var trimmedCode = (request.Code ?? string.Empty).Trim();
        var (ok, msg) = await VerifyStoredTwoFactorCodeAsync(userId, trimmedCode);
        if (!ok)
        {
            return Ok(new TwoFactorCompleteLoginResult { Success = false, Message = msg });
        }

        await _context.Database.ExecuteSqlRawAsync("""
            UPDATE users
            SET twofactorcode = NULL,
                twofactorcodeexpiry = NULL
            WHERE userid = @userId
            """, new NpgsqlParameter("@userId", userId));

        return Ok(new TwoFactorCompleteLoginResult
        {
            Success = true,
            AccessToken = CreateAccessToken(userId, username, role),
            Username = username,
            RoleName = role
        });
    }

    [HttpPost("guest-login")]
    public async Task<ActionResult<GuestLoginResult>> GuestLogin()
    {
        const string guestUsername = "guest";
        var guestUserId = await _context.Database
            .SqlQuery<int?>($"SELECT userid AS \"Value\" FROM users WHERE username = '{guestUsername}'")
            .SingleOrDefaultAsync();

        if (!guestUserId.HasValue)
        {
            const string studentRole = "Student";
            var roleId = await _context.Database
                .SqlQuery<int?>($"SELECT roleid AS \"Value\" FROM roles WHERE rolename = '{studentRole}'")
                .SingleOrDefaultAsync();
            if (!roleId.HasValue)
            {
                roleId = await _context.Database
                    .SqlQuery<int?>($"SELECT roleid AS \"Value\" FROM roles ORDER BY roleid LIMIT 1")
                    .SingleOrDefaultAsync();
            }
            if (!roleId.HasValue)
            {
                return Ok(new GuestLoginResult { Success = false });
            }
            CreatePasswordHash(Guid.NewGuid().ToString("N"), out var hash, out var salt);
            await _context.Database.ExecuteSqlRawAsync("""
                INSERT INTO users
                (username, passwordhash, passwordsalt, firstname, lastname, email, roleid)
                VALUES
                (@un, @ph, @ps, @fn, @ln, @em, @roleId);
                """,
                new NpgsqlParameter("@un", guestUsername),
                new NpgsqlParameter("@ph", hash),
                new NpgsqlParameter("@ps", salt),
                new NpgsqlParameter("@fn", "Гость"),
                new NpgsqlParameter("@ln", Encoding.UTF8.GetBytes("Гость")),
                new NpgsqlParameter("@em", "guest@local"),
                new NpgsqlParameter("@roleId", roleId.Value));
            guestUserId = await _context.Database
                .SqlQuery<int?>($"SELECT userid AS \"Value\" FROM users WHERE username = '{guestUsername}'")
                .SingleOrDefaultAsync();
        }

        return Ok(new GuestLoginResult
        {
            Success = guestUserId.HasValue,
            UserId = guestUserId ?? 0
        });
    }

    [HttpPost("toggle-twofactor")]
    [Authorize(Roles = "Student")]
    public async Task<ActionResult<ApiCommandResponse>> ToggleTwoFactor([FromBody] ToggleTwoFactorRequest request)
    {
        var userId = User.GetUserId();
        if (userId == 0)
        {
            return Unauthorized(new ApiCommandResponse { Success = false, Message = "Unauthorized" });
        }

        if (request.Enabled)
        {
            var userEmail = await _context.Database
                .SqlQuery<string>($"SELECT email AS \"Value\" FROM users WHERE userid = {userId}")
                .SingleOrDefaultAsync();
            var allowedDomains = GetAllowedDomains();
            if (!EmailDomainRules.IsAllowedEmail(userEmail, allowedDomains))
            {
                return Ok(new ApiCommandResponse
                {
                    Success = false,
                    Message = $"Для использования двухфакторной аутентификации нужен email на поддерживаемом домене. {BuildEmailValidationMessage(allowedDomains)}"
                });
            }
        }

        if (request.Enabled)
        {
            await _context.Database.ExecuteSqlRawAsync("""
                UPDATE users
                SET istwofactorenabled = TRUE
                WHERE userid = @userId
                """, new NpgsqlParameter("@userId", userId));
        }
        else
        {
            await _context.Database.ExecuteSqlRawAsync("""
                UPDATE users
                SET istwofactorenabled = FALSE,
                    twofactorcode = NULL,
                    twofactorcodeexpiry = NULL
                WHERE userid = @userId
                """, new NpgsqlParameter("@userId", userId));
        }

        return Ok(new ApiCommandResponse { Success = true });
    }

    private static byte[] GenerateSalt()
    {
        var salt = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        return salt;
    }

    private static string HashPassword(string password, string salt)
    {
        using var sha = SHA512.Create();
        var bytes = Encoding.UTF8.GetBytes(password + salt);
        return Convert.ToBase64String(sha.ComputeHash(bytes));
    }

    private static void CreatePasswordHash(string password, out string passwordHash, out string passwordSalt)
    {
        var saltBytes = GenerateSalt();
        passwordSalt = Convert.ToBase64String(saltBytes);
        using var sha = SHA512.Create();
        var bytes = Encoding.UTF8.GetBytes(password + passwordSalt);
        passwordHash = Convert.ToBase64String(sha.ComputeHash(bytes));
    }

    private string CreateTwoFactorToken(int userId, string username, string role)
    {
        var issuer = _configuration["JwtSettings:Issuer"] ?? "LibraryMPT";
        var audience = _configuration["JwtSettings:Audience"] ?? "LibraryMPT.Api";
        var key = _configuration["JwtSettings:Key"] ?? throw new InvalidOperationException("JwtSettings:Key is missing.");
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, role),
            new("purpose", "2fa")
        };
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(20),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string CreateAccessToken(int userId, string username, string role)
    {
        var issuer = _configuration["JwtSettings:Issuer"] ?? "LibraryMPT";
        var audience = _configuration["JwtSettings:Audience"] ?? "LibraryMPT.Api";
        var key = _configuration["JwtSettings:Key"] ?? throw new InvalidOperationException("JwtSettings:Key is missing.");
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, role)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateTwoFactorCode()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToUInt32(bytes);
        var code = (int)(value % 900000) + 100000;
        return code.ToString();
    }

    private async Task<(bool Success, string? Message)> VerifyStoredTwoFactorCodeAsync(int userId, string trimmedCode)
    {
        var expiry = await _context.Database
            .SqlQuery<DateTime?>($"""
                SELECT twofactorcodeexpiry AS "Value"
                FROM users
                WHERE userid = {userId}
                  AND istwofactorenabled = TRUE
                """)
            .SingleOrDefaultAsync();

        if (expiry == null)
            return (false, "Код 2FA не задан.");

        if (expiry.Value <= DateTime.UtcNow)
            return (false, "Срок действия кода 2FA истёк.");

        var storedCode = await _context.Database
            .SqlQuery<string?>($"""
                SELECT twofactorcode AS "Value"
                FROM users
                WHERE userid = {userId}
                  AND istwofactorenabled = TRUE
                """)
            .SingleOrDefaultAsync();

        if (storedCode == null)
            return (false, "Код 2FA не задан.");

        var success = string.Equals(storedCode.Trim(), trimmedCode, StringComparison.Ordinal);
        return success ? (true, null) : (false, "Код не совпадает.");
    }

    private bool TryValidateTwoFactorJwt(string? token, out int userId, out string username, out string role)
    {
        userId = 0;
        username = string.Empty;
        role = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var issuer = _configuration["JwtSettings:Issuer"] ?? "LibraryMPT";
        var audience = _configuration["JwtSettings:Audience"] ?? "LibraryMPT.Api";
        var key = _configuration["JwtSettings:Key"] ?? throw new InvalidOperationException("JwtSettings:Key is missing.");
        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);
            var purpose = principal.FindFirst("purpose")?.Value;
            if (purpose != "2fa")
                return false;
            var rawUserId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(rawUserId, out userId) || userId <= 0)
                return false;
            username = principal.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
            role = principal.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
            return !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(role);
        }
        catch
        {
            return false;
        }
    }

    private IReadOnlyList<string> GetAllowedDomains()
    {
        var configured = _configuration.GetSection("EmailValidation:AllowedDomains").Get<string[]>();
        var domains = (configured is { Length: > 0 } ? configured : EmailDomainRules.DefaultAllowedDomains)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(EmailDomainRules.NormalizeDomain)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return domains.Length == 0 ? EmailDomainRules.DefaultAllowedDomains : domains;
    }

    private static string BuildEmailValidationMessage(IReadOnlyList<string> allowedDomains)
        => $"Поддерживаемые почтовые домены: {string.Join(", ", allowedDomains.Select(d => $"@{d}"))}.";
}
