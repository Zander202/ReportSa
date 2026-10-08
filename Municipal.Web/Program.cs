using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Municipal.Web.Data;
using Municipal.Web.Services;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddDbContext<ApplicationDbContext>(o => {
    var connection = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.Equals(builder.Configuration["Database:Provider"], "Supabase", StringComparison.OrdinalIgnoreCase) || connection?.Contains("supabase", StringComparison.OrdinalIgnoreCase) == true)
    {
        o.UseNpgsql(connection);
    }
    else
    {
        o.UseSqlServer(connection);
    }
});
builder.Services.AddIdentity<IdentityUser, IdentityRole>(o => {
    o.Password.RequiredLength = 12; o.User.RequireUniqueEmail = true;
    o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o => {
    o.LoginPath = "/Account/Login"; o.AccessDeniedPath = "/Account/Denied";
    o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30); o.SlidingExpiration = true;
});
builder.Services.AddSingleton<AssignmentService>();
builder.Services.AddHttpClient<SmsNotificationService>();
builder.Services.AddScoped<RequestService>();
var app = builder.Build();
if (args.Contains("--setup") || args.Contains("--seed-demo")) {
    await SetupUtility.RunAsync(app.Services, app.Configuration, args.Contains("--seed-demo")); return;
}
app.UseExceptionHandler("/Requests/Error");
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection(); app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
app.MapControllerRoute("admin", "Admin/{action=Index}/{id?}", new { controller = "Admin" });
app.MapControllerRoute("default", "{controller=Requests}/{action=Report}/{id?}");
app.Run();
public partial class Program { }
