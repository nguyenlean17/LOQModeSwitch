# LOQ Mode - Lenovo LOQ Automation Tool

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6.svg)](https://microsoft.com/windows)
[![Architecture](https://img.shields.io/badge/Arch-x64-orange.svg)]()
[![Tests](https://img.shields.io/badge/Tests-28%20Passed-brightgreen.svg)]()

A lightweight, standalone Windows automation tool built specifically for **Lenovo LOQ** (and compatible Legion) laptops. Effortlessly switches between **Uni Mode** (optimized for all-day battery life during lectures without a charger) and **Gaming Mode** (restoring your exact baseline hardware configuration).

All Lenovo-specific mechanisms were reverse-engineered directly from [LenovoLegionToolkit](https://github.com/BartoszCichecki/LenovoLegionToolkit) rather than guessing undocumented APIs or modifying firmware unsafely.

---

## What Does Uni Mode Do?

```
┌────────────────────────┬─────────────────────────────┬─────────────────────────────┐
│ Hardware Subsystem     │ Baseline (Gaming)           │ Uni Mode (Battery Saver)    │
├────────────────────────┼─────────────────────────────┼─────────────────────────────┤
│ Lenovo Thermal Mode    │ Performance (Red LED)       │ Quiet Mode (Blue LED)       │
│ Display Refresh Rate   │ 144 Hz (or panel max)       │ 60 Hz (via Windows CCD API) │
│ Keyboard Backlight     │ High / On                   │ Off                         │
│ Windows Power Mode     │ Best Performance            │ Best Power Efficiency       │
│ GPU MUX Configuration  │ Discrete GPU / Hybrid       │ iGPU-Only (powers off dGPU) │
└────────────────────────┴─────────────────────────────┴─────────────────────────────┘
```

---

## Getting Started (No Installation Required)

### 1. Interactive Menu (Double-Click)
Simply **double-click** `loq-mode.exe` (or `LOQMode-Menu.bat`). It will prompt for UAC elevation and present an interactive console menu:

```text
======================================================
               LOQ Mode - System Status               
======================================================
Machine Model       : LOQ 15IAX9 (83GS) (NECN47WW)
Power Source        : AC Connected (97%)
GPU Mode            : dGPU Only (Discrete MUX Active)
Refresh Rate        : 144 Hz (Supported: 60, 75, 100, 120, 144 Hz)
Lenovo Thermal      : Performance (Red LED)
Keyboard Light      : High
Windows Power Mode  : Best Performance
Saved Profile State : Available (Saved at 2026-09-15 01:50:13)
Active Profile      : GAMING
======================================================

Select an action:
  [1] Switch to Uni Mode (Quiet, 60 Hz, Backlight Off, Best Power Efficiency)
  [2] Switch to Gaming Mode (Restore saved baseline configuration)
  [3] Preview Uni Mode (--dry-run)
  [4] Auto Mode (Detect AC / Battery)
  [5] Refresh Status
  [0] Exit

Enter choice [0-5]: 
```

### 2. Command-Line Interface (CLI)

Add `build\` to your system `PATH`, or run directly in PowerShell / Command Prompt:

```powershell
# Inspect current live status of all subsystems
loq-mode.exe status

# Preview changes without modifying hardware
loq-mode.exe uni --dry-run

# Activate Uni Mode before heading to class
loq-mode.exe uni

# Restore Gaming baseline when back home
loq-mode.exe gaming

# Intelligent AC vs Battery status check
loq-mode.exe auto

# Help and command syntax
loq-mode.exe --help
```

### 3. PowerShell Module (`LOQMode.psm1`)

```powershell
Import-Module .\src\LOQMode.psm1

# Get status object
Get-LOQStatus

# Subsystem cmdlets
Set-LOQThermalMode -Mode Quiet          # Quiet (1), Balanced (2), Performance (3)
Set-LOQDisplayRefreshRate -Hz 60       # Sets display refresh rate via CCD API
Set-LOQKeyboardBacklight -Level Off     # Off (1), Low (2), High (3)
Set-LOQWindowsPowerMode -Mode BestPowerEfficiency
```

---

## Technical Architecture & Underlying APIs

All mechanisms were discovered and adapted from Lenovo Legion Toolkit source code:

| Subsystem | Underlying API / Interface | Technical Details |
| :--- | :--- | :--- |
| **Thermal Fan Mode** | WMI: `root\wmi:LENOVO_GAMEZONE_DATA` | `GetSmartFanMode` / `SetSmartFanMode` (`1`=Quiet/Blue, `2`=Balanced/White, `3`=Performance/Red). Adjusts EC fan curve and CPU power limits. |
| **Keyboard Backlight** | WMI: `root\wmi:LENOVO_LIGHTING_METHOD` | `Get_Lighting_Current_Status` / `Set_Lighting_Current_Status` with `Lighting_ID=0` (Brightness: `1`=Off, `2`=Low, `3`=High). |
| **GPU MUX Switch** | WMI: `root\wmi:LENOVO_GAMEZONE_DATA` | `GetGSyncStatus` / `SetGSyncStatus` (`0`=Hybrid, `1`=dGPU), `GetIGPUModeStatus` / `SetIGPUModeStatus` (`1`=iGPU-Only). Accurately reports when a reboot is physically required to rewire display pins from dGPU to Intel/AMD iGPU. |
| **Display Refresh Rate** | User32 CCD API (`SetDisplayConfig`) | Connects to active desktop window station (`WinSta0\Default`), invokes `QueryDisplayConfig` and `SetDisplayConfig` with `SDC_APPLY \| SDC_USE_SUPPLIED_DISPLAY_CONFIG \| SDC_ALLOW_CHANGES \| SDC_SAVE_TO_DATABASE`. Changes active signal mode to 60 Hz dynamically. |
| **Windows Power Mode** | `powrprof.dll` Overlay Schemes | `PowerSetActiveOverlayScheme` with Best Power Efficiency GUID (`961cc777-2547-4f9d-8174-7d86181b8a7a`). |

---

## Safety & State Persistence

- **Baseline Restoration**: Prior to entering Uni Mode, current settings are inspected and stored as baseline in `%LOCALAPPDATA%\LOQMode\state.json`. `loq-mode gaming` restores this exact baseline rather than guessing.
- **Idempotency**: Running `loq-mode uni` twice will never overwrite or corrupt your saved baseline.
- **Elevation Management**: Only requests administrator rights when interacting with Lenovo ACPI WMI interfaces.
- **Auditing**: Operational logs are stored in `%LOCALAPPDATA%\LOQMode\loq-mode.log` with timestamps and Win32 error codes.

---

## Building from Source

No heavy .NET SDK or Visual Studio installation is required. Uses Windows built-in C# compiler:

```cmd
build\build.bat
```

Or manually:
```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:exe /out:build\loq-mode.exe /platform:anycpu /optimize+ src\Program.cs
```

---

## Running Verification Tests

Run the included automated 28-point test suite on your machine:
```powershell
powershell -ExecutionPolicy Bypass -File "tests\test_loq_mode.ps1"
```

---

## Pushing to Your GitHub

To publish this repository to your GitHub account:

```bash
# 1. Open terminal in the project directory
cd C:\Users\Admin\.gemini\antigravity-ide\scratch\LOQMode

# 2. Add your GitHub repository as remote
git remote add origin https://github.com/<YOUR_GITHUB_USERNAME>/<YOUR_REPO_NAME>.git

# 3. Rename branch to main (if not already main)
git branch -M main

# 4. Push code
git push -u origin main
```

---

## License

This project is licensed under the [MIT License](LICENSE).
