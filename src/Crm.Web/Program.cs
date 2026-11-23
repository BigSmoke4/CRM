using System.Security.Claims;
using Crm.Data;
using Crm.Data.Entities;
using Crm.Web;
using Crm.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- Data layer ----------
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Server=localhost\\SQLEXPRESS;Database=CrmDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    var tenant = sp.GetRequiredService<ITenantContext>();
    options.UseSqlServer(connectionString, sql =>
    {
        sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
        sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
    });
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
});
// NOTE: for background jobs use IServiceScopeFactory + scoped DbContext.

// ---------- Identity ----------
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 10;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedAccount = false; // orgs may not have email configured; TOTP/2FA still supported
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// ---------- App services ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
builder.Services.AddScoped<IClaimsTransformation, PermissionClaimsTransformation>();
builder.Services.AddHttpClient("integrations", c =>
{
    c.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddScoped<Crm.Web.Services.Integrations.IntegrationHttpService>();
builder.Services.AddScoped<Crm.Web.Services.Integrations.IntegrationSyncService>();
builder.Services.AddHostedService<Crm.Web.Services.BackgroundJobsService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<Crm.Web.Services.Ai.EmbeddingService>();
builder.Services.AddScoped<Crm.Web.Services.OAuth.OAuthFlowService>();
builder.Services.AddScoped<Crm.Web.Services.Payments.StripePaymentService>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddHttpClient();

// Data Protection key ring: persisted outside the webroot so it survives redeploys.
var configuredKeyRing = builder.Configuration["DataProtection:KeyRingPath"];
var keyRingPath = string.IsNullOrWhiteSpace(configuredKeyRing)
    ? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", ".keys"))
    : configuredKeyRing;
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
    .SetApplicationName("Crm");

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});
builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();

// Rate limiting (fixed window per IP for auth endpoints)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

// ---------- Pipeline ----------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Security headers
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    h["Content-Security-Policy"] =
        "default-src 'self'; " +
        "img-src 'self' data: https:; " +
        "style-src 'self' 'unsafe-inline'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'";
    await next();
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}")
   .WithStaticAssets();
app.MapRazorPages();

// ---------- Startup: ensure DB created & migrated, seed roles ----------
await DatabaseInitializer.InitializeAsync(app.Services, app.Logger);

app.Run();
