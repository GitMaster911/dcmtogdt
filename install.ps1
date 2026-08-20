<#
.SYNOPSIS
    Installiert DCMtoGDTReports (DICOM SR nach GDT fuer MEDICAL OFFICE).

.DESCRIPTION
    Kopiert die Programmdateien in das gewaehlte Installationsverzeichnis, legt bei Bedarf
    die zentrale Konfiguration in ProgramData an, registriert optional den Windows-Dienst
    und erstellt Verknuepfungen.

    Standard-Installationspfad: C:\BITS\DCMtoGDT

.PARAMETER InstallPath
    Zielverzeichnis der Installation. Ohne Angabe wird interaktiv danach gefragt.

.PARAMETER SourcePath
    Ordner mit den veroeffentlichten Programmdateien (Ergebnis von "dotnet publish").
    Ohne Angabe wird neben dem Skript gesucht.

.PARAMETER Build
    Baut die Anwendung vor der Installation selbst (benoetigt das .NET SDK).

.PARAMETER InstallService
    Registriert zusaetzlich den Windows-Dienst fuer die Ordnerueberwachung.

.PARAMETER AllowUserUpdates
    Vergibt Schreibrechte auf das Installationsverzeichnis, damit angemeldete Benutzer
    die Selbstaktualisierung ohne Administratorrechte ausfuehren koennen.

.PARAMETER Uninstall
    Entfernt Dienst, Verknuepfungen und Programmdateien.

.PARAMETER CheckOnly
    Prueft nur, ob Paket und Zielpfad in Ordnung sind. Veraendert nichts und benoetigt
    keine Administratorrechte.

.EXAMPLE
    .\install.ps1
    Fragt den Installationspfad ab (Vorgabe C:\BITS\DCMtoGDT) und installiert die GUI.

.EXAMPLE
    .\install.ps1 -InstallPath "D:\Programme\DCMtoGDT" -InstallService -Silent

.EXAMPLE
    .\install.ps1 -Uninstall -InstallPath "C:\BITS\DCMtoGDT" -Silent
