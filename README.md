# floQ

> Sprich: **„flock"** — österreichischer Slang für Geld / Cash.
> Reines Verrechnungstool: Angebote, Rechnungen, Gutschriften, Mahnungen.

floQ ist eine eigenständige Web-App unter [floq.at](https://floq.at). Bewusst klein, bewusst fokussiert: keine Projektverwaltung, keine Personalverrechnung, keine Branchenlogik. Nur Belege rausschicken, getrackt sehen, Zahlung abgleichen.

## Positionierung

- **Zielgruppe**: Einzelunternehmer, Kleinbetriebe, Freelancer
- **Abgrenzung**: kein InvoiceNinja-Klon — Fokus auf Bedien-Tempo und saubere Defaults statt Feature-Sammlung
- **Nicht-Ziele**: Buchhaltungssoftware, ERP, Zeiterfassung, Projektmanagement

## Architektur

- **Stack**: ASP.NET Core 10 (Razor Pages + Minimal API), EF Core, PostgreSQL 17
- **API-First**: Jeder UI-Flow geht über dieselbe REST-API, die auch für externe Integrationen offen ist (Webhooks, Zapier/Make, optional batOS-Push)
- **Deployment**: Docker auf Hetzner (Helsinki, ubuntu-8gb-hel1-1)

## Projektstruktur

```
floQ/
├── src/
│   ├── floQ.Domain/      Pure Entities + Enums, keine Framework-Abhängigkeiten
│   └── floQ.Web/         ASP.NET 10 — Razor Pages, Minimal API, EF Core, Postgres
├── tests/
│   └── floQ.Tests/       xUnit
├── compose.yaml          Lokales Postgres für Dev (Port 5433)
└── Dockerfile            Production-Image
```

`Application/` und `Infrastructure/` werden erst herausgezogen, wenn die `Web/`-Folder eng werden — kein vorgezogenes Splitting.

## Entwicklung

```bash
docker compose up -d                          # Postgres starten
dotnet run --project src/floQ.Web             # App starten
```

## Lege artis

- Commits sauber strukturiert, Sprache deutsch (UI, Code-Kommentare, Commits)
- Migrations versioniert, kein `EnsureCreated`
- API-Versionierung ab Tag 1 (`/api/v1/...`)
- Kein Live-Deploy ohne Freigabe
