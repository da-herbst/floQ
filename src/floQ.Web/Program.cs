using Fido2NetLib;
using floQ.Web.AdminCenter;
using floQ.Web.Api.V1;
using floQ.Web.Auth;
using floQ.Web.Data;
using floQ.Web.Services.Documents;
using floQ.Web.Services.Pdf;
using floQ.Web.Services.Storage;
using floQ.Web.Tenancy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
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

        // API-Calls kriegen 401/403 statt Redirect auf die Login-HTML —
        // dieselbe API bedient UI (Cookie) und externe Consumer.
        options.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            else
                ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            else
                ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    });

// Policy "InternalRender": lässt die Playwright-Self-Calls der PDF-Pipeline
// durch (Loopback + renderKey, siehe InternalRenderMiddleware).
builder.Services.AddAuthorization(o =>
    o.AddPolicy(InternalRenderRequirement.PolicyName,
        p => p.AddRequirements(new InternalRenderRequirement())));
builder.Services.AddSingleton<IAuthorizationHandler, InternalRenderAuthorizationHandler>();

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

// Beleg-Engine (Port des batOS-DocumentEngine-Letztstands).
builder.Services.AddScoped<IDocumentEngine, DocumentEngine>();

// PDF-Pipeline: Playwright-Renderer (Singleton, Browser wird wiederverwendet)
// + tenant-getrenntes Upload-Root für persistierte Beleg-PDFs/Briefpapier.
builder.Services.AddSingleton<HtmlToPdfService>();
builder.Services.AddSingleton<UploadStorage>();

// AdminCenter-Anbindung (zentrale Abo-Verwaltung, https://admin.batos.at).
// Ohne Konfiguration (PlatformKey/ShortName leer) bleibt der Sync untätig.
builder.Services.Configure<AdminCenterOptions>(
    builder.Configuration.GetSection(AdminCenterOptions.SectionName));
builder.Services.AddHttpClient(AdminCenterSyncService.HttpClientName);
builder.Services.AddScoped<AdminCenterClient>();
builder.Services.AddSingleton<IAdminCenterSyncTrigger, AdminCenterSyncTrigger>();
builder.Services.AddSingleton<TenantShutoffService>();
builder.Services.AddSingleton<ModuleCatalog>();
builder.Services.AddSingleton<ModuleGateService>();
builder.Services.AddHostedService<AdminCenterSyncService>();

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

// Shutoff- und Abo-Cache aus der DB laden (Zustand überlebt Neustarts).
app.Services.GetRequiredService<TenantShutoffService>().Reload();
app.Services.GetRequiredService<ModuleGateService>().Reload();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseSession();
app.UseAuthentication();

// Internes PDF-Rendering: Playwright-Loopback-Requests mit renderKey
// authentifizieren (setzt auch den "tid"-Claim für den TenantResolver).
app.UseInternalRenderAuth();

app.UseTenantResolver();   // muss NACH UseAuthentication, VOR allem mit DbContext-Zugriff

// Shutoff-Gate je Tenant: stillgelegte Mandanten sehen nur die Wartungsseite.
app.UseTenantShutoff();

// Modul-Gate: nicht abonnierte Modul-Routen → 503 (Basis: lokaler Abo-Cache).
app.UseModuleGate();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapAdminCenterEndpoints();
app.MapAccountSubscriptionEndpoints();

// REST-API v1 (API-First: die Razor Pages sind reine Consumer).
app.MapBillingApi();
app.MapCompanyProfileApi();

app.Run();