#>
[CmdletBinding()]
param(
    [string]$InstallPath,
    [string]$SourcePath,
    [switch]$Build,
    [switch]$InstallService,
    [string]$ServiceName = 'DCMtoGDTReports',
    [string]$UpdateSource,
    [switch]$AllowUserUpdates,
    [switch]$NoShortcut,
    [switch]$Silent,
    [switch]$Uninstall,
    [switch]$CheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:DefaultInstallPath = 'C:\BITS\DCMtoGDT'
$script:SettingsFolder = Join-Path $env:ProgramData 'brans IT solutions\DCMtoGDTReports'
$script:GuiExecutable = 'DCMtoGDTReports.exe'
$script:ServiceExecutable = 'DCMtoGDTReports.Worker.exe'
$script:ShortcutName = 'DCMtoGDTReports.lnk'

function Write-Step   { param([string]$Text) Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-Ok     { param([string]$Text) Write-Host "    $Text" -ForegroundColor Green }
function Write-Info   { param([string]$Text) Write-Host "    $Text" -ForegroundColor Gray }
function Write-Warn   { param([string]$Text) Write-Host "    $Text" -ForegroundColor Yellow }

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal $identity).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-InstallPath {
    param([string]$Provided)

    if ($Provided) { return $Provided.TrimEnd('\') }
    if ($Silent)   { return $script:DefaultInstallPath }

    Write-Host ''
    $answer = Read-Host "Installationspfad [$script:DefaultInstallPath]"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $script:DefaultInstallPath }
    return $answer.Trim().Trim('"').TrimEnd('\')
}

function Assert-SafeInstallPath {
    param([string]$Path)

    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        throw "Der Installationspfad muss absolut sein: '$Path'"
    }

    $full = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($full).TrimEnd('\')

    # Schutz davor, versehentlich ein Laufwerk oder einen Systemordner zu ueberschreiben.
    if ($full.TrimEnd('\') -eq $root) {
        throw "Ein Laufwerksstammverzeichnis ist als Installationspfad nicht zulaessig: '$full'"
    }
    foreach ($forbidden in @($env:SystemRoot, $env:ProgramData, $env:USERPROFILE)) {
        if ($forbidden -and $full.TrimEnd('\') -ieq $forbidden.TrimEnd('\')) {
            throw "Dieser Ordner darf nicht als Installationspfad verwendet werden: '$full'"
        }
    }

    return $full
}

function Resolve-SourcePath {
    param([string]$Provided)

    if ($Provided) {
        if (-not (Test-Path $Provided)) { throw "Quellordner nicht gefunden: '$Provided'" }
        return (Resolve-Path $Provided).Path
    }

    # Uebliche Ablageorte: neben dem Skript, im Unterordner publish, oder das Skript liegt
    # bereits im fertigen Paket.
    $candidates = @(
        (Join-Path $PSScriptRoot 'publish\gui'),
        (Join-Path $PSScriptRoot 'publish'),
        $PSScriptRoot
    )

    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path $candidate $script:GuiExecutable)) {
            return (Resolve-Path $candidate).Path
        }
    }

    return $null
}

function Invoke-Publish {
    Write-Step 'Anwendung wird gebaut'

    $projectFile = Join-Path $PSScriptRoot 'src\DCMtoGDTReports.App\DCMtoGDTReports.App.csproj'
    if (-not (Test-Path $projectFile)) {
        throw "Projektdatei nicht gefunden: '$projectFile'. Bitte -SourcePath angeben."
    }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Das .NET SDK wurde nicht gefunden. Bitte -SourcePath mit fertigen Dateien angeben.'
    }

    $target = Join-Path $PSScriptRoot 'publish\gui'
    & dotnet publish $projectFile -c Release -r win-x64 --self-contained true -o $target | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish ist fehlgeschlagen (ExitCode $LASTEXITCODE)." }

    Write-Ok "Build abgelegt unter $target"
    return $target
}

function Copy-Program {
    param([string]$Source, [string]$Destination)

    Write-Step "Dateien werden nach $Destination kopiert"
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    # /XF install.ps1 verhindert, dass sich das Skript aus dem Paket selbst mitinstalliert.
    & robocopy $Source $Destination /E /R:3 /W:2 /NFL /NDL /NJH /NJS /XF 'install.ps1' | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Das Kopieren ist fehlgeschlagen (robocopy ExitCode $LASTEXITCODE)." }

    Write-Ok 'Programmdateien kopiert'
}

function Grant-UserWriteAccess {
    param([string]$Path)

    Write-Step 'Schreibrechte fuer die Selbstaktualisierung werden gesetzt'
    Write-Warn 'Hinweis: Angemeldete Benutzer koennen damit Programmdateien veraendern.'

    $account = New-Object Security.Principal.SecurityIdentifier(
        [Security.Principal.WellKnownSidType]::AuthenticatedUserSid, $null)
    $rule = New-Object Security.AccessControl.FileSystemAccessRule(
        $account, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')

    $acl = Get-Acl $Path
    $acl.AddAccessRule($rule)
    Set-Acl -Path $Path -AclObject $acl

    Write-Ok 'Berechtigungen gesetzt'
}

function Initialize-Settings {
    param([string]$UpdateManifestUrl, [string]$ServiceNameForUpdate)

    $settingsFile = Join-Path $script:SettingsFolder 'settings.json'
    New-Item -ItemType Directory -Path $script:SettingsFolder -Force | Out-Null

    if (Test-Path $settingsFile) {
        Write-Info "Vorhandene Konfiguration bleibt unveraendert: $settingsFile"

        # Nur die Updatequelle nachtragen, wenn sie ausdruecklich uebergeben wurde.
        if ($UpdateManifestUrl) {
            $existing = Get-Content $settingsFile -Raw | ConvertFrom-Json
            $existing.Update.Enabled = $true
            $existing.Update.ManifestUrl = $UpdateManifestUrl
            if ($ServiceNameForUpdate) { $existing.Update.ServiceName = $ServiceNameForUpdate }
            $existing | ConvertTo-Json -Depth 10 | Set-Content -Path $settingsFile -Encoding UTF8
            Write-Ok 'Updatequelle in der vorhandenen Konfiguration aktualisiert'
        }
        return $settingsFile
    }

    $template = Join-Path $PSScriptRoot 'appsettings.example.json'
    if (Test-Path $template) {
        $settings = Get-Content $template -Raw | ConvertFrom-Json
    }
    else {
        # Ohne Vorlage genuegt ein Grundgeruest - die Anwendung ergaenzt fehlende Werte selbst.
        $settings = [pscustomobject]@{
            InputFolder  = ''
            OutputFolder = ''
            Update       = [pscustomobject]@{ Enabled = $false; ManifestUrl = ''; ServiceName = '' }
        }
    }

    if ($UpdateManifestUrl) {
        $settings.Update.Enabled = $true
        $settings.Update.ManifestUrl = $UpdateManifestUrl
        if ($ServiceNameForUpdate) { $settings.Update.ServiceName = $ServiceNameForUpdate }
    }

    $settings | ConvertTo-Json -Depth 10 | Set-Content -Path $settingsFile -Encoding UTF8
    Write-Ok "Konfiguration angelegt: $settingsFile"
    Write-Info 'Bitte Eingangs- und Ausgabeordner in der Anwendung setzen.'

    return $settingsFile
}

function New-StartMenuShortcut {
    param([string]$TargetExecutable)

    $folder = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\brans IT solutions'
    New-Item -ItemType Directory -Path $folder -Force | Out-Null

    $shortcutPath = Join-Path $folder $script:ShortcutName
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $TargetExecutable
    $shortcut.WorkingDirectory = Split-Path $TargetExecutable -Parent
    $shortcut.Description = 'DICOM Structured Reports nach GDT fuer MEDICAL OFFICE'
    $shortcut.Save()

    Write-Ok "Startmenue-Verknuepfung erstellt: $shortcutPath"
    return $shortcutPath
}

function Install-WindowsService {
    param([string]$Path, [string]$Name)

    $executable = Join-Path $Path $script:ServiceExecutable
    if (-not (Test-Path $executable)) {
        Write-Warn "Dienstprogramm nicht gefunden ($script:ServiceExecutable) - der Dienst wird uebersprungen."
        Write-Warn 'Bitte zusaetzlich DCMtoGDTReports.Worker veroeffentlichen.'
        return
    }

    Write-Step "Windows-Dienst '$Name' wird eingerichtet"

    $existing = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($existing) {
        if ($existing.Status -ne 'Stopped') { Stop-Service -Name $Name -Force }
        & sc.exe config $Name binPath= "`"$executable`"" start= auto | Out-Null
        Write-Ok 'Vorhandener Dienst wurde aktualisiert'
    }
    else {
        & sc.exe create $Name binPath= "`"$executable`"" DisplayName= 'DCMtoGDTReports (DICOM SR nach GDT)' start= auto | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Der Dienst konnte nicht angelegt werden (ExitCode $LASTEXITCODE)." }

        & sc.exe description $Name 'Wandelt DICOM Structured Reports in GDT-Dateien fuer MEDICAL OFFICE um.' | Out-Null
        & sc.exe failure $Name reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
        Write-Ok 'Dienst angelegt'
    }

    Start-Service -Name $Name
    Write-Ok 'Dienst gestartet'
    Write-Info 'Liegt der Eingangsordner auf einer Freigabe, den Dienst auf ein Domaenenkonto umstellen.'
}

function Remove-Installation {
    param([string]$Path, [string]$Name)

    Write-Step 'Deinstallation'

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($service) {
        if ($service.Status -ne 'Stopped') { Stop-Service -Name $Name -Force }
        & sc.exe delete $Name | Out-Null
        Write-Ok "Dienst '$Name' entfernt"
    }

    $shortcut = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\brans IT solutions\$script:ShortcutName"
    if (Test-Path $shortcut) {
        Remove-Item $shortcut -Force
        Write-Ok 'Verknuepfung entfernt'
    }

    if (Test-Path $Path) {
        Get-Process -Name 'DCMtoGDTReports' -ErrorAction SilentlyContinue | Stop-Process -Force
        Remove-Item $Path -Recurse -Force
        Write-Ok "Programmdateien entfernt: $Path"
    }
    else {
        Write-Info "Kein Installationsverzeichnis unter '$Path' gefunden."
    }

    Write-Info "Konfiguration und Verarbeitungsverlauf bleiben erhalten: $script:SettingsFolder"
}

# ---------------------------------------------------------------------------

Write-Host ''
Write-Host 'DCMtoGDTReports - Installation' -ForegroundColor White
Write-Host '------------------------------' -ForegroundColor DarkGray

if ($CheckOnly) {
    $checkTarget = Assert-SafeInstallPath $(if ($InstallPath) { $InstallPath } else { $script:DefaultInstallPath })
    $checkSource = Resolve-SourcePath $SourcePath

    Write-Step 'Pruefung'
    Write-Info "Zielpfad        : $checkTarget"
    Write-Info "Bereits vorhanden: $(Test-Path $checkTarget)"
    Write-Info "Administrator   : $(Test-Administrator)"

    if ($checkSource) {
        $hasGui = Test-Path (Join-Path $checkSource $script:GuiExecutable)
        $hasService = Test-Path (Join-Path $checkSource $script:ServiceExecutable)
        Write-Info "Quelle          : $checkSource"
        Write-Info "GUI enthalten   : $hasGui"
        Write-Info "Dienst enthalten: $hasService"
        Write-Ok 'Paket gefunden'
    }
    else {
        Write-Warn 'Keine Programmdateien gefunden - bitte -SourcePath angeben oder -Build verwenden.'
    }

    $checkService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    Write-Info "Dienst '$ServiceName': $(if ($checkService) { $checkService.Status } else { 'nicht installiert' })"
    Write-Info "Konfiguration   : $(Join-Path $script:SettingsFolder 'settings.json')"
    Write-Host ''
    exit 0
}

if (-not (Test-Administrator)) {
    Write-Host ''
    Write-Warn 'Diese Installation benoetigt Administratorrechte.'
    Write-Warn 'Bitte PowerShell als Administrator starten und das Skript erneut ausfuehren.'
    exit 1
}

$targetPath = Assert-SafeInstallPath (Resolve-InstallPath $InstallPath)

if ($Uninstall) {
    Remove-Installation -Path $targetPath -Name $ServiceName
    Write-Host ''
    Write-Host 'Deinstallation abgeschlossen.' -ForegroundColor Green
    exit 0
}

$source = if ($Build) { Invoke-Publish } else { Resolve-SourcePath $SourcePath }

if (-not $source) {
    Write-Host ''
    Write-Warn 'Es wurden keine Programmdateien gefunden.'
    Write-Warn 'Bitte -SourcePath auf den Publish-Ordner setzen oder -Build verwenden:'
    Write-Warn '  .\install.ps1 -Build'
    exit 1
}

Write-Info "Quelle          : $source"
Write-Info "Installationsort: $targetPath"

$running = Get-Process -Name 'DCMtoGDTReports' -ErrorAction SilentlyContinue
if ($running) {
    Write-Step 'Laufende Anwendung wird beendet'
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService -and $existingService.Status -ne 'Stopped') {
    Write-Step "Dienst '$ServiceName' wird fuer die Aktualisierung angehalten"
    Stop-Service -Name $ServiceName -Force
}

Copy-Program -Source $source -Destination $targetPath

if ($AllowUserUpdates) { Grant-UserWriteAccess -Path $targetPath }

$settingsFile = Initialize-Settings -UpdateManifestUrl $UpdateSource `
                                    -ServiceNameForUpdate $(if ($InstallService) { $ServiceName } else { '' })

$guiPath = Join-Path $targetPath $script:GuiExecutable
if (-not $NoShortcut -and (Test-Path $guiPath)) { New-StartMenuShortcut -TargetExecutable $guiPath | Out-Null }

if ($InstallService) { Install-WindowsService -Path $targetPath -Name $ServiceName }
elseif ($existingService) { Start-Service -Name $ServiceName }

Write-Host ''
Write-Host 'Installation abgeschlossen.' -ForegroundColor Green
Write-Host ''
Write-Info "Programm       : $guiPath"
Write-Info "Konfiguration  : $settingsFile"
if ($InstallService) { Write-Info "Dienst         : $ServiceName" }
if (-not $AllowUserUpdates) {
    Write-Host ''
    Write-Warn 'Selbstaktualisierung benoetigt Schreibrechte auf das Installationsverzeichnis.'
    Write-Warn 'Fuer Updates ohne Administratorrechte einmalig mit -AllowUserUpdates installieren.'
}
Write-Host ''
