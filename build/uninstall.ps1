#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Removes the SiteMonitor service, program files and shortcut. Pass -RemoveData to also delete settings and logs.
#>
param(
    [string]$InstallDir = "$env:ProgramFiles\SiteMonitor",
    [switch]$RemoveData
)
$ErrorActionPreference = 'Stop'
$ServiceName = 'SiteMonitor'

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') { Stop-Service $ServiceName -Force; $svc.WaitForStatus('Stopped', '00:00:30') }
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Service removed."
}

Get-Process SiteMonitor -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\SiteMonitor.lnk') -ErrorAction SilentlyContinue
if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force; Write-Host "Program files removed." }

if ($RemoveData) {
    Remove-Item (Join-Path $env:ProgramData 'SiteMonitor') -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Settings and logs removed."
} else {
    Write-Host "Settings kept in $env:ProgramData\SiteMonitor (use -RemoveData to delete)."
}
