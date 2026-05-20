using LibraryMPT.Data;
using LibraryMPT.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddControllers();
builder.Services.AddDbContext<LibraryContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("LibraryDb"),
        npgsql =>
        {
            // После pg_restore / обрыва TCP (10054) повторы снимают временные сбои пула.
            npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(4), null);
        }));
builder.Services.AddHttpClient("BookCoverFetch", client =>
{
    client.Timeout = TimeSpan.FromSeconds(25);
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var issuer = builder.Configuration["JwtSettings:Issuer"] ?? "LibraryMPT";
        var audience = builder.Configuration["JwtSettings:Audience"] ?? "LibraryMPT.Api";
        var key = builder.Configuration["JwtSettings:Key"] ?? throw new InvalidOperationException("JwtSettings:Key is missing.");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<EmailService>();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization: введите токен (без слова Bearer)",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("MvcClient", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (origins is { Length: > 0 })
        {
            policy.WithOrigins(origins);
        }
        else
        {
            policy.WithOrigins("http://localhost:5155", "https://localhost:7107");
        }

        policy
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Content-Security-Policy", "frame-ancestors 'none'");
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}
app.UseRateLimiter();
app.UseCors("MvcClient");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
