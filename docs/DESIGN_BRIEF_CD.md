# floQ — Design-Brief für Claude Design

> Prompt-Dokument: vollständige Seiten- und Inhalts-Spezifikation des
> Ist-Stands (Phase 1). Ziel: ein durchgängiges, hochwertiges UI-Design
> für alle Screens. Der Funktionsumfang ist FIX — designe, was hier
> steht; erfinde keine neuen Features, lass nichts weg.

---

## 1. Auftrag

Du bist Produkt-Designer für **floQ**, ein schlankes SaaS-Verrechnungstool
für Freiberufler und Ein-Personen-Unternehmen (floq.at). Gestalte alle
unten spezifizierten Screens als kohärentes Design-System: Layout,
Typografie, Farb-Tokens, Komponenten, Zustände, Responsive-Verhalten.
Liefere pro Screen ein vollständiges Design (Desktop primär, mobil
brauchbar) plus ein Token-/Komponenten-Sheet.

## 2. Produkt & Marke

- **floQ** — sprich „flock", österreichischer Slang für Geld.
  Claim: *„nur Cash is' fesch"*. Ton: unaufgeregt, präzise, mit einem
  Augenzwinkern — aber nie verspielt auf Kosten der Seriosität
  (es geht um Rechnungen und Geld).
- Zielnutzer: **eine Person**, die schnell eine Honorarnote schreiben
  will. Kein Buchhaltungs-Profi. Onboarding ohne Stammdaten-Pflicht:
  Empfänger wird direkt in den Beleg getippt.
- Kern-Flow (der wichtigste Weg durchs Produkt, muss friktionslos sein):
  **Login → Neue Rechnung → Empfänger + Positionen eintippen →
  PDF-Vorschau → Abschließen → per Mail versenden.**

### Brand-Ist-Stand (darauf aufbauen, gern verfeinern)

- Schrift: **Inter** (400/500/600/700/800), Zahlen tabellarisch.
  Landing nutzt zusätzlich Cormorant Garamond italic für den Claim.
