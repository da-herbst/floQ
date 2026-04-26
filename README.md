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
- **API-First**: Jeder UI-Flow geht über dieselbe REST-API
- **Logging**: Serilog (Console-JSON in Prod, hübsch in Dev)
- **Deployment**: Docker auf Hetzner (Helsinki, ubuntu-8gb-hel1-1), Caddy als Reverse-Proxy mit Let's-Encrypt

## Projektstruktur

```
floQ/
├── src/
│   ├── floQ.Domain/             Pure Entities + Enums, keine Framework-Abhängigkeiten
│   └── floQ.Web/                ASP.NET 10
│       ├── Data/                AppDbContext
│       ├── Migrations/          EF-Core-Migrationen (versioniert)
│       ├── Pages/               Razor Pages
│       └── wwwroot/img/         Brand-Assets (Logo-Mark als PNG + SVG)
├── tests/
│   └── floQ.Tests/              xUnit
├── scripts/
│   └── deploy.sh                One-Click-Deploy aus Rider (Push + Server-Pull + Rebuild)
├── compose.yaml                 Lokales Postgres für Dev (Port 5433)
├── compose.production.yaml      Production-Stack (floq + Postgres)
├── Dockerfile                   Production-Image
└── .env.example                 Vorlage für Server-seitige .env (POSTGRES_PASSWORD)
```

`Application/` und `Infrastructure/` werden erst herausgezogen, wenn die `Web/`-Folder eng werden — kein vorgezogenes Splitting.

## Entwicklung

### Erstmaliges Setup

```bash
dotnet tool install -g dotnet-ef            # EF-CLI fuer Migrationen
docker compose up -d                        # Lokales Postgres (Port 5433)
dotnet run --project src/floQ.Web           # App starten — Migrationen laufen automatisch beim Start
```

### Migrationen

```bash
dotnet ef migrations add <Name> --project src/floQ.Web
dotnet ef database update    --project src/floQ.Web   # Optional — laeuft sonst beim App-Start
```

> **Hinweis**: Migrationen werden derzeit beim App-Start automatisch angewendet (`db.Database.Migrate()`).
> Solange floQ keine echten Nutzer hat und Daily-Backups laufen, ist das OK.
> Vor Live-Gang auf manuelle Migration via `dotnet ef migrations bundle` umstellen.

### Tests

```bash
dotnet test
```

## Production

### Server

- **Host**: Hetzner Helsinki, `ubuntu-8gb-hel1-1` (`46.62.224.113`)
- **Pfad**: `/opt/floq/` (Git-Clone)
- **Domain**: [floq.at](https://floq.at) — TLS via Caddy + Let's Encrypt (geteilt mit Co-Tenant `dlvr`)
- **Container**: `floq-floq-1` (App), `floq-postgres-1` (DB) — beide intern, hinter Caddy

### Erstmaliges Server-Setup

Auf dem Server unter `/opt/floq/.env` ein File mit dem DB-Passwort anlegen (einmalig):

```bash
cd /opt/floq
cat > .env <<EOF
POSTGRES_PASSWORD=$(openssl rand -base64 32 | tr -d '/+=' | head -c 40)
EOF
chmod 600 .env
```

`.env` ist gitignored — wird vom Deploy-Script geprüft, nicht überschrieben.

### Deploy

```bash
./scripts/deploy.sh
```

Das Script:
1. **Pre-flight**: Branch ist `main`, Working Tree clean, nicht hinter origin
2. **Push**: `git push origin main` (Deploy zieht Push immer mit — eiserne Regel: nur was auf GitHub liegt, darf live sein)
3. **Server**: SSH zum Hetzner, `git pull` + `docker compose -f compose.production.yaml up -d --build`
4. **Smoke-Test**: `curl https://floq.at/`, prüft HTTP 200 und Claim im HTML
5. Zeigt Cert-Restlaufzeit

In Rider als External Tool:
- **Program**: `bash`
- **Arguments**: `scripts/deploy.sh`
- **Working directory**: `$ProjectFileDir$`

### Backup

PostgreSQL-Daten liegen im benamten Volume `floq_postgres_data`. Daily-Backups via `pg_dump` als Cron — Setup folgt sobald erste echte Daten anfallen.

## Lege artis

- Commits sauber strukturiert, Sprache deutsch (UI, Code-Kommentare, Commits)
- Migrations versioniert, kein `EnsureCreated`
- API-Versionierung ab Tag 1 (`/api/v1/...`)
- **GitHub = Source of Truth für Live**: was nicht auf `origin/main` liegt, läuft nicht auf floq.at
- Push und Deploy nur auf explizite Anweisung
