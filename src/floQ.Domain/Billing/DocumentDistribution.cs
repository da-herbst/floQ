using floQ.Domain.Tenants;

namespace floQ.Domain.Billing;

/// <summary>
/// Versand-Tracking für Belege (batOS-Muster). Pro Versand-Vorgang ein
/// Eintrag — ein Beleg kann mehrfach versendet werden (erneuter Versand,
/// andere E-Mail). Der Token ist der einzige Schlüssel der öffentlichen
/// Landing-Page (kein Login) und trägt implizit auch die Tenant-Identität.
/// Tenant-scoped (denormalisiert): Direktabfragen sind tenant-isoliert.
/// </summary>
public class DocumentDistribution : TenantScopedEntity
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    /// <summary>Kryptographischer Token für die öffentliche Landing-Page
    /// (32 Zufalls-Bytes, URL-safe Base64).</summary>
    public string Token { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Ablaufzeitpunkt. Null = kein Ablauf.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public string RecipientEmail { get; set; } = "";

    /// <summary>Versandart: false = Tracking-Link in der Mail (Default),
    /// true = Beleg-PDF als Datei-Anhang ohne Link.</summary>
    public bool AttachPdf { get; set; }

    // ── Tracking-Events ─────────────────────────────────────
    public DateTime? SentAtUtc { get; set; }
    public DateTime? FirstOpenedAtUtc { get; set; }
    public DateTime? LastOpenedAtUtc { get; set; }
    public int OpenCount { get; set; }
    public DateTime? FirstDownloadedAtUtc { get; set; }
    public int DownloadCount { get; set; }

    /// <summary>Versand-Snapshot des Beleg-PDFs (relativ zum Tenant-Upload-Root) —
    /// die Landing-Page liefert exakt die Datei, die verschickt wurde.</summary>
    public string? PdfFilePath { get; set; }
}
