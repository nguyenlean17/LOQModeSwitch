# LOQMode.psm1 - PowerShell Module for Lenovo LOQ automation
# Requires Administrator privileges for Lenovo ACPI WMI operations

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:AppDataDir = Join-Path $env:LOCALAPPDATA "LOQMode"
$script:StateFile  = Join-Path $script:AppDataDir "state.json"
$script:LogFile    = Join-Path $script:AppDataDir "loq-mode.log"

if (-not (Test-Path $script:AppDataDir)) {
    New-Item -ItemType Directory -Path $script:AppDataDir -Force | Out-Null
}

function Write-LOQLog {
    param([string]$Level, [string]$Message)
    $ts = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    $line = "[$ts] [$Level] $Message"
    try {
        Add-Content -Path $script:LogFile -Value $line -Encoding UTF8
    } catch { }
}

function Test-LOQAdmin {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-LOQWmiScope {
    if (-not (Test-LOQAdmin)) {
        throw "Administrator privileges are required to interact with Lenovo ACPI WMI interfaces."
    }
}

# --- Win32 Display APIs ---
$csharpDisplaySource = @'
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace LOQMode.Native {
    public static class DisplayApi {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetProcessWindowStation(IntPtr hWinSta);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetThreadDesktop(IntPtr hDesktop);

        [DllImport("user32.dll")]
        public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        public static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray, ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        public static extern int SetDisplayConfig(uint numPathArrayElements, [In] DISPLAYCONFIG_PATH_INFO[] pathInfoArray, uint numModeInfoArrayElements, [In] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        public static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        public static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        public const int ENUM_CURRENT_SETTINGS = -1;
        public const int DISP_CHANGE_SUCCESSFUL = 0;
        public const uint CDS_UPDATEREGISTRY = 0x01;
        public const int DM_DISPLAYFREQUENCY = 0x400000;

        private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        private const uint SDC_APPLY = 0x00000080;
        private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
        private const uint SDC_ALLOW_CHANGES = 0x00000400;
        private const uint SDC_SAVE_TO_DATABASE = 0x00000200;

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology;
            public uint rotation; public uint scaling; public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering; public bool targetAvailable; public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTL { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_SOURCE_MODE
        {
            public uint width; public uint height; public uint pixelFormat; public POINTL position;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_2DREGION { public uint cx; public uint cy; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
        {
            public ulong pixelRate;
            public DISPLAYCONFIG_RATIONAL hSyncFreq;
            public DISPLAYCONFIG_RATIONAL vSyncFreq;
            public DISPLAYCONFIG_2DREGION activeSize;
            public DISPLAYCONFIG_2DREGION totalSize;
            public uint videoStandard;
            public uint scanLineOrdering;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_TARGET_MODE
        {
            public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECTL { public int left; public int top; public int right; public int bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
        {
            public POINTL PathSourceSize;
            public RECTL InuseSubPath;
            public RECTL TargetsPath;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct DISPLAYCONFIG_MODE_INFO_UNION
        {
            [FieldOffset(0)] public DISPLAYCONFIG_TARGET_MODE targetMode;
            [FieldOffset(0)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
            [FieldOffset(0)] public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_MODE_INFO
        {
            public uint infoType;
            public uint id;
            public LUID adapterId;
            public DISPLAYCONFIG_MODE_INFO_UNION modeInfo;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DISPLAY_DEVICE {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DEVMODE {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public short dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        private static void EnsureDesktopAccess()
        {
            try
            {
                IntPtr winSta0 = OpenWindowStation("WinSta0", false, 0x37F);
                if (winSta0 != IntPtr.Zero)
                {
                    SetProcessWindowStation(winSta0);
                    IntPtr desk = OpenDesktop("Default", 0, false, 0x1FF);
                    if (desk != IntPtr.Zero)
                    {
                        SetThreadDesktop(desk);
                    }
                }
            }
            catch { }
        }

        public static string GetPrimaryDevice() {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
            uint devNum = 0;
            while (EnumDisplayDevices(null, devNum, ref dd, 0)) {
                if ((dd.StateFlags & 1) != 0 && (dd.StateFlags & 4) != 0) {
                    return dd.DeviceName;
                }
                devNum++;
            }
            return @"\\.\DISPLAY1";
        }

        public static int GetCurrentRefreshRate() {
            EnsureDesktopAccess();
            try {
                uint pathCount = 0, modeCount = 0;
                int bufErr = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);
                if (bufErr == 0 && pathCount > 0) {
                    var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                    var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                    int qErr = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                    if (qErr == 0 && paths[0].targetInfo.refreshRate.Denominator > 0) {
                        double hz = (double)paths[0].targetInfo.refreshRate.Numerator / paths[0].targetInfo.refreshRate.Denominator;
                        return (int)Math.Round(hz);
                    }
                }
            } catch { }

            string device = GetPrimaryDevice();
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
            if (EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref dm)) {
                return dm.dmDisplayFrequency;
            }
            return 0;
        }

        public static int[] GetAvailableRefreshRates() {
            string device = GetPrimaryDevice();
            var current = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
            var list = new SortedSet<int>();
            if (EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref current)) {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                int modeNum = 0;
                while (EnumDisplaySettings(device, modeNum, ref dm)) {
                    if (dm.dmPelsWidth == current.dmPelsWidth && dm.dmPelsHeight == current.dmPelsHeight) {
                        list.Add(dm.dmDisplayFrequency);
                    }
                    modeNum++;
                }
            }
            int[] result = new int[list.Count];
            list.CopyTo(result);
            return result;
        }

        public static bool SetRefreshRate(int hz) {
            int cur = GetCurrentRefreshRate();
            if (cur == hz) return true;

            EnsureDesktopAccess();

            // First attempt: Windows CCD API (QueryDisplayConfig / SetDisplayConfig)
            try {
                uint pathCount = 0, modeCount = 0;
                int bufErr = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);
                if (bufErr == 0 && pathCount > 0) {
                    var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                    var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                    int qErr = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                    if (qErr == 0) {
                        paths[0].targetInfo.refreshRate.Numerator = (uint)(hz * 1000);
                        paths[0].targetInfo.refreshRate.Denominator = 1000;

                        uint tgtIdx = paths[0].targetInfo.modeInfoIdx;
                        if (tgtIdx < modeCount && modes[tgtIdx].infoType == 2) {
                            var sig = modes[tgtIdx].modeInfo.targetMode.targetVideoSignalInfo;
                            sig.vSyncFreq.Numerator = (uint)(hz * 1000);
                            sig.vSyncFreq.Denominator = 1000;
                            sig.pixelRate = (ulong)((double)hz * sig.totalSize.cx * sig.totalSize.cy);
                            sig.hSyncFreq.Numerator = (uint)(hz * sig.totalSize.cy);
                            sig.hSyncFreq.Denominator = 1;
                            modes[tgtIdx].modeInfo.targetMode.targetVideoSignalInfo = sig;
                        }

                        uint flags = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE;
                        int sErr = SetDisplayConfig(pathCount, paths, modeCount, modes, flags);
                        if (sErr == 0) return true;
                    }
                }
            } catch { }

            // Second attempt: Fallback to ChangeDisplaySettingsEx
            try {
                string device = GetPrimaryDevice();
                var current = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                if (EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref current)) {
                    var target = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                    int modeNum = 0;
                    while (EnumDisplaySettings(device, modeNum, ref target)) {
                        if (target.dmPelsWidth == current.dmPelsWidth &&
                            target.dmPelsHeight == current.dmPelsHeight &&
                            target.dmDisplayFrequency == hz) {
                            target.dmFields = DM_DISPLAYFREQUENCY;
                            int res = ChangeDisplaySettingsEx(device, ref target, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
                            if (res == DISP_CHANGE_SUCCESSFUL) return true;
                            break;
                        }
                        modeNum++;
                    }
                }
            } catch { }

            return false;
        }
    }

    public static class PowerApi {
        [DllImport("powrprof.dll")]
        public static extern uint PowerSetActiveOverlayScheme(Guid OverlaySchemeGuid);
    }
}
'@

if (-not ([System.Management.Automation.PSTypeName]'LOQMode.Native.DisplayApi').Type) {
    Add-Type -TypeDefinition $csharpDisplaySource
}

# --- Hardware Methods ---

function Get-LOQHardwareInfo {
    $cs = Get-CimInstance Win32_ComputerSystem
    $bios = Get-CimInstance Win32_BIOS
    [PSCustomObject]@{
        Model       = if ($cs.SystemFamily) { "$($cs.SystemFamily) ($($cs.Model))" } else { $cs.Model }
        BiosVersion = $bios.SMBIOSBIOSVersion
    }
}

function Get-LOQPowerSource {
    $battery = Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue
    if ($battery) {
        $isOnBattery = ($battery.BatteryStatus -eq 1)
        $percent = $battery.EstimatedChargeRemaining
    } else {
        $isOnBattery = $false
        $percent = 100
    }
    [PSCustomObject]@{
        IsOnBattery = $isOnBattery
        Percent     = $percent
    }
}

function Get-LOQThermalMode {
    Get-LOQWmiScope
    $gz = Get-CimInstance -Namespace 'root\wmi' -ClassName 'LENOVO_GAMEZONE_DATA'
    $res = Invoke-CimMethod -InputObject $gz -MethodName 'GetSmartFanMode'
    return [int]$res.Data
}

function Set-LOQThermalMode {
    param([int]$Mode)
    Get-LOQWmiScope
    $gz = Get-CimInstance -Namespace 'root\wmi' -ClassName 'LENOVO_GAMEZONE_DATA'
    Invoke-CimMethod -InputObject $gz -MethodName 'SetSmartFanMode' -Arguments @{ Data = [uint32]$Mode } | Out-Null
}

function Get-LOQGpuMode {
    Get-LOQWmiScope
    $gz = Get-CimInstance -Namespace 'root\wmi' -ClassName 'LENOVO_GAMEZONE_DATA'
    $gsync = (Invoke-CimMethod -InputObject $gz -MethodName 'GetGSyncStatus').Data
    $igpu = (Invoke-CimMethod -InputObject $gz -MethodName 'GetIGPUModeStatus').Data
    [PSCustomObject]@{
        GSyncStatus    = [int]$gsync
        IGPUModeStatus = [int]$igpu
    }
}

function Set-LOQGpuMode {
    param(
        [Parameter(Mandatory = $true)]
        [int]$GSyncStatus,
        [Parameter(Mandatory = $true)]
        [int]$IGPUModeStatus,
        [switch]$Reboot
    )
    Get-LOQWmiScope
    $gz = Get-CimInstance -Namespace 'root\wmi' -ClassName 'LENOVO_GAMEZONE_DATA'
    Invoke-CimMethod -InputObject $gz -MethodName 'SetGSyncStatus' -Arguments @{ Data = [uint32]$GSyncStatus } | Out-Null
    Invoke-CimMethod -InputObject $gz -MethodName 'SetIGPUModeStatus' -Arguments @{ mode = [uint32]$IGPUModeStatus } | Out-Null
    if ($GSyncStatus -eq 0) {
        $notifyVal = if ($IGPUModeStatus -eq 1) { 0 } else { 1 }
        try {
            Invoke-CimMethod -InputObject $gz -MethodName 'NotifyDGPUStatus' -Arguments @{ status = [uint32]$notifyVal } | Out-Null
        } catch { }
    }
    if ($Reboot) {
        Write-LOQLog -Level "INFO" -Message "Initiating system restart for MUX switch (/r /t 3)"
        Start-Process -FilePath "shutdown.exe" -ArgumentList "/r /t 3 /c `"Rebooting into LOQ Uni Mode (iGPU-only)...`"" -NoNewWindow
    }
}

function Get-LOQKeyboardBacklight {
    Get-LOQWmiScope
    $lm = Get-CimInstance -Namespace 'root\wmi' -ClassName 'LENOVO_LIGHTING_METHOD'
    $res = Invoke-CimMethod -InputObject $lm -MethodName 'Get_Lighting_Current_Status' -Arguments @{ Lighting_ID = [uint32]0 }
    return [int]$res.Current_Brightness_Level
}

function Set-LOQKeyboardBacklight {
    param([int]$Level)
    Get-LOQWmiScope
    $lm = Get-CimInstance -Namespace 'root\wmi' -ClassName 'LENOVO_LIGHTING_METHOD'
    Invoke-CimMethod -InputObject $lm -MethodName 'Set_Lighting_Current_Status' -Arguments @{
        Lighting_ID            = [uint32]0
        Current_State_Type     = [uint32]0
        Current_Brightness_Level = [uint32]$Level
    } | Out-Null
}

function Get-LOQDisplayRefreshRate {
    return [LOQMode.Native.DisplayApi]::GetCurrentRefreshRate()
}

function Get-LOQAvailableRefreshRates {
    return [LOQMode.Native.DisplayApi]::GetAvailableRefreshRates()
}

function Set-LOQDisplayRefreshRate {
    param([int]$Hz)
    return [LOQMode.Native.DisplayApi]::SetRefreshRate($Hz)
}

function Get-LOQWindowsPowerMode {
    try {
        $reg = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes' -ErrorAction SilentlyContinue
        if ($reg -and $reg.ActiveOverlayAcPowerScheme) {
            return [string]$reg.ActiveOverlayAcPowerScheme
        }
    } catch { }
    return "00000000-0000-0000-0000-000000000000"
}

function Set-LOQWindowsPowerMode {
    param([string]$GuidString)
    $g = [Guid]::Parse($GuidString)
    [LOQMode.Native.PowerApi]::PowerSetActiveOverlayScheme($g) | Out-Null
    try {
        Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes' -Name 'ActiveOverlayAcPowerScheme' -Value $GuidString -ErrorAction SilentlyContinue
        Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes' -Name 'ActiveOverlayDcPowerScheme' -Value $GuidString -ErrorAction SilentlyContinue
    } catch { }
}

# --- State Management ---

function Save-LOQState {
    param([hashtable]$State)
    $json = $State | ConvertTo-Json
    [System.IO.File]::WriteAllText($script:StateFile, $json, [System.Text.Encoding]::UTF8)
}

function Get-LOQSavedState {
    if (Test-Path $script:StateFile) {
        try {
            $json = Get-Content -Path $script:StateFile -Raw -Encoding UTF8
            return $json | ConvertFrom-Json
        } catch {
            return $null
        }
    }
    return $null
}

# --- Exported Functions ---

function Get-LOQStatus {
    [CmdletBinding()]
    param()

    $hw = Get-LOQHardwareInfo
    $pwr = Get-LOQPowerSource
    $gpu = Get-LOQGpuMode
    $curHz = Get-LOQDisplayRefreshRate
    $availHz = Get-LOQAvailableRefreshRates
    $thermal = Get-LOQThermalMode
    $kbd = Get-LOQKeyboardBacklight
    $winPwr = Get-LOQWindowsPowerMode
    $saved = Get-LOQSavedState

    $gpuDesc = switch ($true) {
        ($gpu.GSyncStatus -eq 1) { "dGPU Only (Discrete MUX Active, Optimus Off)" }
        ($gpu.IGPUModeStatus -eq 1) { "iGPU Only (Discrete GPU Powered Off)" }
        ($gpu.IGPUModeStatus -eq 2) { "Hybrid-Auto (dGPU dynamic sleep)" }
        default { "Hybrid Mode (Optimus / Dynamic Switching)" }
    }

    $thermalDesc = switch ($thermal) {
        1 { "Quiet (Blue LED / Power Saving)" }
        2 { "Balanced (White LED)" }
        3 { "Performance (Red LED)" }
        255 { "Custom / GodMode (Purple LED)" }
        default { "Unknown ($thermal)" }
    }

    $kbdDesc = switch ($kbd) {
        1 { "Off" }
        2 { "Low" }
        3 { "High" }
        default { "Unknown ($kbd)" }
    }

    $winPwrDesc = switch ($winPwr) {
        "961cc777-2547-4f9d-8174-7d86181b8a7a" { "Best Power Efficiency" }
        "ded574b5-45a0-4f42-8737-46345c09c238" { "Best Performance" }
        "00000000-0000-0000-0000-000000000000" { "Balanced" }
        default { "Custom ($winPwr)" }
    }

    [PSCustomObject]@{
        Model             = $hw.Model
        Bios              = $hw.BiosVersion
        PowerSource       = if ($pwr.IsOnBattery) { "Battery ($($pwr.Percent)%)" } else { "AC Connected ($($pwr.Percent)%)" }
        GpuMode           = $gpuDesc
        RefreshRate       = "$curHz Hz"
        SupportedRates    = ($availHz -join ", ") + " Hz"
        ThermalMode       = $thermalDesc
        KeyboardLight     = $kbdDesc
        WindowsPowerMode  = $winPwrDesc
        SavedProfileState = if ($saved) { "Available (Saved $($saved.timestamp))" } else { "None" }
        ActiveProfile     = if ($saved) { $saved.activeProfile } else { "None" }
    }
}

Export-ModuleMember -Function Get-LOQStatus, Get-LOQHardwareInfo, Get-LOQThermalMode, Set-LOQThermalMode, Get-LOQGpuMode, Set-LOQGpuMode, Get-LOQKeyboardBacklight, Set-LOQKeyboardBacklight, Get-LOQDisplayRefreshRate, Set-LOQDisplayRefreshRate, Get-LOQWindowsPowerMode, Set-LOQWindowsPowerMode, Get-LOQSavedState, Save-LOQState
