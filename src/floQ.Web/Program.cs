using floQ.Web.Data;
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

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
