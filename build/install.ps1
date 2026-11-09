#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs SiteMonitor to Program Files, registers the Windows service, and adds a Start Menu shortcut.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1
#>
param(
    [string]$InstallDir = "$env:ProgramFiles\SiteMonitor"
)
$ErrorActionPreference = 'Stop'
$ServiceName = 'SiteMonitor'
$Source = $PSScriptRoot

foreach ($f in 'SiteMonitor.exe', 'SiteMonitorService.exe') {
    if (-not (Test-Path (Join-Path $Source $f))) { throw "$f not found next to install.ps1" }
}

# Stop an existing service so the exe can be replaced (upgrade).
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Write-Host "Stopping existing service..."
    Stop-Service $ServiceName -Force
    (Get-Service $ServiceName).WaitForStatus('Stopped', '00:00:30')
}

Write-Host "Copying files to $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item (Join-Path $Source '*') $InstallDir -Recurse -Force -Exclude 'install.ps1'

# Data folder: admins + SYSTEM only (it holds the encrypted SMTP password).
$data = Join-Path $env:ProgramData 'SiteMonitor'
New-Item -ItemType Directory -Force -Path $data | Out-Null
icacls $data /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
# Logs hold no secrets; let signed-in users read them (Explorer runs un-elevated under UAC).
$logs = Join-Path $data 'logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
icacls $logs /grant '*S-1-5-11:(OI)(CI)RX' | Out-Null

$exe = Join-Path $InstallDir 'SiteMonitorService.exe'
if (-not $existing) {
    Write-Host "Registering service"
    New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName 'Site Monitor' `
        -Description 'Monitors websites and hosts and emails an alert when they go down.' -StartupType Automatic | Out-Null
    sc.exe config $ServiceName start= delayed-auto | Out-Null
    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null
}

Write-Host "Starting service"
Start-Service $ServiceName

# Start Menu shortcut for the settings app
$lnk = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\SiteMonitor.lnk'
$shell = New-Object -ComObject WScript.Shell
$sc = $shell.CreateShortcut($lnk)
$sc.TargetPath = Join-Path $InstallDir 'SiteMonitor.exe'
$sc.WorkingDirectory = $InstallDir
$sc.Description = 'SiteMonitor settings'
$sc.Save()

Write-Host ""
Write-Host "SiteMonitor installed. Open 'SiteMonitor' from the Start Menu to add sites and set up email." -ForegroundColor Green
Start-Process (Join-Path $InstallDir 'SiteMonitor.exe')
