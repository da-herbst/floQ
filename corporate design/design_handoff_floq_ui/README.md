# Handoff: floq — UI-Design Phase 1

## Overview

Komplettes UI-Design für **floq** (floq.at), ein schlankes SaaS-Verrechnungstool für
Freiberufler und Ein-Personen-Unternehmen in Österreich. Abgedeckt: Landing, Passkey-Auth,
Dashboard, Belegliste, Beleg-Workbench (Entwurf + Abgeschlossen), Modals, Firmenprofil,
E-Mail-Versand-Settings, öffentliche White-Label-Dokumentseite, Versand-E-Mail-Template,
A4-Beleg-PDF, Fehler-/503-Seiten — plus Token- und Komponenten-System.

Funktionsumfang laut Design-Brief (Phase 1) ist FIX: umsetzen, was hier spezifiziert ist,
keine neuen Features erfinden, nichts weglassen.

## About the Design Files

Die Dateien in diesem Bundle sind **Design-Referenzen in HTML** — Prototypen, die Look und
Verhalten zeigen, KEIN Produktionscode. Aufgabe: diese Designs in der Ziel-Codebase
nachbauen — laut Brief **server-gerendertes HTML (Razor) + vanilla JS,
CSS-Custom-Properties als Token-System, kein UI-Framework**. Die HTML-Dateien nicht
direkt übernehmen; die Inline-Styles dienen als exakte Spezifikation und sollten in ein
sauberes CSS-Token-/Klassensystem übersetzt werden.

`floq UI Design.dc.html` im Browser öffnen (mit `support.js` und `assets/` im selben
Ordner) — alle Screens liegen untereinander auf einem Canvas, jeweils mit Mono-Label
(§-Referenz auf den Design-Brief).

## Fidelity

**High-fidelity.** Farben, Typografie, Abstände, Zustände und Copy sind final gemeint.
Pixel-genau nachbauen. Alle Werte unten sind verbindlich.

---

## Design-Prinzipien (das Wichtigste zuerst)

