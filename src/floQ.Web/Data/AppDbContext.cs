using System.Linq.Expressions;
using System.Reflection;
using floQ.Domain.Identity;
using floQ.Domain.Settings;
using floQ.Domain.Tenants;
using floQ.Web.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Data;

/// <summary>
/// EF-Wurzel.
///
/// Multi-Tenancy:
/// - Jede <see cref="TenantScopedEntity"/>-Subklasse bekommt automatisch einen
///   Global Query Filter auf <c>TenantId == _tenantContext.TenantId</c>.
/// - <see cref="SaveChangesAsync"/> setzt <c>TenantId</c> beim Insert automatisch
///   aus dem aktuellen TenantContext, damit Aufrufer das nie vergessen können.
/// - Anonyme/tenantlose Requests sehen aus jeder Tenant-Tabelle nichts (Filter
///   matcht <see cref="Guid.Empty"/> = leere Menge).
///
/// User/Tenant/UserTenant/PasskeyCredential sind bewusst NICHT tenant-scoped:
/// Login passiert vor Tenant-Resolution.
/// </summary>
public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ITenantContext tenantContext) : DbContext(options)
{
    private readonly ITenantContext _tenantContext = tenantContext;

    // Plattform-Schicht (tenant-agnostisch)
    public DbSet<User> Users => Set<User>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();
    public DbSet<PasskeyCredential> PasskeyCredentials => Set<PasskeyCredential>();

    // Tenant-Schicht (auto-isoliert)
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---- Plattform ----
        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(u => u.Id);
            b.HasIndex(u => u.Email).IsUnique();
            b.Property(u => u.Email).HasMaxLength(256).IsRequired();
            b.Property(u => u.DisplayName).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<Tenant>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Name).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<UserTenant>(b =>
        {
            b.HasKey(ut => new { ut.UserId, ut.TenantId });
            b.HasOne(ut => ut.User).WithMany().HasForeignKey(ut => ut.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(ut => ut.Tenant).WithMany().HasForeignKey(ut => ut.TenantId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(ut => new { ut.UserId, ut.IsDefault });
        });

        modelBuilder.Entity<PasskeyCredential>(b =>
        {
            b.HasKey(p => p.Id);
            b.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(p => p.CredentialId).IsUnique();
            b.Property(p => p.Name).HasMaxLength(128);
        });

        // ---- Tenant-Schicht ----
        modelBuilder.Entity<CompanyProfile>(b =>
        {
            b.HasKey(c => c.Id);
            // Genau ein CompanyProfile pro Tenant.
            b.HasIndex(c => c.TenantId).IsUnique();
            b.Property(c => c.LegalName).HasMaxLength(256);
            b.Property(c => c.CountryCode).HasMaxLength(2);
            b.Property(c => c.VatId).HasMaxLength(32);
            b.Property(c => c.Iban).HasMaxLength(34);
            b.Property(c => c.Bic).HasMaxLength(11);
        });

        // ---- Auto-Configuration für jede TenantScopedEntity ----
        ApplyTenantConventions(modelBuilder);
    }

    /// <summary>
    /// Reflection-basiert: für jeden Entity-Typ, der von <see cref="TenantScopedEntity"/>
    /// erbt, einen Index auf TenantId und einen Global Query Filter setzen.
    /// Damit kann man eine neue Beleg-Entity einfach von TenantScopedEntity ableiten —
    /// Filter/Index sind automatisch aktiv, kein manuelles Setup nötig.
    ///
    /// Filter-Lambda wird über die generic <see cref="BuildTenantFilter{T}"/> erzeugt,
    /// damit die Expression-Struktur snapshot-stabil ist (sonst meldet EF Core 10
    /// PendingModelChangesWarning, weil dynamisch gebaute Lambdas zwischen Compile
    /// und Runtime nicht 1:1 übereinstimmen).
    /// </summary>
    private void ApplyTenantConventions(ModelBuilder modelBuilder)
    {
        var buildFilterMethod = typeof(AppDbContext).GetMethod(
            nameof(BuildTenantFilter),
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("BuildTenantFilter nicht gefunden.");

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(TenantScopedEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            // Index auf TenantId (idempotent)
            var existing = entityType.GetIndexes()
                .Any(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(TenantScopedEntity.TenantId));
            if (!existing)
                modelBuilder.Entity(entityType.ClrType).HasIndex(nameof(TenantScopedEntity.TenantId));

            var lambda = (LambdaExpression)buildFilterMethod
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, null)!;
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }

    /// <summary>
    /// Filter-Lambda <c>e =&gt; e.TenantId == CurrentTenantId</c>.
    /// EF parametrisiert <see cref="CurrentTenantId"/> als Instance-Member-Access
    /// — strukturell identisch über alle DbContext-Instanzen, also snapshot-stabil.
    /// </summary>
    private LambdaExpression BuildTenantFilter<T>() where T : TenantScopedEntity
    {
        Expression<Func<T, bool>> filter = e => e.TenantId == CurrentTenantId;
        return filter;
    }

    /// <summary>
    /// Wird vom Query-Filter-Lambda referenziert. Nicht direkt aus der App
    /// benutzen — Aufrufer sollen über <see cref="ITenantContext"/> gehen.
    /// </summary>
    public Guid CurrentTenantId => _tenantContext.TenantId;

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTenant();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampTenant();
        return base.SaveChanges();
    }

    /// <summary>
    /// Setzt TenantId auf neu eingefügten TenantScopedEntities aus dem aktuellen
    /// TenantContext. Wenn der Aufrufer TenantId schon explizit gesetzt hat
    /// (z.B. System-Provisioning), wird der bestehende Wert nicht überschrieben.
    /// Wenn TenantContext nicht resolved ist und ein Insert ohne explizite TenantId
    /// passiert: Exception, damit so ein Fehler nicht stillschweigend Cross-Tenant-Daten erzeugt.
    /// </summary>
    private void StampTenant()
    {
        foreach (var entry in ChangeTracker.Entries<TenantScopedEntity>())
        {
            if (entry.State != EntityState.Added) continue;
            if (entry.Entity.TenantId != Guid.Empty) continue;

            if (!_tenantContext.IsResolved)
                throw new InvalidOperationException(
                    $"Insert von {entry.Entity.GetType().Name} ohne TenantId und ohne aktiven TenantContext. " +
                    "TenantId explizit setzen oder Request-Pipeline prüfen.");

            entry.Entity.TenantId = _tenantContext.TenantId;
        }
    }
}
