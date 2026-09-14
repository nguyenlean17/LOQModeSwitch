# Automated Test Suite for LOQ Mode Automation Tool
$ErrorActionPreference = 'Continue'

$exePath = Join-Path $PSScriptRoot "..\build\loq-mode.exe"
$stateFile = Join-Path $env:LOCALAPPDATA "LOQMode\state.json"
$logFile = Join-Path $env:LOCALAPPDATA "LOQMode\loq-mode.log"

$global:passCount = 0
$global:failCount = 0

function Assert-Condition {
    param([bool]$Condition, [string]$TestName, [string]$Detail = "")
    if ($Condition) {
        Write-Host "  [PASS] $TestName" -ForegroundColor Green
        $global:passCount++
    } else {
        Write-Host "  [FAIL] $TestName" -ForegroundColor Red
        if ($Detail) { Write-Host "         Detail: $Detail" -ForegroundColor Yellow }
        $global:failCount++
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "      Starting LOQ Mode Automated Verification Suite      " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Ensure binary exists
if (-not (Test-Path $exePath)) {
    Write-Host "Building loq-mode.exe..."
    & cmd /c (Join-Path $PSScriptRoot "..\build\build.bat")
}

# -----------------------------------------------------------
# Test 1: loq-mode status
# -----------------------------------------------------------
Write-Host "`nTest 1: Status command" -ForegroundColor White
$statusOutput = & $exePath status | Out-String
$exitCode = $LASTEXITCODE
Assert-Condition ($exitCode -eq 0) "Exit code is 0"
Assert-Condition ($statusOutput -match "Machine Model") "Contains Machine Model"
Assert-Condition ($statusOutput -match "Refresh Rate") "Contains Refresh Rate"
Assert-Condition ($statusOutput -match "Lenovo Thermal") "Contains Lenovo Thermal"
Assert-Condition ($statusOutput -match "Keyboard Light") "Contains Keyboard Light"
Assert-Condition ($statusOutput -match "Windows Power Mode") "Contains Windows Power Mode"

# -----------------------------------------------------------
# Test 2: loq-mode uni --dry-run
# -----------------------------------------------------------
Write-Host "`nTest 2: Uni Mode --dry-run" -ForegroundColor White
$dryRunOutput = & $exePath uni --dry-run | Out-String
$exitCode = $LASTEXITCODE
Assert-Condition ($exitCode -eq 0) "Exit code is 0"
Assert-Condition ($dryRunOutput -match "\[DRY RUN\] Simulation complete") "Dry run confirms simulation without changes"

# -----------------------------------------------------------
# Test 3: loq-mode gaming without saved state
# -----------------------------------------------------------
Write-Host "`nTest 3: Gaming Mode when no state exists" -ForegroundColor White
$backupState = $null
if (Test-Path $stateFile) {
    $backupState = Get-Content -Path $stateFile -Raw -Encoding UTF8
    Remove-Item -Path $stateFile -Force
}

$gamingNoStateOutput = & $exePath gaming | Out-String
$exitCode = $LASTEXITCODE
Assert-Condition ($exitCode -ne 0) "Exit code is non-zero (refuses blindly applying arbitrary defaults)"
Assert-Condition ($gamingNoStateOutput -match "No saved pre-Uni state found") "Reports missing state clearly"

# Restore state file if we had one before test
if ($backupState) {
    Set-Content -Path $stateFile -Value $backupState -Encoding UTF8
}

# -----------------------------------------------------------
# Test 4: Uni Mode activation & verification
# -----------------------------------------------------------
Write-Host "`nTest 4: Uni Mode activation and live state verification" -ForegroundColor White
$uniOutput = & $exePath uni | Out-String
$exitCode = $LASTEXITCODE
Assert-Condition ($exitCode -eq 0) "Exit code is 0"
Assert-Condition (Test-Path $stateFile) "State file created at $stateFile"

# Query live status after Uni Mode
$postUniStatus = & $exePath status | Out-String
Assert-Condition ($postUniStatus -match "Refresh Rate\s*:\s*60 Hz") "Display refresh rate verified at 60 Hz"
Assert-Condition ($postUniStatus -match "Lenovo Thermal\s*:\s*Quiet") "Lenovo Thermal mode verified at Quiet (Blue LED)"
Assert-Condition ($postUniStatus -match "Keyboard Light\s*:\s*Off") "Keyboard backlight verified Off"
Assert-Condition ($postUniStatus -match "Windows Power Mode\s*:\s*Best Power Efficiency") "Windows Power mode verified Best Power Efficiency"

# -----------------------------------------------------------
# Test 5: Idempotency (running uni twice)
# -----------------------------------------------------------
Write-Host "`nTest 5: Idempotency test (running Uni Mode twice)" -ForegroundColor White
$stateBefore = Get-Content -Path $stateFile -Raw -Encoding UTF8
$uniTwiceOutput = & $exePath uni | Out-String
$exitCode = $LASTEXITCODE
Assert-Condition ($exitCode -eq 0) "Second uni run exit code is 0"
Assert-Condition ($uniTwiceOutput -match "Preserving existing pre-Uni state") "Preserves original baseline state"
$stateAfter = Get-Content -Path $stateFile -Raw -Encoding UTF8
Assert-Condition ($stateBefore -eq $stateAfter) "State file was not overwritten or corrupted on second run"

# -----------------------------------------------------------
# Test 6: Gaming Mode restoration & verification
# -----------------------------------------------------------
Write-Host "`nTest 6: Gaming Mode restoration and live state verification" -ForegroundColor White
$savedObj = $stateBefore | ConvertFrom-Json
$gamingOutput = & $exePath gaming | Out-String
$exitCode = $LASTEXITCODE
Assert-Condition ($exitCode -eq 0) "Gaming restore exit code is 0"

# Query live status after Gaming Mode
$postGamingStatus = & $exePath status | Out-String
$expectedThermal = switch ($savedObj.thermalMode) {
    1 { "Quiet" }
    2 { "Balanced" }
    3 { "Performance" }
    default { ".*" }
}
$expectedKbd = switch ($savedObj.keyboardLight) {
    1 { "Off" }
    2 { "Low" }
    3 { "High" }
    default { ".*" }
}
$expectedHzPattern = "Refresh Rate\s*:\s*" + $savedObj.refreshRate + " Hz"
$expectedThermalPattern = "Lenovo Thermal\s*:\s*$expectedThermal"
$expectedKbdPattern = "Keyboard Light\s*:\s*$expectedKbd"

Assert-Condition ($postGamingStatus -match $expectedHzPattern) "Display refresh rate restored to baseline ($($savedObj.refreshRate) Hz)"
Assert-Condition ($postGamingStatus -match $expectedThermalPattern) "Lenovo thermal mode restored to baseline (Mode $($savedObj.thermalMode))"
Assert-Condition ($postGamingStatus -match $expectedKbdPattern) "Keyboard light restored to baseline (Level $($savedObj.keyboardLight))"

# -----------------------------------------------------------
# Test 7: Auto Mode detection
# -----------------------------------------------------------
Write-Host "`nTest 7: Auto Mode detection" -ForegroundColor White
$autoOutput = & $exePath auto --dry-run | Out-String
$exitCode = $LASTEXITCODE
Assert-Condition ($exitCode -eq 0) "Auto mode exit code is 0"
Assert-Condition ($autoOutput -match "Current Power Status") "Detects current power status"
Assert-Condition ($autoOutput -match "RECOMMENDATION|OK") "Provides intelligent mode decision"

# -----------------------------------------------------------
# Test 8: Log file verification
# -----------------------------------------------------------
Write-Host "`nTest 8: Log file verification" -ForegroundColor White
Assert-Condition (Test-Path $logFile) "Log file exists at $logFile"
$recentLogs = Get-Content -Path $logFile -Tail 20
Assert-Condition ($recentLogs.Count -gt 0) "Log file contains recent operations"

# -----------------------------------------------------------
# Summary
# -----------------------------------------------------------
Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "Verification Summary: Passed: $global:passCount, Failed: $global:failCount" -ForegroundColor $(if ($global:failCount -eq 0) { "Green" } else { "Red" })
Write-Host "==========================================================" -ForegroundColor Cyan

if ($global:failCount -gt 0) {
    exit 1
} else {
    exit 0
}
