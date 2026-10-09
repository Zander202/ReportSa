using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Municipal.Web.Data;
using Municipal.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Compiled local launches also need the same secrets as dotnet run.
builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);

builder.Services.AddControllersWithViews(o =>
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

var connection = builder.Configuration.GetConnectionString("DefaultConnection");
var provider = builder.Configuration["Database:Provider"] ?? "Postgres";
if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException(
        "Database connection is missing. Set ConnectionStrings__DefaultConnection to your Supabase PostgreSQL connection, or configure ConnectionStrings:DefaultConnection in User Secrets.");
if (!provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)
    && !provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Database:Provider must be Postgres or SqlServer.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(connection);
    }
    else
    {
        options.UseNpgsql(connection);
    }
});

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 12;
    options.User.RequireUniqueEmail = true;

    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Denied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
});

builder.Services.AddSingleton<AssignmentService>();
builder.Services.AddSingleton<MunicipalityDirectory>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddScoped<RequestService>();

var app = builder.Build();

if (args.Contains("--verify-database") || args.Contains("--verify-storage"))
{
    await DatabaseVerification.RunAsync(app.Services, app.Configuration, args.Contains("--verify-storage"));
    return;
}

if (args.Contains("--setup") || args.Contains("--seed-demo"))
{
    await SetupUtility.RunAsync(
        app.Services,
        app.Configuration,
        args.Contains("--seed-demo"));

    return;
}

app.UseExceptionHandler("/Requests/Error");

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
    "admin",
    "Admin/{action=Index}/{id?}",
    new { controller = "Admin" });

app.MapControllerRoute(
    "default",
    "{controller=Requests}/{action=Report}/{id?}");

app.Run();

public partial class Program { }
