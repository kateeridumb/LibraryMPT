using LibraryMPT.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;



var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<LibraryMPT.Filters.ApiHttpExceptionFilter>();
});

builder.Services.AddDbContext<LibraryContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("LibraryDb"),
        npgsql => npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(4), null)
    )
);
builder.Services.AddSession();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "LibraryMPT.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<LibraryMPT.Services.EmailService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<LibraryMPT.Services.ApiAuthTokenHandler>();
builder.Services.AddHttpClient("LibraryApi", (serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["ApiSettings:BaseUrl"] ?? "https://localhost:7192";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromMinutes(30);
})
.AddHttpMessageHandler<LibraryMPT.Services.ApiAuthTokenHandler>();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });
}

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseCors();
}
app.UseMiddleware<LibraryMPT.Middleware.SecurityHeadersMiddleware>();
app.UseMiddleware<LibraryMPT.Middleware.RateLimitingMiddleware>();

app.UseSession();


app.UseExceptionHandler("/Home/Error");
app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
