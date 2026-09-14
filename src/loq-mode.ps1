<#
.SYNOPSIS
    Lenovo LOQ Mode Automation CLI (PowerShell launcher)
.DESCRIPTION
    Launches the compiled loq-mode.exe binary or executes LOQMode module commands.
.EXAMPLE
    .\loq-mode.ps1 status
    .\loq-mode.ps1 uni --dry-run
    .\loq-mode.ps1 uni
    .\loq-mode.ps1 gaming
    .\loq-mode.ps1 auto
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Command = "status",

    [switch]$DryRun,
    [switch]$Force,
    [switch]$Help
)

$exePath = Join-Path $PSScriptRoot "..\build\loq-mode.exe"

if (-not (Test-Path $exePath)) {
    # If binary is not yet built, build it now
    $buildBat = Join-Path $PSScriptRoot "..\build\build.bat"
    & cmd /c $buildBat
}

$argList = @($Command)
if ($DryRun) { $argList += "--dry-run" }
if ($Force)  { $argList += "--force" }
if ($Help)   { $argList += "--help" }

& $exePath @argList
