namespace floQ.Domain.Identity;

/// <summary>
/// E-Mail-Einmalcode als Passkey-Fallback und Account-Recovery
/// (Gerätewechsel: Code-Login → neuen Passkey registrieren).
/// Single-use, kurz gültig, max. 5 Eingabeversuche — der Code selbst wird
/// nie gespeichert, nur sein Hash (Row-Id als Salt).
/// </summary>
public class LoginCode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>SHA-256 über "{Id:N}:{Code}" (hex, lowercase).</summary>
    public string CodeHash { get; set; } = "";

    /// <summary>Fehlversuche bei der Code-Eingabe. Ab 5 ist der Code tot
    /// (Brute-Force-Schutz bei 6 Ziffern).</summary>
    public int AttemptCount { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Gesetzt beim erfolgreichen Login — Codes sind single-use.</summary>
    public DateTime? UsedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
