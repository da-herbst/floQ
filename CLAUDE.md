# floQ - Projektkontext

> Verrechnungstool unter [floq.at](https://floq.at). Hobbyprojekt — aber lege artis gebaut.
> Sprich „flock" (österreichischer Slang für Geld). Reines Billing, kein ERP.

## Arbeitsweise

- **Senior-Developer-Niveau**. Saubere Lösungen, kein Raten. Bei Unklarheiten Code lesen oder DB abfragen.
- **IMMER sauber**: nie „Bastellösung oder sauber?" fragen. Sauber ist gesetzt.
- **Nullhypothese**: Aussagen des Entwicklers sind solange unbewiesen, bis durch Code/DB belegt.
- **Fragen ≠ Aufträge**: „Hast du X gemacht?" ist Infofrage, nicht Auftrag.
- **Durcharbeiten** sobald freigegeben — keine Zwischenfragen.
- **Keine Multiple-Choice-Fragen**: niemals `AskUserQuestion`-Tool. Klärung normal im Chat.
- **Debugging-Workflow**: 1) Reproduzieren, 2) Ursache analysieren, 3) Lösung vorschlagen, 4) Freigabe abwarten, 5) Umsetzen.
- **Dedizierte Tools statt Bash**: `Glob`, `Grep`, `Read` bevorzugen. Bash-Befehle einzeln und sauber.
- **Server-Inspektion**: Read-only Befehle auf dem Hetzner-Server (`ssh root@46.62.224.113 "..."` mit lesenden Befehlen — `docker ps`, `docker logs`, `cat`, `ls`, `psql -c "SELECT ..."`) sind ohne Freigabe erlaubt. Schreibende/destruktive Aktionen (`docker rm`, `rm`, `UPDATE/INSERT/DELETE`, `compose up/down`, Caddyfile-Edit) brauchen explizite Freigabe.
- **Eiserne Regel — GitHub ist Source of Truth für Live**: Was nicht auf `origin/main` liegt, darf nicht auf floq.at laufen. Es gibt keinen Live-Stand, der nicht im Git nachvollziehbar ist.
- **Git-Standardprozedere**: Claude commitet selbständig, sobald eine logische Einheit fertig ist (saubere Gliederung, aussagekräftige Messages, deutsch). Änderungen an `CLAUDE.md` sind IMMER ein separater Commit.
- **Push & Deploy nur auf explizite Anweisung**: Claude pusht und deployt nicht von selbst. Auf explizites „push" oder „deploy" durch David führt Claude `scripts/deploy.sh` aus (Deploy zieht Push immer mit — beides in einem Rutsch). Der Entwickler kann denselben Befehl jederzeit selbst aus Rider auslösen.

## Architektur-Prinzipien

- **Verwaltung NUR über das AdminCenter**: floQ hat keinen Admin-, Hersteller- oder Developer-Zugang und bekommt nie einen. Jede Verwaltungsfunktion (Abonnenten, Abos, Shutoff, Preise, Billing, System-Mails, künftige Support-Aktionen wie Tenant-Löschung) gehört ins batOSAdminCenter — floQ-seitig höchstens als eingehender Plattform-Endpoint (X-Platform-Key, Muster `AdminCenter/AdminCenterEndpoints.cs`), niemals als floQ-UI oder Sonder-Login. In floQ loggen sich ausschließlich Kunden ein.
- **API-First**: Jeder UI-Flow geht über dieselbe REST-API. Razor Pages sind Consumer, kein Sonderpfad.
- **Domain pur**: `floQ.Domain` hat keine ASP.NET-/EF-Abhängigkeiten. Persistenz-Konfiguration in `Web/` via Fluent API.
- **API-Versionierung**: Endpoints unter `/api/v1/...` ab Tag 1. Breaking Changes → neue Version, nie stillschweigend.
- **Keine Premature Abstractions**: `Application/`/`Infrastructure/` als eigene Projekte erst, wenn die `Web/`-Folder organisch zu eng werden.
- **Migrations**: EF Core Migrations versioniert. Kein `EnsureCreated`, kein `Database.Migrate()` außerhalb expliziter Deploy-Schritte.

## Regeln & Patterns

- **Zeitzonen**: Server in UTC, UI in CET/CEST (Wien). Speicherung als UTC, Anzeige via Vienna-Konversion. Niemals `DateTime.Now`/`Today`.
- **Geldbeträge**: `decimal`, niemals `double`/`float`. Speicherung mit fester Skala (z.B. `decimal(18,2)`).
- **API-Response**: Einheitlich `{ success, data, errorMessage }`. Fehler immer mit aussagekräftiger Message, nie nur HTTP-Status.
- **Naming**: C# = PascalCase, JS = camelCase, CSS = kebab-case, HTML-IDs = camelCase.
- **Sprache**: Commits, Code-Kommentare, UI-Texte deutsch. Englisch nur für Standard-Begriffe (Invoice, Quote etc.) wo unvermeidbar.
- **Git**: Commits logisch gegliedert, aussagekräftige Messages. **Commits NUR nach expliziter Freigabe.** Niemals pushen/deployen ohne Freigabe.

## Lokale Entwicklung

- **Postgres**: `docker compose up -d` (Port 5433, User/PW/DB = `floq`/`floq_dev`/`floq`)
- **App**: `dotnet run --project src/floQ.Web`
- **Migrations**: `dotnet ef migrations add <Name> --project src/floQ.Web`

## Production

- **Server**: Hetzner Helsinki, `ubuntu-8gb-hel1-1` (46.62.224.113), Docker bereits aktiv (Co-Tenant: `dlvr`)
- **Domain**: floq.at
- **Deploy**: noch nicht eingerichtet — kommt später

## Beziehung zu batOS

floQ ist **bewusst losgelöst** von batOS. Code wird nicht geteilt. Erkenntnisse aus dem Billing-Modul von batOS (`Pages/Billing/CreateV2/`, PDF-Pipeline, Token-Versand mit Tracking, ErsteConnect-Banking) fließen als **Konzept-Vorlage** ein, nicht als Code-Import. Eine optionale batOS-Push-API ist Nebenprodukt der API-First-Architektur, kein Designziel.
