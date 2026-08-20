# DCMtoGDTReports

[![Lizenz: MIT](https://img.shields.io/badge/Lizenz-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

Automatische Auswertung von **DICOM Structured Reports** des **GE Vivid T8** und Erzeugung von
**GDT-2.1-Rückgabedateien (Satzart 6310)** für den GDT-Autoimport von **MEDICAL OFFICE**.

Aus jeder `SR*.dcm` werden die Kopfdaten und alle numerischen Messwerte extrahiert und als
lesbarer Ergebnistext in die Felder `6220` geschrieben. MEDICAL OFFICE übernimmt die Datei
per Autoimport und legt sie als Krankenblatteintrag / Ergebnistext beim Patienten ab.

> **Hinweis:** Das Projekt ist auf den GE Vivid T8 zugeschnitten, arbeitet aber generisch auf
> DICOM SR (TID 5200). Andere Geräte und Modalitäten lassen sich über die Mapping- und
> Filterkonfiguration anbinden, ohne Code zu ändern.

## Datenschutz

Dieses Repository enthält **keine echten Patientendaten**. Alle Beispiel- und Testwerte
(Namen, Patientennummern, Geburtsdaten, UIDs, Gerätekennungen) sind frei erfunden.
DICOM-Dateien sind über `.gitignore` generell ausgeschlossen.

Wer eigene SR-Dateien zum Testen ablegt, muss sicherstellen, dass diese nicht versehentlich
eingecheckt werden — siehe [Tests](#tests).

---

## Inhalt

| Projekt | Zweck |
|---|---|
| `src/DCMtoGDTReports.Core` | Kernlogik: SR-Auswertung, Mapping, GDT-Erzeugung, Dubletten-Registry, Ordnerüberwachung |
| `src/DCMtoGDTReports.Tools` | DCMTK-Erkennung, Aufruf von `dsr2xml.exe` / `dcmdump.exe`, optionale DCMTK-Installation |
| `src/DCMtoGDTReports.App` | Moderne WPF-Oberfläche (Dashboard, Konfiguration, Log, Testfunktionen) |
| `src/DCMtoGDTReports.Worker` | Hintergrunddienst / Windows-Dienst mit dauerhafter Ordnerüberwachung |
| `src/DCMtoGDTReports.Cli` | Konsolenwerkzeug für Testläufe und Batch-Verarbeitung (`dcm2gdt.exe`) |
| `tests/DCMtoGDTReports.Tests` | Unit- und End-to-End-Tests, optional gegen eine lokale SR-Datei |

Zielframework: **.NET 8** (GUI: `net8.0-windows`).

> **Beispieldatei:** DICOM-Dateien sind per `.gitignore` ausgeschlossen und deshalb nicht
> Teil des Repositories. Für den End-to-End-Test legen Sie eine eigene SR-Datei unter
> `samples/` ab — ohne Datei wird dieser Test übersprungen, alle übrigen laufen normal.

---

## DICOM Toolkit — mitgeliefert

Das Tool bringt sein **eigenes DICOM-Toolkit** mit: [fo-dicom](https://github.com/fo-dicom/fo-dicom)
ist als NuGet-Paket fest eingebunden. **Es muss nichts installiert oder konfiguriert werden.**

DCMTK ist rein **optional** und kann als alternative Engine verwendet werden:

* Beim Start wird nach `dsr2xml.exe` und `dcmdump.exe` gesucht — in dieser Reihenfolge:
  1. konfigurierter `DcmtkPath`
  2. `<Anwendungsordner>\tools\dcmtk\bin` (lokal mitgeliefert/installiert)
  3. Verzeichnisse aus der `PATH`-Variable
  4. `C:\Program Files\DCMTK\bin`, `C:\dcmtk\bin`, `C:\tools\dcmtk\bin`
* Wird DCMTK nicht gefunden, erscheint eine Klartextmeldung mit allen geprüften Pfaden.
* Über die Schaltfläche **„DCMTK herunterladen / einrichten"** kann DCMTK nach ausdrücklicher
  Bestätigung von `dicom.offis.de` geladen und nach `tools\dcmtk` entpackt werden.
  **Ohne Benutzeraktion findet niemals ein Download statt.**
* Umschalten der Engine über `Processing.PreferredEngine`: `"Builtin"` oder `"Dcmtk"`.

---

## Installation

### Fertiges Paket herunterladen

Das aktuelle Release enthält alles Nötige — **self-contained für Windows x64, es muss keine
.NET-Runtime installiert werden**:

**[Download: DCMtoGDTReports-1.1.0-win-x64.zip](https://github.com/GitMaster911/dcmtogdt/releases/latest)**

```powershell
# oder direkt auf dem Server
Invoke-WebRequest -Uri "https://github.com/GitMaster911/dcmtogdt/releases/download/v1.1.0/DCMtoGDTReports-1.1.0-win-x64.zip" `
    -OutFile "$env:TEMP\DCMtoGDT.zip"
Expand-Archive "$env:TEMP\DCMtoGDT.zip" -DestinationPath C:\Temp\DCMtoGDT
```

Danach weiter bei [Installieren](#installieren).

### Voraussetzungen

* Windows Server 2019+ oder Windows 10/11
* Nur beim Selbstbauen: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
  (das Release-Paket bringt die Runtime bereits mit)

### Bauen

```powershell
cd DCMtoGDTReports
dotnet build DCMtoGDTReports.slnx -c Release
dotnet test  tests\DCMtoGDTReports.Tests\DCMtoGDTReports.Tests.csproj
```

### Veröffentlichen (self-contained, ohne Runtime-Installation)

```powershell
dotnet publish src\DCMtoGDTReports.App    -c Release -r win-x64 --self-contained true -o publish\gui
dotnet publish src\DCMtoGDTReports.Worker -c Release -r win-x64 --self-contained true -o publish\gui
dotnet publish src\DCMtoGDTReports.Cli    -c Release -r win-x64 --self-contained true -o publish\cli
```

GUI und Dienst werden bewusst in **denselben** Ordner veröffentlicht — so installiert
`install.ps1` beides in einem Durchgang und das Selbstupdate tauscht alles gemeinsam aus.

### Installieren

Die Installation erfolgt über `install.ps1` (**PowerShell als Administrator**).
Ohne Parameter wird der Installationspfad interaktiv abgefragt, Vorgabe ist
**`C:\BITS\DCMtoGDT`**:

```powershell
.\install.ps1
```

```
Installationspfad [C:\BITS\DCMtoGDT]: _
```

Weitere Varianten:

```powershell
# Anderer Zielpfad, ohne Rückfragen, inklusive Windows-Dienst
.\install.ps1 -InstallPath "D:\Programme\DCMtoGDT" -InstallService -Silent

# Vor der Installation selbst bauen (benötigt das .NET SDK)
.\install.ps1 -Build

# Mit zentraler Updatequelle ausrollen und Selbstupdate ohne Adminrechte erlauben
.\install.ps1 -Silent -AllowUserUpdates `
    -UpdateSource "\\server\software\DCMtoGDTReports\update.json"

# Nur prüfen, ob Paket und Zielpfad passen (ohne Adminrechte, ändert nichts)
.\install.ps1 -CheckOnly

# Entfernen (Konfiguration und Verarbeitungsverlauf bleiben erhalten)
.\install.ps1 -Uninstall -Silent
```

| Parameter | Bedeutung |
|---|---|
| `-InstallPath` | Zielverzeichnis, Vorgabe `C:\BITS\DCMtoGDT` |
| `-SourcePath` | Publish-Ordner; ohne Angabe wird neben dem Skript gesucht |
| `-Build` | Baut die Anwendung vorher selbst |
| `-InstallService` | Registriert und startet den Windows-Dienst |
| `-ServiceName` | Dienstname, Vorgabe `DCMtoGDTReports` |
| `-UpdateSource` | Trägt die zentrale `update.json` in die Konfiguration ein |
| `-AllowUserUpdates` | Schreibrechte für Selbstupdates ohne Adminrechte |
| `-NoShortcut` | Keine Startmenü-Verknüpfung anlegen |
| `-Silent` | Keine Rückfragen |
| `-CheckOnly` | Nur prüfen, nichts verändern |
| `-Uninstall` | Deinstallation |

Der Installer beendet eine laufende Instanz, hält den Dienst für die Dauer der
Aktualisierung an und legt eine vorhandene `settings.json` **nicht** neu an.

Nicht zulässig als Zielpfad sind Laufwerksstammverzeichnisse sowie `%SystemRoot%`,
`%ProgramData%` und das Benutzerprofil.

---

## Konfiguration

Die Konfiguration liegt standardmäßig unter:

```
C:\ProgramData\brans IT solutions\DCMtoGDTReports\settings.json
```

Damit greifen GUI und Dienst auf dieselbe Datei zu. Eine vollständige Vorlage liegt als
[appsettings.example.json](appsettings.example.json) bei. **Im Code gibt es keine fest verdrahteten
Pfade** — alle Ordner sind konfigurierbar.

Wichtige Einstellungen:

| Schlüssel | Bedeutung |
|---|---|
| `InputFolder` | Eingangsordner, in den der Vivid T8 die `SR*.dcm` ablegt |
| `OutputFolder` | GDT-Ausgabeordner — muss dem Autoimport-Ordner von MEDICAL OFFICE entsprechen |
| `ArchiveFolder` | Archiv für verarbeitete DICOM-Dateien und Struktur-XML |
| `ErrorFolder` | Ablage für nicht verarbeitbare Dateien inkl. `*.error.txt` |
| `Gdt.SenderId` / `Gdt.ReceiverId` | Felder 8316 / 8315 |
| `Gdt.TestType` / `Gdt.TestId` | Felder 8402 / 8410 |
| `Gdt.EncodingCodePage` | `28591` = ISO-8859-1, `1252` = Windows-1252 |
| `Gdt.MaxResultLineLength` | Umbruchbreite der Ergebniszeilen (Feld 6220) |
| `Processing.MoveProcessedFiles` | `false` = Originaldatei bleibt am Ursprungsort |
| `Processing.PreventDuplicateBy` | `SHA256_OR_SOPInstanceUID` |
| `MeasurementShortNames` | Eigene Kurzbezeichnungen, Schlüssel = Code Meaning **oder** Concept-Code (`LN:29436-3`) |
| `MeasurementFilter` | Auswahl der Messwerte, siehe unten |
| `Update` | Zentrale Aktualisierung, siehe unten |

---

## Messwertfilter

Der Vivid T8 liefert pro Untersuchung sehr viele Werte — in der Referenzdatei bleiben nach der
Dublettenbereinigung noch **200 Messwerte** übrig (u. a. 18 Strain-Segmente und je Herzschlag
ein eigener EF-Wert). Für einen Krankenblatteintrag ist das meist zu viel.

Der Filter ist **standardmäßig deaktiviert** — ohne ausdrückliche Konfiguration geht also
kein klinischer Wert verloren. Einstellbar in der GUI („Messwertfilter") oder in `settings.json`:

| Schlüssel | Wirkung |
|---|---|
| `Enabled` | Filter aktivieren |
| `OnlyMappedMeasurements` | Nur Werte, für die eine Kurzbezeichnung hinterlegt ist |
| `OnlySelectedValues` | Nur Werte, die das Gerät per `Selection Status` als gewählt markiert |
| `IncludeConcepts` / `ExcludeConcepts` | Muster für Concept-Code, Originalname oder Kurzname |
| `IncludeFindingSites` / `ExcludeFindingSites` | Muster für die Region |
| `IncludeImageModes` / `ExcludeImageModes` | Muster für den Aufnahmemodus |
| `RepeatedValues` | `All`, `First`, `Last`, `Mean`, `Min`, `Max` |
| `MaxMeasurements` | Obergrenze, `0` = unbegrenzt |

**Regeln:** Muster erlauben `*` und `?` und sind nicht case-sensitiv. Ausschlusslisten haben
Vorrang vor Einschlusslisten. Eine leere Einschlussliste bedeutet „alles zulassen".

`RepeatedValues` fasst Wiederholungsmessungen derselben Messgröße zusammen — gruppiert nach
Messgröße, Region, Methode, Aufnahmemodus, Herzzyklus und Flussrichtung. Bei `Mean`, `Min` und
`Max` wird die Anzahl der Einzelmessungen im Ergebnistext vermerkt, damit nachvollziehbar bleibt,
woraus der Wert entstanden ist:

```
EF (2D Auto EF, Mittel aus 6): 58,53 %
```

### Beispiel: kompakter Befund

```json
"MeasurementFilter": {
  "Enabled": true,
  "OnlyMappedMeasurements": true,
  "ExcludeFindingSites": [ "*segment" ],
  "RepeatedValues": "Mean"
}
```

Damit bleiben aus der Referenzdatei rund 30 aussagekräftige Zeilen übrig statt 200.

---

## Aktualisierung mehrerer Arbeitsplätze

Damit alle Arbeitsplätze denselben Stand haben, wird das Update zentral bereitgestellt:
eine `update.json` plus ZIP-Paket auf einer **Netzwerkfreigabe (UNC)** oder unter **HTTPS**.

### Paket erzeugen

```powershell
dotnet publish src\DCMtoGDTReports.App -c Release -r win-x64 --self-contained true -o publish\gui

.\publish\cli\dcm2gdt.exe pack `
    --input publish\gui `
    --out   \\server\software\DCMtoGDTReports `
    --version 1.2.0 `
    --notes "Messwertfilter ergaenzt"
```

`pack` erzeugt `DCMtoGDTReports-1.2.0.zip`, berechnet die SHA256 und schreibt die passende
`update.json`:

```json
{
  "Version": "1.2.0",
  "PackageUrl": "DCMtoGDTReports-1.2.0.zip",
  "Sha256": "A1B2...",
  "Notes": "Messwertfilter ergaenzt",
  "PublishedAt": "2026-08-19T10:00:00+02:00",
  "Mandatory": false
}
```

`PackageUrl` darf relativ bleiben — der Pfad wird auf den Ort der `update.json` bezogen.

### Arbeitsplätze konfigurieren

```json
"Update": {
  "Enabled": true,
  "ManifestUrl": "\\\\server\\software\\DCMtoGDTReports\\update.json",
  "CheckOnStartup": true,
  "CheckIntervalHours": 24,
  "InstallAutomatically": false,
  "ServiceName": "DCMtoGDTReports"
}
```

### Ablauf einer Installation

1. Beim Start (und im Intervall) wird die Version im Manifest mit der installierten verglichen.
2. Die GUI zeigt „Version x.y.z steht bereit" und die Änderungshinweise an.
3. Nach Bestätigung wird das Paket geladen und die **SHA256 geprüft** — ohne gültige Prüfsumme
   wird abgebrochen. Anschließend wird in ein Staging-Verzeichnis entpackt (Zip-Slip-Schutz).
4. Ein ASCII-Batch-Skript übernimmt den Austausch, weil eine laufende `.exe` sich nicht selbst
   überschreiben kann: Dienst stoppen → auf Programmende warten → `robocopy` → Dienst starten →
   Anwendung neu starten → Skript löscht sich selbst.
5. Protokoll: `%TEMP%\dcm2gdt-update.log`.

**Wichtig für den Betrieb:**

* Das Benutzerkonto braucht **Schreibrechte auf das Installationsverzeichnis**. Unter
  `C:\Program Files` ist das nicht gegeben — deshalb ist `C:\BITS\DCMtoGDT` der Standardpfad.
  Für Selbstupdates ohne Adminrechte einmalig mit `install.ps1 -AllowUserUpdates` installieren
  oder Updates über die Softwareverteilung ausrollen.
* `settings.json` liegt in `ProgramData` und wird vom Update **nicht** überschrieben.
* `ServiceName` nur setzen, wenn der Windows-Dienst auf demselben Rechner läuft.
* `InstallAutomatically: true` installiert ohne Rückfrage — sinnvoll für den Dienst,
  bei Arbeitsplätzen mit laufender Untersuchung eher nicht.

Prüfung per Kommandozeile (Exitcode `10` = Update verfügbar, `0` = aktuell, `1` = Fehler):

```powershell
dcm2gdt update
```

---

## Verarbeitungsablauf

1. `FileSystemWatcher` meldet eine neue Datei im Eingangsordner (zusätzlich zyklischer Rescan).
2. Es wird gewartet, bis die Datei vollständig geschrieben ist (Größe + Zeitstempel stabil,
   Datei exklusiv öffenbar).
3. SHA256 wird gebildet und gegen die Registry geprüft → Dublette wird übersprungen.
4. **Die Originaldatei wird nicht verändert.** Es wird eine temporäre Kopie erstellt,
   ausgewertet und anschließend wieder gelöscht.
5. Auswertung des Structured Reports (rekursiv über die Content Sequence).
6. Zweite Dublettenprüfung über die `SOPInstanceUID`.
7. Mapping der Messwertnamen auf Kurzbezeichnungen und Formatierung der Zahlenwerte,
   anschließend der konfigurierte Messwertfilter.
8. GDT-Datei wird **atomar** geschrieben: erst `.tmp`, danach Umbenennung auf `.gdt`.
9. Optional: Kopie/Verschieben der Originaldatei ins Archiv, Struktur-XML zur Fehlersuche.
10. Ergebnis wird in der SQLite-Registry protokolliert.

### Statuswerte in der GUI

| Status | Bedeutung |
|---|---|
| Neu verarbeitet | GDT-Datei wurde erzeugt |
| Bereits verarbeitet | SHA256 oder SOPInstanceUID war bereits bekannt |
| Übersprungen | Datei war lesbar, enthielt aber keine numerischen Messwerte (oder der Filter hat alle entfernt) |
| Fehler | Verarbeitung fehlgeschlagen, Datei liegt im Fehlerordner |

---

## SR-Struktur des GE Vivid T8

Die Auswertung wurde anhand einer echten Gerätedatei entwickelt
(`samples/SR*.dcm`, SOP Class `1.2.840.10008.5.1.4.1.1.88.33`):

```
CONTAINER  Adult Echocardiography Procedure Report (DCM:125200)   TID 5200
 └─ CONTAINER  Findings (DCM:121070)                              TID 5202
     ├─ CODE  [HAS CONCEPT MOD]  Finding Site (SRT:G-C0E3) = Left Ventricle
     └─ CONTAINER  Measurement Group (DCM:125007)
         ├─ CODE  [HAS CONCEPT MOD]  Image Mode (SRT:G-0373) = 2D mode
         └─ NUM   Left Ventricle Internal End Diastolic Dimension (LN:29436-3)
                  = 4.2141821355915 [cm]   (Rohwert, wird auf 4.21 gerundet)
             ├─ CODE  [HAS CONCEPT MOD]  Measurement Method (SRT:G-C036)
             ├─ CODE  [HAS CONCEPT MOD]  Cardiac Cycle Point (SRT:R-4089A)
             ├─ CODE  [HAS CONCEPT MOD]  Direction of Flow (SRT:G-C048)
             ├─ CODE  [HAS CONCEPT MOD]  Derivation (DCM:121401)
             └─ CODE  [HAS PROPERTIES]   Selection Status (DCM:121404)
```

Wichtige Erkenntnisse aus der Referenzdatei:

* **Finding Site** und **Image Mode** werden vom übergeordneten Container an die Messwerte
  vererbt; ein Modifikator direkt am `NUM`-Knoten überschreibt den geerbten Wert.
* Das Gerät schreibt **jeden Messwert doppelt** — einmal mit `Selection Status`
  („Mean value chosen" / „Most recent value chosen") und einmal ohne. Der
  `MeasurementDeduplicator` entfernt die Dubletten und behält den Eintrag mit Selection Status.
  In der Referenzdatei reduziert das 270 `NUM`-Knoten auf 200 Messwerte.
* Als Einheit wird das kurze UCUM-Kürzel verwendet (`cm` statt `centimeter`), damit die
  GDT-Zeilen kurz bleiben.
* `StudyDescription` enthält beim Vivid T8 gelegentlich nur den Platzhalter `*` — dieser
  wird verworfen.

### Mapping der Messwertnamen

Für die gängigen Größen ist ein Standard-Mapping hinterlegt
(`MeasurementMapper.DefaultShortNames`), z. B.:

```
Left Ventricle Internal End Diastolic Dimension   -> LVIDd
Interventricular Septum Diastolic Thickness       -> IVSd
Left Ventricle Posterior Wall Diastolic Thickness -> LVPWd
Right Ventricular Internal Diastolic Dimension    -> RVIDd
Left Ventricular End Diastolic Volume             -> EDV
Left Ventricular Ejection Fraction                -> EF
Tricuspid Annular Plane Systolic Excursion        -> TAPSE
```

Eigene Einträge in `MeasurementShortNames` überschreiben das Standard-Mapping.
**Existiert kein Mapping, wird der Originalname aus dem SR verwendet.**

---

## GDT-Format

Jede Zeile hat den Aufbau:

```
<dreistellige Länge><vierstellige Feldkennung><Inhalt><CRLF>
```

Die Länge ist die **Inhaltslänge in Bytes der Zielkodierung + 9**
(3 Zeichen Länge + 4 Zeichen Feldkennung + 2 Zeichen CRLF).

Beispiel für Feld `8000` mit Inhalt `6310`:

```
01380006310
```

Verwendete Felder der Satzart 6310:

| Feld | Inhalt |
|---|---|
| 8000 | `6310` — Satzart |
| 8100 | Satzlänge in Bytes (inkl. dieser Zeile) |
| 8315 / 8316 | Empfänger- / Senderkennung |
| 9206 | Zeichensatz (`3` = ISO-8859-1 / ANSI) |
| 9218 | GDT-Version (`02.10`) |
| 3000 | Patientennummer aus `PatientID` |
| 3101 / 3102 | Nach- / Vorname (nur bei eindeutiger Zerlegung des DICOM-PN) |
| 3103 | Geburtsdatum `DDMMYYYY` |
| 3110 | Geschlecht (`1` = männlich, `2` = weiblich; sonst weggelassen) |
| 6200 / 6201 | Untersuchungsdatum `DDMMYYYY` / -zeit `HHMMSS` |
| 8402 | Geräte-/Verfahrenskennung |
| 8410 / 8411 | Test-Ident / Testbezeichnung |
| 6220 | Ergebniszeilen (Messwerte, automatisch an Wortgrenzen umgebrochen) |
| 6227 | Kommentarzeilen (Quelle, SOPInstanceUID, StudyInstanceUID) |

Optional (`Gdt.EmitStructuredTestValues = true`) werden zusätzlich je Messwert die Felder
`8410` / `8411` / `8420` / `8421` als diskrete Messgrößen ausgegeben.

Beispielausgabe: [samples/example-output.gdt](samples/example-output.gdt)

---

## Kommandozeile

```powershell
dcm2gdt analyze --file samples\SR*.dcm          # Messwerte anzeigen
dcm2gdt gdt     --file samples\SR*.dcm --out out # GDT-Testdatei erzeugen
dcm2gdt process                                  # Eingangsordner einmalig abarbeiten
dcm2gdt watch                                    # Ordnerüberwachung starten
dcm2gdt dcmtk                                    # DCMTK-Status prüfen
dcm2gdt update                                   # auf neue Programmversion prüfen
dcm2gdt pack --input publish\gui --out \\server\software\DCMtoGDTReports --version 1.2.0
dcm2gdt config                                   # Konfiguration anzeigen
```

Zusatzoptionen: `--settings <pfad>`, `--input <ordner>`, `--xml <pfad>`.

---

## Einrichtung auf dem Server

1. **Ordner anlegen und berechtigen**

   ```powershell
   New-Item -ItemType Directory C:\MEDOFF\DICOM\in, C:\MEDOFF\DICOM\archiv, C:\MEDOFF\DICOM\fehler
   ```

   Das Dienstkonto braucht Lese-/Schreibrechte auf Eingangs-, Archiv- und Fehlerordner sowie
   Schreibrechte auf den GDT-Autoimport-Ordner von MEDICAL OFFICE.

2. **Installieren** (PowerShell als Administrator):

   ```powershell
   .\install.ps1 -InstallPath "C:\BITS\DCMtoGDT" -InstallService -Silent `
       -UpdateSource "\\server\software\DCMtoGDTReports\update.json"
   ```

3. **Konfiguration prüfen** — `appsettings.example.json` als Vorlage nach
   `C:\ProgramData\brans IT solutions\DCMtoGDTReports\settings.json` kopieren und anpassen,
   oder einmalig die GUI starten und die Pfade dort setzen.

4. **Vorab testen** (bevor der Dienst produktiv läuft):

   ```powershell
   .\publish\cli\dcm2gdt.exe gdt --file C:\MEDOFF\DICOM\in\SR....dcm --out C:\Temp
   ```

5. **Dienstkonto anpassen:** Läuft der Eingangsordner auf einer Netzwerkfreigabe, muss der
   Dienst unter einem Domänenkonto mit Zugriff auf die Freigabe laufen (nicht `LocalSystem`).

   Der Dienst wird von `install.ps1 -InstallService` bereits angelegt, auf Autostart gesetzt
   und mit Neustart-Verhalten bei Fehlern versehen. Manuell entspricht das:

   ```powershell
   sc.exe create DCMtoGDTReports `
       binPath= "C:\BITS\DCMtoGDT\DCMtoGDTReports.Worker.exe" `
       DisplayName= "DCMtoGDTReports (DICOM SR nach GDT)" `
       start= auto
   sc.exe description DCMtoGDTReports "Wandelt DICOM Structured Reports des GE Vivid T8 in GDT-Dateien fuer MEDICAL OFFICE um."
   sc.exe failure DCMtoGDTReports reset= 86400 actions= restart/60000/restart/60000/restart/60000
   Start-Service DCMtoGDTReports
   ```

6. **GDT-Autoimport in MEDICAL OFFICE** auf den `OutputFolder` konfigurieren,
   Empfängerkennung `MEDOFF` und Senderkennung `VIVIDT8` (bzw. die konfigurierten Werte)
   hinterlegen.

7. **Prüfen:** Logdateien unter `LogFolder` (`worker-JJJJMMTT.log`, 31 Tage Aufbewahrung)
   und die Liste in der GUI.

---

## Logging und Datenschutz

* Serilog schreibt tagesrollierende Logdateien; die GUI zeigt das Log zusätzlich live an.
* Es werden **keine Patientenstammdaten ins Log geschrieben** — nur Dateinamen, Status,
  Anzahl Messwerte und technische Kennungen.
* Patientendaten stehen ausschließlich in der GDT-Datei, im Archiv und in der Registry
  (dort nur `PatientID` und UIDs). Diese Ordner sind entsprechend zu berechtigen.

---

## Tests

```powershell
dotnet test tests\DCMtoGDTReports.Tests\DCMtoGDTReports.Tests.csproj
```

Abgedeckt sind unter anderem:

* GDT-Feldlängenberechnung (inkl. Byte-Länge in der Zielkodierung) und Satzlänge 8100
* vollständige GDT-Dateierzeugung und Zeilenumbruch
* Parsing von DICOM-Personennamen (`Family^Given^Middle^Prefix^Suffix`, `=`-Komponenten)
* Datums-/Zeit-/Geschlechtskonvertierung DICOM → GDT
* Dublettenprüfung über SHA256 und SOPInstanceUID
* Parsing einer `dsr2xml`-XML-Struktur inkl. Dublettenbereinigung
* Messwertfilter: Muster mit Platzhaltern, Vorrang der Ausschlusslisten, Zusammenfassung
  von Wiederholungsmessungen
* Updateprüfung: Versionsvergleich, Auflösung relativer Paketpfade, Ablehnung fehlender
  oder falscher SHA256-Prüfsummen
* End-to-End-Lauf mit einer lokalen `SR*.dcm` bis zur validierten GDT-Datei (wird ohne
  Datei übersprungen)

---

## Lizenz

[MIT](LICENSE) — © brans IT solutions
