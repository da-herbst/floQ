using System.Security.Cryptography;
using System.Text;
using floQ.Domain.Identity;
using floQ.Web.Data;
using floQ.Web.Services.Mail;
using Microsoft.EntityFrameworkCore;

namespace floQ.Web.Auth;

/// <summary>
/// E-Mail-Einmalcode als Passkey-Fallback (Login ohne Passkey, Recovery bei
/// Gerätewechsel: Code-Login → unter /auth/register neuen Passkey anlegen).
///
/// Sicherheitsmodell:
/// - 6-stelliger Zufallscode (CSPRNG), 10 Minuten gültig, single-use,
///   max. 5 Eingabeversuche. Gespeichert wird nur der Hash (Id als Salt).
/// - Kein User-Enumeration-Leak: Begin antwortet immer gleich, ob die
///   Mail existiert oder nicht; Complete meldet nur generisch „ungültig".
/// - Cooldown 60 s gegen Mail-Bombing derselben Adresse.
/// - Bewusst Code statt Magic-Link: Link-Prefetch von Mail-Scannern
///   (Outlook SafeLinks & Co.) würde Single-Use-Links unbrauchbar machen.
/// </summary>
public class LoginCodeService(
    AppDbContext db,
    SystemMailer mailer,
    IWebHostEnvironment env,
    ILogger<LoginCodeService> log)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);
    private const int MaxAttempts = 5;

    /// <summary>Erzeugt und verschickt einen Code — bewusst ohne Rückgabe,
    /// ob die Mail ein Konto hat (Enumeration-Schutz). Aufrufer antwortet
    /// immer: „Wenn ein Konto existiert, wurde ein Code gesendet."</summary>
    public async Task BeginAsync(string email, CancellationToken ct)
    {
        email = email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            log.LogInformation("Login-Code angefordert für unbekannte Mail.");
            return;
        }

        var now = DateTime.UtcNow;
        var existing = await db.Set<LoginCode>()
            .Where(c => c.UserId == user.Id)
            .ToListAsync(ct);

        if (existing.Any(c => c.UsedAtUtc == null && now - c.CreatedAtUtc < Cooldown))
        {
            log.LogInformation("Login-Code-Cooldown aktiv für {UserId} — nichts gesendet.", user.Id);
            return;
        }

        // Alte Codes des Users invalidieren — es gilt immer nur der neueste.
        db.RemoveRange(existing);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var row = new LoginCode
        {
            UserId = user.Id,
            ExpiresAtUtc = now + Lifetime,
        };
        row.CodeHash = Hash(row.Id, code);
        db.Add(row);
        await db.SaveChangesAsync(ct);

        if (mailer.IsConfigured)
        {
            var minutes = (int)Lifetime.TotalMinutes;
            await mailer.SendAsync(
                email,
                $"{code} ist dein floQ-Anmeldecode",
                $"""
                <p>Dein Anmeldecode für floQ:</p>
                <p style="font-size:1.6rem;font-weight:700;letter-spacing:0.2em;">{code}</p>
                <p>Er ist {minutes} Minuten gültig und nur einmal verwendbar.
                Wenn du keinen Code angefordert hast, ignoriere diese Mail.</p>
                """,
                $"Dein Anmeldecode für floQ: {code}\nEr ist {minutes} Minuten gültig und nur einmal verwendbar.\nWenn du keinen Code angefordert hast, ignoriere diese Mail.",
                ct);
        }
        else if (env.IsDevelopment())
        {
            // Dev ohne SMTP: Code ins Log — nur lokal, nie in Production.
            log.LogWarning("SystemMail nicht konfiguriert — Login-Code für {Email}: {Code}", email, code);
        }
        else
        {
            log.LogError("SystemMail nicht konfiguriert — Login-Code für {UserId} konnte nicht zugestellt werden.", user.Id);
        }
    }

    /// <summary>Validiert Mail + Code. Erfolg → UserId (Aufrufer signt ein),
    /// sonst null — bewusst ohne Detail, was falsch war.</summary>
    public async Task<Guid?> CompleteAsync(string email, string code, CancellationToken ct)
    {
        email = email.Trim().ToLowerInvariant();
        code = code.Trim();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null) return null;

        var now = DateTime.UtcNow;
        var row = await db.Set<LoginCode>()
            .Where(c => c.UserId == user.Id && c.UsedAtUtc == null)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (row is null || row.ExpiresAtUtc < now || row.AttemptCount >= MaxAttempts)
            return null;

        var expected = Encoding.ASCII.GetBytes(row.CodeHash);
        var actual = Encoding.ASCII.GetBytes(Hash(row.Id, code));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            row.AttemptCount++;
            await db.SaveChangesAsync(ct);
            log.LogInformation("Login-Code falsch für {UserId} (Versuch {Attempt}/{Max}).",
                user.Id, row.AttemptCount, MaxAttempts);
            return null;
        }

        row.UsedAtUtc = now;
        user.LastLoginAtUtc = now;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Login per E-Mail-Code für {UserId}.", user.Id);
        return user.Id;
    }

    private static string Hash(Guid id, string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{id:N}:{code}")));
}
