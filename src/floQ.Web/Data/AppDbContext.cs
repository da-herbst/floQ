using System.Linq.Expressions;
using System.Reflection;
using floQ.Domain.Billing;
using floQ.Domain.Identity;
using floQ.Domain.Platform;
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

    // Plattform-Schicht (AC-Cache: Abos je Tenant, globale Assets)
    public DbSet<EnabledModule> EnabledModules => Set<EnabledModule>();
    public DbSet<PlatformAsset> PlatformAssets => Set<PlatformAsset>();

    // Beleg-Domäne (TPH: eine Documents-Tabelle für alle 5 Subtypen)
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentEntry> DocumentEntries => Set<DocumentEntry>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ReminderInvoice> ReminderInvoices => Set<ReminderInvoice>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<DocumentNumberConfig> DocumentNumberConfigs => Set<DocumentNumberConfig>();
    public DbSet<BillingText> BillingTexts => Set<BillingText>();
    public DbSet<ReminderLevelConfig> ReminderLevelConfigs => Set<ReminderLevelConfig>();
    public DbSet<BillingLayoutItem> BillingLayoutItems => Set<BillingLayoutItem>();

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
            // Slug = ShortName der AC-Instanz (lowercase, ≤ 32, unique).
            b.Property(t => t.Slug).HasMaxLength(32).IsRequired();
            b.HasIndex(t => t.Slug).IsUnique();
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

        // ---- Beleg-Domäne ----
        modelBuilder.Entity<Document>(b =>
        {
            b.HasKey(d => d.Id);
            // TPH: ein Diskriminator über alle 5 ausgehenden Belegtypen.
            b.HasDiscriminator<string>("DocType")
                .HasValue<Quote>("Quote")
                .HasValue<Invoice>("Invoice")
                .HasValue<CreditNote>("CreditNote")
                .HasValue<CancellationInvoice>("CancellationInvoice")
                .HasValue<PaymentReminder>("PaymentReminder");

            b.Property(d => d.Number).HasMaxLength(64);
            b.Property(d => d.Gross).HasPrecision(18, 2);
            b.Property(d => d.DiscountRate).HasPrecision(5, 2);
            b.Property(d => d.RecipientName).HasMaxLength(256);
            b.Property(d => d.RecipientCountry).HasMaxLength(2);
            b.Property(d => d.RecipientUid).HasMaxLength(32);

            b.HasOne(d => d.Customer).WithMany().HasForeignKey(d => d.CustomerId).OnDelete(DeleteBehavior.SetNull);
            b.HasMany(d => d.Entries).WithOne(e => e.Document).HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Cascade);

            // Nummern-Eindeutigkeit pro Tenant (leere Draft-Nummern ausgenommen).
            b.HasIndex(d => new { d.TenantId, d.Number })
                .IsUnique()
                .HasFilter("\"Number\" <> ''");
            b.HasIndex(d => new { d.TenantId, d.Status });
            b.HasIndex(d => d.CustomerId);
        });

        modelBuilder.Entity<Quote>(b => b.Property(q => q.ExternalReference).HasMaxLength(256));
        modelBuilder.Entity<CreditNote>(b => b.HasIndex(c => c.OriginalInvoiceId));
        modelBuilder.Entity<CancellationInvoice>(b => b.HasIndex(c => c.OriginalInvoiceId));
        modelBuilder.Entity<PaymentReminder>(b =>
        {
            b.Property(p => p.ReminderFee).HasPrecision(18, 2);
            b.Property(p => p.InterestRate).HasPrecision(5, 2);
            b.Property(p => p.InterestAmount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<DocumentEntry>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Description).HasMaxLength(1024);
            b.Property(e => e.Quantity).HasPrecision(18, 4);
            b.Property(e => e.UnitPrice).HasPrecision(18, 4);
            b.Property(e => e.VatRate).HasPrecision(5, 2);
            b.Property(e => e.DiscountPercent).HasPrecision(5, 2);
            b.Property(e => e.Unit).HasMaxLength(32);
            b.HasIndex(e => e.DocumentId);
        });

        modelBuilder.Entity<Payment>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Amount).HasPrecision(18, 2);
            b.Property(p => p.Reference).HasMaxLength(256);
            b.HasOne(p => p.Invoice).WithMany(i => i.Payments)
                .HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(p => p.InvoiceId);
        });

        modelBuilder.Entity<ReminderInvoice>(b =>
        {
            b.HasKey(ri => new { ri.PaymentReminderId, ri.InvoiceId });
            b.Property(ri => ri.OutstandingAmount).HasPrecision(18, 2);
            b.HasOne(ri => ri.PaymentReminder).WithMany(pr => pr.ReminderInvoices)
                .HasForeignKey(ri => ri.PaymentReminderId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(ri => ri.Invoice).WithMany()
                .HasForeignKey(ri => ri.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Customer>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(256);
            b.Property(c => c.CountryCode).HasMaxLength(2);
            b.Property(c => c.VatId).HasMaxLength(32);
            b.HasIndex(c => new { c.TenantId, c.Name });
        });

        modelBuilder.Entity<DocumentNumberConfig>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Separator).HasMaxLength(5);
            // Ein Zähler pro (Tenant, Typ, Jahr) — Ziel des FOR-UPDATE-Locks.
            b.HasIndex(c => new { c.TenantId, c.DocumentType, c.Year }).IsUnique();
        });

        modelBuilder.Entity<BillingText>(b =>
        {
            b.HasKey(t => t.Id);
            b.HasIndex(t => new { t.TenantId, t.DocumentType }).IsUnique();
        });

        modelBuilder.Entity<ReminderLevelConfig>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.DefaultFee).HasPrecision(18, 2);
            b.Property(c => c.DefaultInterestRate).HasPrecision(5, 2);
            b.HasIndex(c => new { c.TenantId, c.Level }).IsUnique();
        });

        modelBuilder.Entity<BillingLayoutItem>(b =>
        {
            b.HasKey(li => li.Id);
            b.Property(li => li.Key).HasMaxLength(64);
            b.Property(li => li.Label).HasMaxLength(128);
            b.Property(li => li.Group).HasMaxLength(64);
            b.HasIndex(li => new { li.TenantId, li.Key, li.DocumentType }).IsUnique();
        });

        // ---- Plattform-Schicht (AC-Cache) ----
        modelBuilder.Entity<EnabledModule>(b =>
        {
            b.HasKey(e => new { e.TenantId, e.Key });
            b.Property(e => e.Key).HasMaxLength(64);
        });

        modelBuilder.Entity<PlatformAsset>(b =>
        {
            b.HasKey(a => a.Key);
            b.Property(a => a.Key).HasMaxLength(64);
            b.Property(a => a.ETag).HasMaxLength(128);
            b.Property(a => a.ContentType).HasMaxLength(128);
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

            // TPH: Filter + Index nur am Root-Entity-Typ — abgeleitete Typen
            // (Quote, Invoice, …) erben den Filter der Basisklasse (Document).
            if (entityType.BaseType is not null)
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