1. **Streng schwarz-weiß.** Keine Statusfarben, kein Gold, keine Akzentfarbe. Nirgends.
2. **Status = Zeichen, nicht Farbe** (Mono-Font, Versal, letter-spacing 0.1em):
   - `○ ENTWURF` · `● ABGESCHLOSSEN` · `➔ VERSENDET` · `◐ GESEHEN` · `✕ STORNIERT` (Label durchgestrichen, Farbe #8A8A86)
   - Überfällig wird durch Unterstreichung/Gewicht betont, nie durch Rot.
3. **Hairlines statt Boxen.** Keine Card-Container, keine Schatten, `border-radius: 0` überall.
   Struktur entsteht durch 1px-Linien und Weißraum. Abschnitts-Header: Mono-Label 11px
   Versal + 1px schwarze Linie darunter (`border-bottom: 1px solid #111; padding-bottom: 12px`).
4. **Formularfelder als Unterstrich-Zeilen** — kein Box-Input:
   Label (Mono 11px, Versal, letter-spacing 0.14em, #999) über dem Wert,
   Wert 15px mit `border-bottom: 1px solid`. Gefüllt/fokussiert: #111. Leer/idle: #CACAC6.
   Placeholder-Text: #9A9A96. Fehler: Zusatz-Label rechts `! PFLICHTFELD` (Mono 10.5px).
5. **Zahlen immer IBM Plex Mono**, Beträge im Format `1.234,56 €` (Tausenderpunkt,
   Dezimalkomma, schmales Leerzeichen `&thinsp;` vor €). Datum `TT.MM.JJJJ`. Sprache de-AT.
6. **White-Label hart einhalten:** Alles, was der Rechnungsempfänger sieht (öffentliche
   Dokumentseite, E-Mail, PDF), trägt ausschließlich den Brand des floq-Kunden
   (Firmenname aus Profil) — niemals floq-Logo/-Name.

## Design Tokens

### Farben (nur Graustufen)

```css
--ink:    #111111;  /* Text, Linien stark, Primär-Buttons */
--ink-2:  #55554F;  /* Sekundärtext, PDF-Viewer-Hintergrund */
--muted:  #8A8A86;  /* Meta, inaktive Nav */
--muted-2:#9A9A96;  /* Placeholder, Deko-Zeichen */
--faint:  #B4B4B0;  /* Schwächste Textstufe (z.B. 0,00 €, „IN VORBEREITUNG") */
--line:   #E5E5E3;  /* Hairlines Standard */
--line-2: #CACAC6;  /* Input-Unterstrich idle */
--fill:   #F6F6F4;  /* Flächen (öffentl. Dokumentseite) */
--card:   #FFFFFF;  /* App-Hintergrund */
--frame:  #D9D9D7;  /* Außenrahmen der Screens (nur im Mock) */
```

E-Mail-Desk-Hintergrund: `#EFEFED`. Modal-/PDF-Backdrop: `#55554F` (im Mock als Fläche;
in der App: Overlay `rgba(17,17,17,0.55)` empfohlen).

### Typografie

- **Hanken Grotesk** (Google Fonts, 300/400/500/600/700) — UI-Schrift.
- **IBM Plex Mono** (400/500) — Labels, Zahlen, Belegnummern, Status, Kürzel, IPA.
- **Newsreader** italic (Google Fonts) — NUR für Claim & Leer-Zustands-Schmäh.

| Rolle | Font | Größe/Gewicht | Extras |
|---|---|---|---|
| Wortmarke Landing | Hanken | 72 / 600 | letter-spacing −0.02em |
| Seitentitel | Hanken | 30 / 500 | letter-spacing −0.02em |
| KPI-Zahl | Hanken | 44 / 300 | letter-spacing −0.02em; Überfällig: 500 + underline 3px, offset 6px |
| Zwischentitel | Hanken | 15 / 600 | |
| Fließtext | Hanken | 14.5 / 400 | line-height 1.55, Farbe #33332F o. #55554F |
| Meta | Hanken | 13–13.5 / 400 | #8A8A86 |
| Label/Eyebrow | Plex Mono | 11 / 400 | Versal, letter-spacing 0.14em, #999 |
| Status | Plex Mono | 11.5–12 / 400 | Versal, letter-spacing 0.1em |
| Beträge Listen | Plex Mono | 13–14 / 400–500 | rechtsbündig |
| Claim/Schmäh | Newsreader | 15–23 / 400 italic | #55554F bzw. #33332F |

### Geometrie

- `border-radius: 0` — ausnahmslos. Keine `box-shadow`.
- Hairline 1px; „starke" Linien (Abschnitts-Header, Summen-Trenner, Tabellenkopf) 1px #111.
- Spacing-Skala: 8 / 16 / 24 / 40 / 64. Content max **1280px** zentriert.
- App-Header: 60px hoch, weiß, 1px #E5E5E3 unten.

## Komponenten

### Buttons (Höhe 44px; in Modals 42px; Padding 0 22–26px; font 14.5/600)

- **Primär/Akzent**: `background:#111; color:#fff`. Hover: #000.
- **Sekundär**: weiß, `border:1px solid #111`. Hover: `background:#F6F6F4`.
- **Tertiär/Destruktiv** („Entwurf verwerfen", „Entsperren", „Abbrechen"): reiner Textlink
  #55554F, `text-decoration: underline; text-underline-offset: 3px`. Destruktives immer
  mit Bestätigungs-Modal absichern (kein Rot!).
- **Abgestuft-Sekundär** („Weiterverarbeiten"): border #CACAC6, Text #33332F.
- Disabled: Text #B4B4B0, border/fill entsprechend blass.

### Navigation (App-Shell)

Links Bildmarke (`assets/hand-black.png`, 20px breit) + „floq" (17/600).
Mitte Textlinks 14.5px: aktiv = 600 + `border-bottom: 2px solid #111` (volle Headerhöhe,
padding 18px 0 16px), inaktiv #8A8A86. Rechts „Anna Leitner" + „Abmelden" (unterstrichen, #8A8A86).

### Listenzeile Belege

Flex, `padding: 18px 0`, `border-bottom: 1px solid #E5E5E3`, oberste Linie der Liste 1px #111.
Spalten: Kürzel (Mono 12, #8A8A86, Breite 24px) · Nummer (Mono 14.5/500; „Entwurf" als Text) +
Meta-Zeile (13, #8A8A86: „Belegart · Datum · LZ …") · Kundenname (14, #33332F) · Spacer ·
Status-Zeichen+Label (Mono 11.5, Versal) · Betrag (Mono 14, rechtsbündig, Breite 110px;
0,00 € in #B4B4B0; negative Beträge mit −). Hover: `background:#FAFAF8`. Ganze Zeile klickbar.

### Filter (Aside, 300px)

Abschnitts-Header wie oben. Einträge Mono 12px als Zeile `LABEL … Zähler`:
aktiv = #111 + `border-bottom:1px solid #111` am Label; inaktiv #9A9A96. Mehrfachauswahl (Toggle).

### Tabs

Zeile mit `border-bottom:1px solid #E5E5E3`, Tabs 14.5px, gap 28px;
aktiv 600 + `border-bottom:2px solid #111` (überlappt die Hairline), inaktiv #8A8A86.

### KPI-Zeile (Dashboard)

Ein Block, `border-top: 1px solid #111`, 4 Spalten getrennt durch `border-left:1px solid #E5E5E3`,
padding 28px 32px. Label Mono 11 → Zahl 44 → Subline 13.5. Klickbar → gefilterte Belegliste.

### Positionstabelle (Workbench)

Grid `1fr 70px 70px 110px 60px 110px 60px`, gap 12px.
Kopf: Mono 10.5 Versal #999, `border-bottom:1px solid #111`.
Editierbare Zellen = Unterstrich-Felder (#CACAC6); berechnetes Netto read-only ohne
Unterstrich (#55554F). Zeilen-Aktionen rechts: `%` (Rabattzeile einfügen) und `✕` (löschen),
Mono 12 #8A8A86, Hover #111.
**Rabattzeile**: eingerückt (padding-left 28px), Prefix `• `, 13.5px #55554F, Beträge negativ.
**Summen-Box**: rechtsbündig, Breite 340px: netto / je USt-Satz („zzgl. Umsatzsteuer 20 %";
bei Befreiung immer „zzgl. Umsatzsteuer 0 % — 0,00 €") in 14 #55554F, dann
`border-top:1px solid #111` + „Gesamtbetrag brutto" 15.5/600. Live bei Eingabe.

### Modal

Weißes Rechteck (Breite 380–440px), kein Radius, kein sichtbarer Schatten nötig
(Backdrop trennt). Aufbau: Eyebrow (Mono 10.5, Versal, 0.16em, #999) → Titel (19–21/600)
→ Body → Footer rechtsbündig: „Abbrechen" (Textlink) + Primär-Button.
Pick-Listen im Modal: Zeilen mit Hairline-Trennern (siehe „Belegtyp wählen").

### Toast

Unten mittig, `background:#111; color:#fff`, 13.5px, padding 13px 20px, kein Radius,
auto-dismiss 4s. Fehler-Toast: gleicher Look + Prefix `! ` (kein Rot).

### PDF-Vorschau-Rahmen

Fläche `#55554F`, darin zentriert weiße A4-Seite (aspect-ratio 210/297). In der App:
natives PDF-`<iframe>` (~78vh), Innenleben nicht stylebar. Lade-Overlay: Fläche mit
Mono-Text „PDF WIRD ERZEUGT …" zentriert. Tab-Wechsel auf „Vorschau" speichert vorher.

### Leer-Zustände

Eine Zeile Newsreader italic 15px #9A9A96, z.B. „— sonst nichts überfällig, sehr fesch."
/ „Noch keine Belege. Leg mit ‚Neue Rechnung' los." / „Keine Belege gefunden".

## Screens (Referenz: Labels im Mock = §-Nummern des Design-Briefs)

### §4.1 Landing `/`
Zentriert auf weiß: Hand (104px) → „floq" (72/600, −0.02em, Abstand 44px) → IPA
`[ˈflɔɐk]` (Mono 15, #9A9A96, 0.06em) → Claim „nur Cash is' fesch" (Newsreader italic 23,
#33332F) → Links „Anmelden" (600, unterstrichen via border-bottom 1px) / „Registrieren"
(#8A8A86), gap 32px. Footer absolut unten: „IN VORBEREITUNG" (Mono 10.5, 0.22em, #B4B4B0).
Eingeloggt: statt der Links „Zur App" + „Angemeldet als … · Abmelden".

### §4.2 Auth `/auth/login` + `/auth/register`
Spalte 340px zentriert, oben Mini-Brand (Hand 22px + „floq" 17/600). Titel 30/500,
Lead 14.5 #55554F. **Nur Passkey, kein Passwort.** E-Mail-Unterstrichfeld,
Fullwidth-Primärbutton 46px („Mit Passkey anmelden" / „Passkey erstellen"),
Status-Zeile darunter live (Mono 11.5, 0.08em, #8A8A86, z.B. „WARTE AUF PASSKEY …";
Fehler gleicher Stil mit `! `-Prefix). Register zusätzlich Feld „Anzeigename" mit
Pflichtfeld-Fehlerdarstellung. Fußlink „Noch kein Konto? Registrieren" / „Schon dabei? Anmelden".

### §4.3 Dashboard `/Billing`
Page-Head: „Übersicht" + Datum („Mittwoch, 02. Juli 2026"), rechts Primärbutton
„+ Neue Rechnung" (legt sofort Entwurf an → Workbench). KPI-Zeile (4 Kacheln, s.o.):
Entwürfe / Offene Forderungen / Überfällig / Umsatz {Jahr}. Darunter Grid `1.6fr 1fr`,
gap 64px: Liste „Überfällige Rechnungen" (Header-Zeile mit Titel + „nach Fälligkeit" +
Link „Alle Belege"; Zeilen: RE · Nummer · „Kunde · fällig TT.MM.JJJJ" · Betrag; max 8) und
Liste „Zuletzt bearbeitet" (RE/AN · Nummer bzw. „Entwurf" · „Kunde · Datum" · Status-Zeichen; max 8).

### §4.4 Belegliste `/Billing/Documents`
Page-Head „Belege" + „n von m", rechts „+ Neuer Beleg" → Modal „Belegtyp wählen"
(RE/AN/GS/SR/MA; GS/SR/MA führen erst zum Originalrechnungs-Picker). Grid `1fr 300px`,
gap 64px: Listenzeilen (s.o.) + Aside mit 3 Filterblöcken STATUS / BELEGART / ERGEBNIS
(Meta-Zeilen: Belege n · Entwürfe n · Σ Brutto €).

### §4.5 Beleg-Workbench `/Billing/Document`
Page-Head: Belegtyp als Titel + bei Entwurf Status-Zeichen daneben (Mono 15 „○ Entwurf");
Subline Entwurf: „Nummer beim Abschluss: 2026-11-0007" (Nummern-Vorschau!), sonst
„Rechnung vom TT.MM.JJJJ · Formulare gesperrt". Rechts Link „Zur Liste".
Grid `1fr 300px`, gap 64px. Tabs: Empfänger · Positionen · (Rechnungen, nur Mahnung,
ersetzt Positionen) · Details · Vorschau.

- **Empfänger**: Sub „Direkt eintippen — der Beleg ist autark, kein Kundenstamm nötig."
  Felder: Name* (voll), Adresse (voll), PLZ, Ort, Land (ISO-2), UID, E-Mail (voll).
- **Positionen**: s. Positionstabelle. Kopfzeile mit „+ Position" (Textlink).
- **Rechnungen** (Mahnung): read-only Liste „Nummer + offener Betrag", darunter Mahnstufe
  (Select), Zahlungsziel (Datum), Mahngebühr €, Verzugszinsen €.
- **Details**: Formular-Grid — Belegdatum; Leistungsdatum, Leistungszeitraum von/bis
  (nur AN+RE); Gültig bis + Referenz (nur AN); Zahlungsziel (Tage), Skonto-Frist (Tage),
  Skonto %; Steuerbefreiung (Select: Keine / Kleinunternehmer §6 Abs 1 Z 27 UStG /
  Reverse Charge EU-B2B / Drittland); Hinweistext Steuerbefreiung (leer = Standard);
  Konditionen (nur AN, Textarea); Notiz.
- **Vorschau**: PDF-iframe im #55554F-Rahmen; Tab-Wechsel speichert vorher.

Aside-Karten: **STATUS** (Zeichen+Label, Nummer [„folgt" bei Entwurf], Brutto) ·
**AKTIONEN** (Entwurf: Abschließen [primär] / Speichern [sekundär] / Entwurf verwerfen
[Textlink]; Abgeschlossen: Versenden [primär] / PDF herunterladen [sekundär] /
Weiterverarbeiten [abgestuft] / Entsperren [Textlink]) · **VERSAND** (nur abgeschlossen:
je Versand Empfänger-Mail 14/600 + Meta „gesendet TT.MM. · Link/PDF-Anhang · geöffnet ×n ·
geladen ×n"; leer: „Noch nicht versendet.") · **ZAHLUNGEN** (nur abgeschlossene RE:
„TT.MM.JJJJ · Überweisung — Betrag ✕", darunter „+ Zahlung erfassen").

Zustände: Entwurf = editierbar. Abgeschlossen = read-only, Start-Tab Vorschau.
Storniert = wie abgeschlossen ohne Versenden/Zahlungen. Bestätigungs-Modals vor
Abschließen („Nummer … wird gezogen."), Entsperren, Verwerfen, Zahlung löschen.

### Modals (im Mock auf #55554F-Fläche)
„Belegtyp wählen" (Pick-Liste, GS/SR/MA mit Hinweis „wählt Originalrechnung") ·
„Beleg versenden" (Empfänger* vorbefüllt, persönliche Nachricht, Checkbox „PDF als
Datei-Anhang statt Ansichts-Link", Checkbox „Kopie an mich (Firmen-E-Mail)" default AN,
Footer Abbrechen/Senden mit „Sendet…"-Zustand) · Bestätigung „Abschließen" ·
„Zahlung erfassen" (Betrag €*, Zahldatum*, Zahlungsweg-Select: Überweisung/Bar/Karte/
SEPA-Lastschrift/Sonstige, Referenz, Notiz) · „Originalrechnung wählen" (Pick-Liste
„RE 2026-11-0001 – 02.07.2026 – 405,00 €"; leer: „Keine abgeschlossene Rechnung
vorhanden.") · „Weiterverarbeiten zu …" (Satz + 5 Zieltyp-Einträge).
Checkboxen: 16×16px, 1px #111, checked = gefüllt #111 + weißes ✓.

### §4.6 Firmenprofil `/Settings/CompanyProfile`
Sub: „Absender, Footer und Briefpapier deiner Belege — dein Kunde sieht ausschließlich
diesen Brand." Rechts „Speichern". Blöcke: STAMMDATEN (2-spaltig; Name* + Straße volle
Breite) · BANKVERBINDUNG (IBAN [Mono] / BIC [Mono] / Bank) · UMSATZSTEUER (Checkbox
Kleinunternehmerregelung + Hinweis-Subline + Textarea „Eigener Hinweistext",
Placeholder „Leer = gesetzlicher Standardtext."). Aside: BRIEFPAPIER (Erklärtext,
Status „● HINTERLEGT" / „○ KEINES", „PDF hochladen" [sekundär], „Briefpapier entfernen"
[Textlink, nur wenn vorhanden]) · WEITERE EINSTELLUNGEN → „E-Mail-Versand".

### §4.7 E-Mail-Versand `/Settings/Mail`
Sub: „Belege gehen über deinen eigenen Mail-Server raus — dein Kunde sieht nur deinen
Absender." Rechts „Test-Mail senden" (sekundär) + „Speichern" (primär).
SMTP-ZUGANG: Host* (Mono) / Port (Mono, Default 587) / Benutzername / Passwort
(write-only, Hint „gespeichert — nur zum Ändern ausfüllen" bzw. „noch keines
gespeichert") / Absender-Adresse (From)* / Anzeigename (Placeholder „leer = Firmenname").
Aside HINWEISE: 3 Absätze mit Hairline-Trennern (465 SSL / 587 STARTTLS · Passwort
verschlüsselt · Test-Mail an Firmen-E-Mail) + Rücklink Firmenprofil.

### §4.8 Öffentliche Dokumentseite `/d?t={token}` — WHITE-LABEL
Hintergrund #F6F6F4. Weißer Header: links Ausstellername (16/700) + „Rechnung
2026-11-0001" (13.5 #8A8A86), rechts „PDF herunterladen" (sekundär-Button).
Content: PDF-iframe zentriert (max 900px). Footer dezent zentriert:
„Firmenname · office@…" (12.5, #9A9A96). **Kein floq-Brand, keine floq-Schrift-Marke.**
Fehlzustände als zentrierte Blöcke: „Dokument nicht gefunden" (+ „Wurde er vollständig
kopiert?") / „Link abgelaufen" (+ Hinweis an Aussteller wenden).

### §4.9 Versand-E-Mail — WHITE-LABEL, nur Inline-CSS
Desk #EFEFED, Karte 560px weiß, 1px #E0E0DE. Inhalt: optionale persönliche Nachricht
(15/1.65 #33332F) → Button „Dokument ansehen" (46px, #1A1A1A, weiß, fullwidth) →
Fallback-Link als Klartext (12.5 #8A8A86, unterstrichen). Variante PDF-Anhang: statt
Button die Zeile „Den Beleg finden Sie im Anhang dieser E-Mail." + Anhangs-Hinweis.
Footer-Zone (Hairline oben): Firmenname 13.5/700, dann Adresse · E-Mail · UID · IBAN
(12.5 #8A8A86). Tabellen-Layout für Mail-Clients, alles inline.

### §4.10 Beleg-PDF (A4)
Weiß, Ränder ~58px @620px-Mock (≈ 20mm). Absenderzeile klein unterstrichen auf
Fensterkuvert-Position (~50mm von oben) + Empfängerblock darunter; rechts Merkmal-Block
als Label-Wert-Grid (Nummer/Datum/Leistungszeitraum/Gültig bis/Referenz/UID).
Titel = Belegtyp (16/700, ~100mm). Vortext. Positionstabelle
(Pos./Bezeichnung/Einzelpreis/Menge/Netto), Kopf mit 0.75px-Linie #111; Rabatt-Subzeilen
eingerückt „• Bezeichnung 10 %". Summen-Zone rechts 46%: netto → USt je Satz (bei
Befreiung IMMER „zzgl. Umsatzsteuer 0 % — 0,00 €") → brutto fett mit Linie. Pflichthinweis
bei Steuerbefreiung, Notiz, Zahlungsbedingungen, Endtext. Footer auf JEDER Seite:
Hairline + 3 Spalten (Firma+UID / Adresse / E-Mail+IBAN, 8px-Äquivalent).
Mahnung: statt Positionen Tabelle der gemahnten Rechnungen + Zwischensumme/Mahngebühr/
Zinsen/Gesamt/Zahlungsziel. Muss auch OHNE hinterlegtes Briefpapier gut aussehen.
Koordinaten sind pro Tenant in mm konfigurierbar — Layout datengetrieben halten.

### §4.11 Fehler & Sonderfälle
`/Error`: zentriert, Hand 40px mit opacity 0.25, „Da ist etwas schiefgelaufen." (22/600),
Subline, Link „Zur Übersicht". 503: „503" (Mono, 0.18em, #9A9A96) + „Vorübergehend nicht
verfügbar." — neutral, minimal. Toasts s. Komponenten. Native `confirm()` durch das
Modal-Pattern ersetzen.

## Interactions & Behavior

- Kern-Flow friktionslos: Login → „+ Neue Rechnung" (legt Entwurf an, direkt in Workbench)
  → Empfänger/Positionen tippen (Summen live) → Vorschau (autosave davor) → Abschließen
  (Bestätigung, Nummer wird gezogen) → Versenden (Modal).
- KPI-Kacheln und Filter-Pills navigieren in die gefilterte Belegliste.
- Hover: Zeilen #FAFAF8; Links/Aktionen #111; Buttons s. Komponenten. Fokus: Unterstrich
  des aktiven Felds wird #111 (kein Glow, kein Ring — bei Tastaturfokus zusätzlich
  `outline: 1px solid #111; outline-offset: 2px` für Accessibility).
- Übergänge sparsam: 120–160ms ease-out auf Farbe/Border; keine Bewegungsanimationen.
- Responsive (~390px): Header-Nav bleibt, Aside rutscht unter den Inhalt, KPI-Zeile wird
  2×2, Positionstabelle horizontal scrollbar, Beträge bleiben rechtsbündig.
  (Mobile-Mocks sind in diesem Bundle noch nicht enthalten.)

## State Management (serverseitig + vanilla JS)

- Beleg: `Entwurf → Abgeschlossen → Versendet → Gesehen`, `Storniert` als Endzustand;
  Entsperren: Abgeschlossen → Entwurf (Bestätigung).
- Workbench: dirty-Tracking für Autosave bei Tab-Wechsel auf Vorschau; Summen-Berechnung
  live im Client, verbindlich am Server.
- Versand-Modal: Idle → Sendet… → Erfolg (Toast) / Fehler (Toast mit `! `).
- Auth: Idle → WebAuthn-Dialog (browser-nativ) → Erfolg (Redirect) / Fehler (Statuszeile).

## Assets

- `assets/hand-black.png` (267×321, transparent) — Original-Bildmarke, unverändert aus
  der Kundenvorlage extrahiert. **Nicht neu zeichnen, nicht ersetzen.**
- `assets/hand-white.png` — Weiß-Variante für dunkle Flächen (App-Icon/Favicon-Basis).
- Fonts via Google Fonts: Hanken Grotesk, IBM Plex Mono, Newsreader (italic).
  Empfehlung: selbst hosten (woff2), keine CDN-Abhängigkeit im Produkt.

## Files

- `floq UI Design.dc.html` — alle Screens + Token-/Komponenten-Sheet (Hauptreferenz).
  Öffnen im Browser; benötigt `support.js` + `assets/` im selben Ordner.
- `floq Logo Explorationen.dc.html` — Logo-Runde 1; **gewählt: Richtung 1a** („Ruhig",
  Hanken Grotesk 600, Original-Hand). 1b–1d sind verworfen, nur Archiv.
- `support.js` — Runtime für die Design-Dateien (nur fürs Öffnen der Mocks, irrelevant
  für die Implementierung).
- `assets/hand-black.png`, `assets/hand-white.png`

## Offene Punkte (bewusst nicht Teil dieses Bundles)

- Mobile-Varianten (~390px) der App-Screens
- Mahnung-Tab „Rechnungen" als eigener Mock (Spezifikation oben in §4.5 vollständig)
- App-Icon/Favicon-Ableitung aus der Bildmarke
