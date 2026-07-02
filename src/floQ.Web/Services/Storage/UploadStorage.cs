namespace floQ.Web.Services.Storage;

/// <summary>
/// Tenant-getrenntes Datei-Ablage-Root (Beleg-PDFs, Logo, Briefpapier).
/// Struktur: <c>{BasePath}/{tenantId}/…</c> — alle persistierten Pfade in der
/// DB sind RELATIV zum Tenant-Root (z.B. "billing/2026-07/RE_2026-11-0001.pdf"),
/// damit BasePath je Umgebung frei wandern kann.
///
/// BasePath aus Config <c>Uploads:BasePath</c> (Production: /data/uploads,
/// Docker-Volume; Development: relatives "data/uploads" unterm ContentRoot).
/// </summary>
public class UploadStorage(IConfiguration config, IWebHostEnvironment env)
{
    private readonly string _basePath = ResolveBasePath(config, env);

    private static string ResolveBasePath(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["Uploads:BasePath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = "data/uploads";
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(env.ContentRootPath, configured);
    }

    /// <summary>Absoluter Pfad zu einer tenant-relativen Datei. Wirft bei
    /// Pfad-Ausbruch (../) — DB-Pfade sind vertrauenswürdig, der Guard ist
    /// Defense-in-Depth.</summary>
    public string Resolve(Guid tenantId, string relativePath)
    {
        var tenantRoot = Path.Combine(_basePath, tenantId.ToString());
        var full = Path.GetFullPath(Path.Combine(tenantRoot, relativePath));
        if (!full.StartsWith(Path.GetFullPath(tenantRoot), StringComparison.Ordinal))
            throw new InvalidOperationException($"Pfad verlässt das Tenant-Root: {relativePath}");
        return full;
    }

    /// <summary>Bytes unter dem tenant-relativen Pfad ablegen (Verzeichnisse
    /// werden angelegt, bestehende Datei wird ersetzt).</summary>
    public async Task SaveAsync(Guid tenantId, string relativePath, byte[] bytes, CancellationToken ct = default)
    {
        var full = Resolve(tenantId, relativePath);
        var dir = Path.GetDirectoryName(full);
        if (dir is not null) Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(full, bytes, ct);
    }

    /// <summary>Datei löschen, falls vorhanden (idempotent).</summary>
    public void Delete(Guid tenantId, string relativePath)
    {
        var full = Resolve(tenantId, relativePath);
        if (File.Exists(full)) File.Delete(full);
    }

    public bool Exists(Guid tenantId, string relativePath)
        => File.Exists(Resolve(tenantId, relativePath));
}
