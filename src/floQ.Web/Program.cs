using Fido2NetLib;
using floQ.Web.Auth;
using floQ.Web.Data;
using floQ.Web.Tenancy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Serilog: in Dev hübsch lesbar, in Prod als Compact-JSON (Docker captured stdout).
builder.Host.UseSerilog((ctx, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext();

    if (ctx.HostingEnvironment.IsDevelopment())
        lc.WriteTo.Console();
    else
        lc.WriteTo.Console(new CompactJsonFormatter());
});

builder.Services.AddRazorPages();

// EF
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Tenant-Schicht: pro Request ein Context, Middleware befüllt aus Cookie-Claim.
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddHttpContextAccessor();

// Cookie-Auth (kein Identity-Stack — passwortlos via Passkey, siehe Auth/).
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "floq.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/auth/login";
        options.LogoutPath = "/auth/logout";
        options.AccessDeniedPath = "/auth/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

// Session für WebAuthn-Challenge zwischen Begin/Complete.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(opts =>
{
    opts.Cookie.Name = "floq.webauthn";
    opts.Cookie.HttpOnly = true;
    opts.Cookie.SameSite = SameSiteMode.Lax;
    opts.Cookie.IsEssential = true;
    opts.IdleTimeout = TimeSpan.FromMinutes(10);
});

// Fido2 / WebAuthn — Konfiguration aus appsettings.
var fido2Config = builder.Configuration.GetSection("Fido2");
builder.Services.AddFido2(options =>
{
    options.ServerDomain = fido2Config["ServerDomain"];
    options.ServerName = fido2Config["ServerName"];
    options.Origins = fido2Config.GetSection("Origins").Get<HashSet<string>>() ?? new();
    options.TimestampDriftTolerance = 300_000;
});

builder.Services.AddScoped<IPasskeyService, PasskeyService>();

var app = builder.Build();

app.UseSerilogRequestLogging();

// Migrations beim Start auto-anwenden.
// Akzeptabel solange floQ keine echten Nutzer hat und Daily-Backups laufen.
// Vor Live-Gang (echte Nutzer) auf manuell-via-Bundle umstellen.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseTenantResolver();   // muss NACH UseAuthentication, VOR allem mit DbContext-Zugriff
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
