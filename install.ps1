# Installs DesktopGroups from source for the current user (no admin needed):
#   1. publishes a Release build, precompiled with ReadyToRun, to the install folder
#   2. starts it with Windows (HKCU Run key; same setting as the panel's "Start with Windows")
#   3. starts it. On startup the app adds "Desktop group" to the desktop's New menu
#      and points every group shortcut at this copy.
#
# The install folder must not be under AppData: on some machines Explorer won't show icons stored there.

param([string]$InstallDir = "F:\Apps\DesktopGroups")

$ErrorActionPreference = "Stop"
$exe = Join-Path $InstallDir "DesktopGroups.exe"

Get-Process DesktopGroups -ErrorAction SilentlyContinue | Stop-Process
Get-Process DesktopGroups -ErrorAction SilentlyContinue | Wait-Process

dotnet publish "$PSScriptRoot\DesktopGroups.csproj" -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o $InstallDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "DesktopGroups" -Value "`"$exe`""

Start-Process $exe
Write-Host "Installed to $InstallDir and started."