- Farben: warmes Off-White `#FAF7F2` als App-Hintergrund, Ink `#1A1A1A`,
  Gold-Akzent `#B08D57` (das „Q" der Wortmarke), Karten weiß.
- Statusfarben (EINE Farbsprache, nur für Beleg-Status verwenden):
  ok-grün `#1E874B`, warn-amber `#B5710E`, danger-rot `#C73E36`,
  info-blau `#4A6FA5`, neutral-grau.
- Logo: Bildmarke (Maske, einfärbbar) + Wortmarke „floQ" mit goldenem Q.

### Harte Regeln

1. **White-Label beim Endkunden**: Alles, was der RECHNUNGSEMPFÄNGER
   sieht (öffentliche Dokument-Seite, E-Mail, PDF), trägt AUSSCHLIESSLICH
   den Brand des floQ-Kunden (Firmenname aus dessen Profil) — niemals
   floQ-Branding. floQ-Brand nur in der App selbst.
2. Sprache: **Deutsch (de-AT)**. Beträge `1.234,56 €`, Datum `TT.MM.JJJJ`.
3. Beleg-Status ist die einzige Farbcodierung in Listen: Entwurf=warn,
   Abgeschlossen=ok, Versendet=info, Gesehen=neutral, Storniert=danger.
   Belegart (AN/RE/GS/SR/MA) bleibt ein NEUTRALES Kürzel-Tile.
4. Technik-Constraints: Server-gerendertes HTML (Razor) + vanilla JS,
   CSS-Custom-Properties als Token-System, kein UI-Framework. Die
   PDF-Vorschau ist ein natives PDF-`<iframe>` (Chrome-Viewer) —
   dessen Innenleben ist nicht stylebar, nur der Rahmen drumherum.

## 3. Informationsarchitektur

```
/                          Landing (öffentlich, Marketing-Minimal)
/auth/login                Passkey-Login (öffentlich)
/auth/register             Passkey-Registrierung (öffentlich)
App (eingeloggt, gemeinsame Shell mit Top-Navigation):
  /Billing                 Dashboard „Übersicht"
  /Billing/Documents       Belegliste
  /Billing/Document        Beleg-Workbench (Editor + Vorschau + Lebenszyklus)
  /Settings/CompanyProfile Firmenprofil (Absender/Brand fürs PDF)
  /Settings/Mail           E-Mail-Versand (SMTP)
/d?t={token}               Öffentliche Dokument-Seite für den ENDKUNDEN
                           (White-Label! kein floQ-Brand)
/Error                     Fehlerseite
(503-Wartungsseite: minimal, serverseitig — nur Textblock)
Beleg-PDF                  A4-Druckbild (wird als PDF gerendert)
```

App-Shell: Header 60px, sticky. Links Bildmarke + Wortmarke, Mitte
Navigation als Pills („Übersicht", „Belege", „Einstellungen" — aktiver
Punkt gold hinterlegt), rechts Anzeigename des Users + „Abmelden".
Content max. 1280px zentriert.

## 4. Screens im Detail

### 4.1 Landing `/` (öffentlich)

Zentrierter Lockup: Bildmarke, Wortmarke „floQ" (Q gold), darunter
IPA-Lautschrift `[ˈflɔɐk]` in Monospace, Claim „nur Cash is' fesch"
in Serifen-Italic. Darunter: anonym „Anmelden / Registrieren"-Links;
eingeloggt „Zur App" + „Angemeldet als … · Abmelden".
Footer: „In Vorbereitung" (uppercase, klein). Bewusst minimal — darf
so bleiben, gern typografisch verfeinern.

### 4.2 Auth `/auth/login` + `/auth/register` (öffentlich)

Schmale zentrierte Spalte, floQ-Brand oben. **Nur Passkey, kein
Passwort-Feld!**
- Login: H1 „Anmelden", Lead „Mit Passkey. Touch ID, Windows Hello,
  YubiKey — was du registriert hast.", ein E-Mail-Feld, ein
  Submit-Button, darunter Status-Zeile (live, z.B. „Warte auf
  Passkey…"), Link „Noch kein Konto? Registrieren".
- Register analog: E-Mail + Anzeigename, erzeugt Passkey, Status-Zeile.
- Zustände: Idle, WebAuthn-Dialog offen (Browser-nativ), Fehler
  (rote Statuszeile), Erfolg (Redirect).

### 4.3 Dashboard `/Billing`

- **Page-Head**: Titel „Übersicht", darunter aktuelles Datum
  (z.B. „Mittwoch, 02. Juli 2026"). Rechts primärer Button
  „+ Neue Rechnung" (legt sofort einen Entwurf an und springt in die
  Workbench).
- **4 KPI-Kacheln** (klickbar, verlinken in die gefilterte Liste):
  1. „Entwürfe" — Anzahl, Sub „in Bearbeitung" (warn-Ton wenn > 0)
  2. „Offene Forderungen" — Summe €, Sub „n Rechnungen"
  3. „Überfällig" — Anzahl, Sub Summe € (danger-Ton wenn > 0)
  4. „Umsatz {Jahr}" — Summe €, Sub „abgeschlossene Rechnungen"
- **Zwei Karten nebeneinander** (rechts schmaler):
  - „Überfällige Rechnungen" (Sub „nach Fälligkeit", Button „Alle
    Belege"): bis 8 Zeilen — Typ-Kürzel-Tile, Belegnummer, Meta
    „Kunde · fällig TT.MM.JJJJ", rechts offener Betrag.
    Leer-Zustand: „Nichts überfällig — sehr fesch."
  - „Zuletzt bearbeitet": bis 8 Zeilen analog, Meta „Kunde · Datum".
    Leer-Zustand: „Noch keine Belege. Leg mit ‚Neue Rechnung' los."

### 4.4 Belegliste `/Billing/Documents`

Zweispaltig: Liste links, Filter-Aside rechts (320px).
- **Page-Head**: „Belege", Sub „n von m". Rechts „+ Neuer Beleg".
- **Listenzeile** (klickbar → Workbench): Typ-Kürzel-Tile (AN/RE/GS/
  SR/MA, neutral), Belegnummer fett (oder „Entwurf"), daneben
  Kunden-Chip (runder Avatar mit 2 Initialen + Name, abgeschnitten),
  Meta-Zeile „Belegart · Datum · Leistungszeitraum". Rechts
  Status-Badge (Punkt + Label, Statusfarbe) und Brutto-Betrag
  (tabellarische Ziffern; 0,00 gedimmt).
  Leer-Zustand: „Keine Belege gefunden".
- **Aside, 3 Karten**:
  1. „Status" — Filter-Pills je vorkommendem Status mit Zähler,
     an/abwählbar (Mehrfachauswahl)
  2. „Belegart" — Pills „RE · Rechnung (n)" etc.
  3. „Ergebnis" — Meta-Zeilen: Belege n, Entwürfe n, Σ Brutto €
- **Modal „Belegtyp wählen"** (über „+ Neuer Beleg"): 5 große
  Auswahl-Buttons mit Kürzel-Tile + Label (Rechnung, Angebot,
  Gutschrift, Stornorechnung, Mahnung). Gutschrift/Storno/Mahnung
  führen erst zum Originalrechnungs-Picker (4.5).

### 4.5 Beleg-Workbench `/Billing/Document` — der wichtigste Screen

Zweispaltig: Inhalt links (Tabs), Aktions-Aside rechts.

**Page-Head**: Belegtyp als Titel (z.B. „Rechnung") + Nummer;
Sub-Zeile: bei Entwurf „Entwurf — Nummer beim Abschluss: 2026-11-0007"
(Vorschau der nächsten Nummer!), sonst „Rechnung vom TT.MM.JJJJ".
Rechts „Zur Liste".

**Tabs**: Empfänger · Positionen · Rechnungen (nur Mahnung, ersetzt
Positionen) · Details · Vorschau.

- **Tab Empfänger**: Karte „Empfänger", Sub „Direkt eintippen — der
  Beleg ist autark, kein Kundenstamm nötig." Felder: Name* (voll),
  Adresse (voll), PLZ, Ort, Land (ISO-2), UID, E-Mail (voll).
- **Tab Positionen**: Karte mit Kopf „Positionen" + Button
  „+ Position". Editierbare Tabelle: Bezeichnung (Freitext, breit),
  Menge, Einheit (Freitext, z.B. „Std."), Einzelpreis €, USt %,
  Netto (berechnet, read-only), Zeilen-Aktionen: „%" (Rabattzeile
  unter der Position einfügen — eingerückt, Preis negativ) und „×"
  (Zeile samt Rabatten löschen). Darunter rechtsbündige Summen-Box:
  „Gesamtbetrag netto", je USt-Satz „zzgl. Umsatzsteuer 20 %", bei
  Steuerbefreiung stattdessen „zzgl. Umsatzsteuer 0 % — 0,00 €",
  fett „Gesamtbetrag brutto" — live beim Tippen.
- **Tab Rechnungen** (nur Mahnung): Liste der gemahnten Rechnungen
  (Nummer + offener Betrag, read-only), darunter Felder: Mahnstufe
  (Select: Zahlungserinnerung / 1.–3. Mahnung), Zahlungsziel (Datum),
  Mahngebühr €, Verzugszinsen € (optional).
- **Tab Details**: Formular-Grid — Belegdatum; Leistungsdatum,
  Leistungszeitraum von/bis (nur Angebot+Rechnung); Gültig bis +
  Referenz (nur Angebot); Zahlungsziel (Tage), Skonto-Frist (Tage),
  Skonto %; Steuerbefreiung (Select: Keine / Kleinunternehmer §6 Abs 1
  Z 27 UStG / Reverse Charge EU-B2B / Drittland); Hinweistext
  Steuerbefreiung (leer = Standard); Konditionen (nur Angebot,
  Textarea); Notiz.
- **Tab Vorschau**: PDF-iframe (A4, ~78vh) mit Lade-Overlay „PDF wird
  erzeugt …". Wechsel auf den Tab speichert vorher automatisch.

**Aside, 3–4 Karten**:
1. „Status": Status-Badge, Meta-Zeilen Nummer + Brutto.
2. „Aktionen" (vertikaler Button-Stack, je Zustand):
   - Entwurf: „Speichern" (primär), „Abschließen" (akzent),
     „Entwurf verwerfen" (danger-outline)
   - Abgeschlossen: „Versenden" (akzent), „PDF herunterladen",
     „Weiterverarbeiten", „Entsperren"
3. „Versand" (nur abgeschlossen): Historie je Versand — Empfänger-Mail
   fett, darunter „gesendet TT.MM. · Link/PDF-Anhang · geöffnet ×n ·
   geladen ×n". Leer: „Noch nicht versendet."
4. „Zahlungen" (nur abgeschlossene Rechnung): Zeilen „TT.MM.JJJJ ·
   Überweisung — Betrag ×(löschen)", Button „+ Zahlung erfassen".

**Modals**:
- „Originalrechnung wählen" (Einstieg Gutschrift/Storno/Mahnung):
  Liste klickbarer Einträge „RE 2026-11-0001 – 02.07.2026 – 405,00 €".
  Leer: „Keine abgeschlossene Rechnung vorhanden."
- „Weiterverarbeiten zu …": Erklärungssatz + 5 Zieltyp-Buttons.
- „Beleg versenden": Empfänger-E-Mail* (vorbefüllt aus Beleg),
  persönliche Nachricht (Textarea), Checkboxen „PDF als Datei-Anhang
  statt Ansichts-Link" und „Kopie an mich (Firmen-E-Mail)" (default an),
  Buttons Abbrechen / „Senden" (mit Sendet…-Zustand).
- „Zahlung erfassen": Betrag €*, Zahldatum*, Zahlungsweg (Überweisung/
  Bar/Karte/SEPA-Lastschrift/Sonstige), Referenz, Notiz.

**Zustände**: Entwurf = alles editierbar. Abgeschlossen = Formulare
gesperrt (read-only), Start-Tab ist die Vorschau. Storniert = wie
abgeschlossen, aber ohne „Versenden"/Zahlungen. Bestätigungs-Dialoge
vor Abschließen („Nummer wird gezogen…"), Entsperren, Verwerfen,
Zahlung löschen. Feedback via Toast unten mittig (dunkel; Fehler rot).

### 4.6 Firmenprofil `/Settings/CompanyProfile`

Sub: „Absender, Footer und Briefpapier deiner Belege — dein Kunde sieht
ausschließlich diesen Brand." Rechts oben „Speichern".
- Karte „Stammdaten": Firmenname/Name*, Straße, PLZ, Ort, Land (ISO-2),
  UID, E-Mail, Telefon, Website.
- Karte „Bankverbindung": IBAN, BIC, Bank.
- Karte „Umsatzsteuer" (Sub: „Kleinunternehmer: neue Belege starten
  ohne USt, der Pflichthinweis wird gedruckt."): Checkbox
  „Kleinunternehmerregelung (§6 Abs 1 Z 27 UStG)" + Textarea „Eigener
  Hinweistext (leer = gesetzlicher Standardtext)".
- Aside „Briefpapier": Erklärtext (Vektor-PDF als Hintergrund jeder
  Beleg-Seite), Status „hinterlegt/keines", Buttons „PDF hochladen" /
  „Briefpapier entfernen" (danger, nur wenn vorhanden).
- Aside „Weitere Einstellungen": Link „E-Mail-Versand".

### 4.7 E-Mail-Versand `/Settings/Mail`

Sub: „Belege gehen über deinen eigenen Mail-Server raus — dein Kunde
sieht nur deinen Absender." Rechts oben „Test-Mail senden" + „Speichern".
- Karte „SMTP-Zugang": Host*, Port (Default 587), Benutzername,
  Passwort (write-only: Hint „gespeichert — nur zum Ändern ausfüllen" /
  „noch keines gespeichert"), Absender-Adresse (From)*,
  Absender-Anzeigename (leer = Firmenname).
- Aside „Hinweise": Port-Erklärung (465 SSL / 587 STARTTLS),
  Passwort-Verschlüsselungs-Hinweis, Test-Mail-Erklärung.
- Aside-Link zurück zum Firmenprofil.

### 4.8 Öffentliche Dokument-Seite `/d?t={token}` — WHITE-LABEL!

Publikum: der RECHNUNGSEMPFÄNGER (klickt den Link aus der Mail).
**Kein floQ-Branding.** Neutral-elegant, vertrauenswürdig.
- Gültig: schlanker Header — links Firmenname des Ausstellers (fett)
  + darunter „Rechnung 2026-11-0001"; rechts Pill-Button
  „PDF herunterladen". Content: PDF-iframe (max 900px, volle Höhe).
  Footer: „Firmenname · office@…" (dezent).
- Ungültiger Token: zentrierte Karte „Dokument nicht gefunden" +
  Erklärtext (Link vollständig kopiert?).
- Abgelaufen: „Link abgelaufen" + Hinweis, sich an den Aussteller zu
  wenden.

### 4.9 Versand-E-Mail (HTML-Template) — WHITE-LABEL!

Karte (max 560px) auf hellgrauem Grund: optional persönliche Nachricht
des Ausstellers, dann Button „Dokument ansehen" (dunkel) + Fallback-Link
als Klartext; bei Anhang-Versand stattdessen Hinweiszeile „Den Beleg
finden Sie im Anhang dieser E-Mail." Footer-Zone: Firmenname fett,
Adresse, E-Mail · UID · IBAN. Nur Inline-CSS-taugliches Design.

### 4.10 Beleg-PDF (A4-Druckbild)

Layout ist datengetrieben (mm-Koordinaten pro Tenant konfigurierbar),
Default: Empfängerblock links auf Fensterkuvert-Position (~50mm von
oben), Merkmal-Block rechts (Nummer/Datum/Leistungszeitraum/Gültig
bis/Referenz/UID als Label-Wert-Tabelle), Titel = Belegtyp (fett,
~100mm), Vortext, Positionstabelle (Pos./Bezeichnung/Einzelpreis/
Menge/Netto; Rabatt-Subzeilen eingerückt mit „• Bezeichnung 10%"),
Summen-Zone (netto → USt je Satz, bei Befreiung IMMER „zzgl.
Umsatzsteuer 0 % — 0,00 €" → brutto fett), Pflichthinweis bei
Steuerbefreiung, Notiz, Zahlungsbedingungen-Satz, Endtext. Footer auf
JEDER Seite: 3 Spalten (Firma+UID / Adresse / E-Mail+IBAN), Hairline
darüber. Mahnung: statt Positionen eine Tabelle der gemahnten
Rechnungen + Zwischensumme/Mahngebühr/Zinsen/Gesamt/Zahlungsziel.
Hinter allem kann ein Kunden-Briefpapier (Vektor-PDF) liegen — das
Druckbild muss auch OHNE Briefpapier gut aussehen.

### 4.11 Fehler & Sonderfälle

- `/Error`: generische Fehlerseite (aktuell Razor-Standard — bitte
  floQ-konform gestalten: Marke, „Da ist etwas schiefgelaufen",
  Zurück-Link).
- Wartungsseite (503, wenn ein Mandant stillgelegt ist): minimaler
  Textblock, neutral halten.
- Toasts: unten mittig, dunkel (Fehler rot), verschwinden nach 4s.
- Bestätigungs-Dialoge: aktuell native confirm() — gern als gestyltes
  Modal-Pattern mitdesignen.

## 5. Komponenten-Inventar (einmal designen, überall nutzen)

Buttons (primär/akzent/sekundär/danger/sm) · KPI-Kachel · Karte mit
Header/Sub · Listenzeile mit Kürzel-Tile · Kunden-Chip mit Initialen-
Avatar · Status-Badge mit Punkt · Filter-Pill mit Zähler · Tabs ·
Formularfeld (Label oben, Input 38px) · Checkbox-Zeile · editierbare
Positionstabelle · Summen-Box · Modal (Eyebrow + Titel + Body + Footer)
· Auswahl-Kachel (Belegtyp/Weiterverarbeiten) · Pick-Liste · Toast ·
PDF-Vorschau-Rahmen mit Lade-Overlay · Leer-Zustände (freundlich,
eine Zeile, gern mit floQ-Schmäh).

## 6. Deliverables

1. Token-Sheet (Farben, Typo-Skala, Radii, Spacing, Schatten) —
   als CSS-Custom-Properties benennbar.
2. Komponenten-Sheet (alle Komponenten aus §5 in allen Zuständen:
   default/hover/aktiv/disabled/Fehler).
3. Alle Screens aus §4 als Desktop-Design (1280px-Content), die App-
   Screens zusätzlich als Mobile-Variante (~390px; Aside rutscht unter
   den Inhalt, Tabellen werden scrollbar/gestapelt).
4. Der Kern-Flow (§2) als zusammenhängende Sequenz.
