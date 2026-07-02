using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Playwright;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace floQ.Web.Services.Pdf;

/// <summary>
/// Rendert interne HTML-Seiten via Playwright (Chromium headless) als PDF
/// (batOS-Muster). Optional wird ein Briefpapier-PDF als Vektor-Hintergrund
/// unterlegt (PdfSharp-Overlay). Singleton — der Browser wird lazy
/// initialisiert und wiederverwendet.
///
/// Self-Call-Auth: der Request bekommt den internen RenderKey angehängt;
/// die <see cref="InternalRenderMiddleware"/> lässt Loopback-Requests mit
/// gültigem Key durch (inkl. Tenant-Auflösung via ?tenant=… — floQ-Erweiterung,
/// weil jede Print-Seite tenant-scoped liest).
/// </summary>
public sealed class HtmlToPdfService(
    IServer server,
    IConfiguration config,
    ILogger<HtmlToPdfService> logger) : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _browserLock = new(1, 1);

    /// <summary>Rendert eine interne Seite als PDF. <paramref name="relativePath"/>
    /// inkl. Query (z.B. "/Print/BillingDocument/5?tenant=…"); der RenderKey wird
    /// automatisch angehängt.</summary>
    public async Task<byte[]> RenderPdfAsync(string relativePath, PdfRenderOptions? options = null)
    {
        options ??= new PdfRenderOptions();
        var browser = await GetBrowserAsync();
        var page = await browser.NewPageAsync();

        try
        {
            var url = BuildInternalUrl(relativePath);
            logger.LogInformation("PDF-Rendering: {Path}", relativePath);

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000
            });

            var pdfOptions = new PagePdfOptions
            {
                Format = "A4",
                Landscape = options.Landscape,
                PrintBackground = true,
                Margin = new Margin
                {
                    Top = options.MarginTop ?? "0",
                    Bottom = options.MarginBottom ?? "0",
                    Left = options.MarginLeft ?? "0",
                    Right = options.MarginRight ?? "0"
                }
            };

            // Footer-Konvention: definiert die Seite ein <template id="pdf-footer-template">,
            // wird dessen Inhalt als Chromium-FooterTemplate auf JEDER Seite gerendert.
            // Chromium reserviert dafür die Bottom-Margin-Zone (data-margin-bottom,
            // Default 20mm) — Fließ-Inhalt kann nie mit dem Footer kollidieren.
            // Das Template muss self-contained sein (nur Inline-Styles, keine Webfonts).
            var footerTemplate = await page.EvaluateAsync<string?>(
                "() => { const t = document.getElementById('pdf-footer-template'); return t ? t.innerHTML : null; }");
            if (!string.IsNullOrWhiteSpace(footerTemplate))
            {
                var footerMargin = await page.EvaluateAsync<string?>(
                    "() => document.getElementById('pdf-footer-template').dataset.marginBottom || null");
                pdfOptions.DisplayHeaderFooter = true;
                pdfOptions.HeaderTemplate = "<span></span>";
                pdfOptions.FooterTemplate = footerTemplate;
                pdfOptions.Margin.Bottom = string.IsNullOrWhiteSpace(footerMargin) ? "20mm" : footerMargin;
            }

            var pdfBytes = await page.PdfAsync(pdfOptions);

            if (!string.IsNullOrEmpty(options.LetterheadPdfPath))
            {
                if (File.Exists(options.LetterheadPdfPath))
                    pdfBytes = OverlayLetterhead(pdfBytes, options.LetterheadPdfPath);
                else
                    logger.LogWarning("Briefpapier nicht gefunden: {Path}", options.LetterheadPdfPath);
            }

            return pdfBytes;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PDF-Rendering fehlgeschlagen für {Path}", relativePath);
            throw;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>Legt ein Briefpapier-PDF als Vektor-Hintergrund hinter jede Seite.
    /// Append-Mode zeichnet ÜBER den Content — da das Briefpapier transparent ist,
    /// überdeckt nur der Briefkopf (Logo/Farbflächen) den Content an diesen Stellen.</summary>
    private static byte[] OverlayLetterhead(byte[] pdfBytes, string letterheadPdfPath)
    {
        using var contentStream = new MemoryStream(pdfBytes);
        var document = PdfReader.Open(contentStream, PdfDocumentOpenMode.Modify);
        using var letterhead = XPdfForm.FromFile(letterheadPdfPath);

        foreach (var page in document.Pages)
        {
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            gfx.DrawImage(letterhead, 0, 0, page.Width.Point, page.Height.Point);
        }

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    private async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser is { IsConnected: true })
            return _browser;

        await _browserLock.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true })
                return _browser;

            logger.LogInformation("Playwright-Browser wird initialisiert…");
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = ["--no-sandbox", "--disable-dev-shm-usage"]
            });
            logger.LogInformation("Playwright-Browser gestartet");
            return _browser;
        }
        finally
        {
            _browserLock.Release();
        }
    }

    private string BuildInternalUrl(string relativePath)
    {
        var baseUrl = GetBaseUrl();
        var secret = config["PdfRendering:InternalSecret"] ?? "";
        var separator = relativePath.Contains('?') ? '&' : '?';
        return $"{baseUrl}{relativePath}{separator}renderKey={Uri.EscapeDataString(secret)}";
    }

    private string GetBaseUrl()
    {
        var feature = server.Features.Get<IServerAddressesFeature>();
        var address = feature?.Addresses.FirstOrDefault(a => a.StartsWith("http://"));

        // Docker meldet oft http://[::]:8083 oder http://+:8083 — keine gültigen
        // Playwright-URLs, daher auf localhost umschreiben.
        address = address?
            .Replace("://[::]", "://localhost")
            .Replace("://+", "://localhost")
            .Replace("://0.0.0.0", "://localhost");

        return address ?? "http://localhost:8083";
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }
        _playwright?.Dispose();
        _playwright = null;
    }
}

/// <summary>Optionen für die PDF-Generierung. Margin-Werte als CSS-String ("0", "8mm").</summary>
public class PdfRenderOptions
{
    public bool Landscape { get; set; }
    public string? LetterheadPdfPath { get; set; }
    public string? MarginTop { get; set; }
    public string? MarginBottom { get; set; }
    public string? MarginLeft { get; set; }
    public string? MarginRight { get; set; }

    public static PdfRenderOptions Portrait(string? letterheadPdfPath = null) => new()
    {
        Landscape = false,
        LetterheadPdfPath = letterheadPdfPath,
        MarginTop = "0",
        MarginBottom = "0",
        MarginLeft = "0",
        MarginRight = "0"
    };
}
