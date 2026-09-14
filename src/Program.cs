using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace LOQMode
{
    class Program
    {
        public const string Version = "1.0.0";

        static int Main(string[] args)
        {
            bool isInteractive = (args.Length == 0 ||
                                  HasFlag(args, "--interactive") ||
                                  HasFlag(args, "-i") ||
                                  (args.Length == 1 && args[0].Equals("menu", StringComparison.OrdinalIgnoreCase)));

            try
            {
                if (isInteractive)
                {
                    if (!IsAdministrator() && !HasFlag(args, "--no-elevate"))
                    {
                        return RunElevated(args, interactive: true);
                    }
                    return RunInteractiveMenu();
                }

                if (HasFlag(args, "--help") || HasFlag(args, "-h") || HasFlag(args, "/?"))
                {
                    PrintHelp();
                    return 0;
                }

                if (HasFlag(args, "--version") || HasFlag(args, "-v"))
                {
                    Console.WriteLine("LOQ Mode Automation Tool v" + Version);
                    return 0;
                }

                string outputFile = GetNamedArg(args, "--output-file");
                if (!string.IsNullOrEmpty(outputFile))
                {
                    try
                    {
                        var streamWriter = new StreamWriter(outputFile, false, Encoding.UTF8) { AutoFlush = true };
                        Console.SetOut(streamWriter);
                        Console.SetError(streamWriter);
                    }
                    catch { }
                }

                string command = args[0].ToLowerInvariant().TrimStart('-');
                bool dryRun = HasFlag(args, "--dry-run");
                bool force = HasFlag(args, "--force");
                bool noElevate = HasFlag(args, "--no-elevate");
                bool reboot = HasFlag(args, "--reboot") || HasFlag(args, "-r");

                bool needsElevation = (command == "uni" || command == "gaming" || command == "status" || command == "auto");

                if (needsElevation && !IsAdministrator() && !noElevate)
                {
                    return RunElevated(args, interactive: false);
                }

                Logger.Init();
                Logger.Info("Command executed: " + string.Join(" ", args) + " (Admin: " + IsAdministrator() + ")");

                switch (command)
                {
                    case "status":
                        return HandleStatus();

                    case "uni":
                        return HandleUni(dryRun, force, reboot);

                    case "gaming":
                        return HandleGaming(dryRun, force, reboot);

                    case "auto":
                        return HandleAuto(dryRun, force);

                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Unknown command: '" + args[0] + "'. Use 'loq-mode --help' for usage.");
                        Console.ResetColor();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Unhandled exception: " + ex.ToString());
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[ERROR] An unexpected error occurred: " + ex.Message);
                Console.ResetColor();
                if (isInteractive)
                {
                    Console.WriteLine("\nPress Enter to exit...");
                    Console.ReadLine();
                }
                return 1;
            }
        }

        static int RunInteractiveMenu()
        {
            Logger.Init();
            try { Console.Title = "Lenovo LOQ Mode Controller v" + Version; } catch { }

            while (true)
            {
                try { Console.Clear(); } catch { }
                HandleStatus();

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Select an action:");
                Console.ResetColor();
                Console.WriteLine("  [1] Switch to Uni Mode (Quiet, 60 Hz, Backlight Off, Best Power Efficiency)");
                Console.WriteLine("  [2] Switch to Uni Mode & Reboot into iGPU-only (--reboot)");
                Console.WriteLine("  [3] Switch to Gaming Mode (Restore saved baseline configuration)");
                Console.WriteLine("  [4] Switch to Gaming Mode & Reboot (--reboot)");
                Console.WriteLine("  [5] Preview Uni Mode (--dry-run)");
                Console.WriteLine("  [6] Auto Mode (Detect AC / Battery)");
                Console.WriteLine("  [7] Refresh Status");
                Console.WriteLine("  [0] Exit");
                Console.Write("\nEnter choice [0-7]: ");

                string input = Console.ReadLine();
                if (input == null) break;
                input = input.Trim();

                if (input == "0" || input.Equals("q", StringComparison.OrdinalIgnoreCase) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                Console.WriteLine();
                switch (input)
                {
                    case "1":
                        int gsyncMode = LenovoWmi.GetGSyncStatus();
                        bool doReboot = false;
                        if (gsyncMode == 1)
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.Write("Laptop is in dGPU mode. A restart is required for iGPU-only mode. Reboot now? [y/N]: ");
                            Console.ResetColor();
                            string ans = Console.ReadLine();
                            if (ans != null && (ans.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) || ans.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)))
                            {
                                doReboot = true;
                            }
                        }
                        HandleUni(dryRun: false, force: false, reboot: doReboot);
                        break;
                    case "2":
                        HandleUni(dryRun: false, force: false, reboot: true);
                        break;
                    case "3":
                        HandleGaming(dryRun: false, force: false, reboot: false);
                        break;
                    case "4":
                        HandleGaming(dryRun: false, force: false, reboot: true);
                        break;
                    case "5":
                        HandleUni(dryRun: true, force: false, reboot: false);
                        break;
                    case "6":
                        HandleAuto(dryRun: false, force: false);
                        break;
                    case "7":
                        continue;
                    default:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("Invalid option. Please enter 0 through 7.");
                        Console.ResetColor();
                        break;
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("\nPress Enter to return to the menu...");
                Console.ResetColor();
                Console.ReadLine();
            }

            return 0;
        }

        #region Elevation Helper

        static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        static int RunElevated(string[] args, bool interactive = false)
        {
            if (interactive)
            {
                try
                {
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = "--interactive --no-elevate",
                        Verb = "runas",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Normal
                    };

                    var proc = Process.Start(startInfo);
                    if (proc != null)
                    {
                        proc.WaitForExit();
                        return proc.ExitCode;
                    }
                    return 1;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[NOTICE] Administrator privileges are required to communicate with Lenovo ACPI WMI interfaces.");
                    Console.WriteLine("Elevation request cancelled or failed: " + ex.Message);
                    Console.ResetColor();
                    Console.WriteLine("\nPress Enter to exit...");
                    Console.ReadLine();
                    return 1;
                }
            }

            string tempFile = Path.Combine(Path.GetTempPath(), "loq_mode_" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                string arguments = string.Join(" ", args) + " --no-elevate --output-file \"" + tempFile + "\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                var proc = Process.Start(startInfo);
                if (proc != null)
                {
                    proc.WaitForExit();
                    if (File.Exists(tempFile))
                    {
                        string output = File.ReadAllText(tempFile, Encoding.UTF8);
                        Console.Write(output);
                    }
                    return proc.ExitCode;
                }
                return 1;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[NOTICE] Administrator privileges are required to communicate with Lenovo ACPI WMI interfaces.");
                Console.WriteLine("Elevation request failed: " + ex.Message);
                Console.WriteLine("Please run this command in an elevated Administrator command prompt or PowerShell.");
                Console.ResetColor();
                return 1;
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        static string GetNamedArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        static bool HasFlag(string[] args, string flag)
        {
            foreach (var arg in args)
            {
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        #endregion

        #region Command Handlers

        static int HandleStatus()
        {
            Console.WriteLine("======================================================");
            Console.WriteLine("               LOQ Mode - System Status               ");
            Console.WriteLine("======================================================");

            // Hardware & OS Info
            string bios = LenovoWmi.GetBiosVersion();
            string model = LenovoWmi.GetModel();
            Console.WriteLine(string.Format("{0,-20}: {1} ({2})", "Machine Model", model, bios));

            // Power Status
            var pwr = PowerManager.GetPowerStatus();
            string powerSource = pwr.IsOnBattery ? "Battery (" + pwr.BatteryLifePercent + "%)" : "AC Connected (" + pwr.BatteryLifePercent + "%)";
            Console.WriteLine(string.Format("{0,-20}: {1}", "Power Source", powerSource));

            // GPU Mode
            int gsync = LenovoWmi.GetGSyncStatus();
            int igpuMode = LenovoWmi.GetIGPUModeStatus();
            string gpuDescription;
            if (gsync == 1)
                gpuDescription = "dGPU Only (Discrete MUX Active, Optimus Off)";
            else if (igpuMode == 1)
                gpuDescription = "iGPU Only (Discrete GPU Powered Off)";
            else if (igpuMode == 2)
                gpuDescription = "Hybrid-Auto (dGPU dynamic sleep)";
            else if (gsync == 0 && igpuMode == 0)
                gpuDescription = "Hybrid Mode (Optimus / Dynamic Switching)";
            else
                gpuDescription = "Unknown";

            Console.WriteLine(string.Format("{0,-20}: {1}", "GPU Mode", gpuDescription));

            // Display Refresh Rate
            int currentHz = DisplayManager.GetCurrentRefreshRate();
            var availableHz = DisplayManager.GetAvailableRefreshRates();
            string hzList = (availableHz != null && availableHz.Count > 0) ? string.Join(", ", availableHz) + " Hz" : "Unknown";
            Console.WriteLine(string.Format("{0,-20}: {1} Hz (Supported: {2})", "Refresh Rate", currentHz, hzList));

            // Lenovo Thermal Mode
            int thermalMode = LenovoWmi.GetSmartFanMode();
            string thermalDesc;
            switch (thermalMode)
            {
                case 1: thermalDesc = "Quiet (Blue LED / Power Saving)"; break;
                case 2: thermalDesc = "Balanced (White LED)"; break;
                case 3: thermalDesc = "Performance (Red LED)"; break;
                case 255: thermalDesc = "Custom / GodMode (Purple LED)"; break;
                default: thermalDesc = "Unknown (" + thermalMode + ")"; break;
            }
            Console.WriteLine(string.Format("{0,-20}: {1}", "Lenovo Thermal", thermalDesc));

            // Keyboard Backlight
            int kbd = LenovoWmi.GetKeyboardBacklight();
            string kbdDesc;
            switch (kbd)
            {
                case 1: kbdDesc = "Off"; break;
                case 2: kbdDesc = "Low"; break;
                case 3: kbdDesc = "High"; break;
                default: kbdDesc = "Unknown (" + kbd + ")"; break;
            }
            Console.WriteLine(string.Format("{0,-20}: {1}", "Keyboard Light", kbdDesc));

            // Windows Power Mode Overlay
            Guid currentOverlay = PowerManager.GetActiveOverlayScheme();
            string winPowerDesc;
            if (currentOverlay == PowerManager.BestPowerEfficiency)
                winPowerDesc = "Best Power Efficiency";
            else if (currentOverlay == PowerManager.BestPerformance)
                winPowerDesc = "Best Performance";
            else if (currentOverlay == PowerManager.Balanced)
                winPowerDesc = "Balanced";
            else
                winPowerDesc = "Custom (" + currentOverlay.ToString("B") + ")";
            Console.WriteLine(string.Format("{0,-20}: {1}", "Windows Power Mode", winPowerDesc));

            // Saved State
            var state = StateManager.LoadState();
            if (state != null)
            {
                Console.WriteLine(string.Format("{0,-20}: Available (Saved at {1})", "Saved Profile State", state.Timestamp));
                Console.WriteLine(string.Format("{0,-20}: {1}", "Active Profile", state.ActiveProfile));
            }
            else
            {
                Console.WriteLine(string.Format("{0,-20}: None (No pre-Uni configuration saved)", "Saved Profile State"));
            }

            Console.WriteLine("======================================================");
            return 0;
        }

        static int HandleUni(bool dryRun, bool force, bool reboot = false)
        {
            Console.WriteLine("======================================================");
            Console.WriteLine(dryRun ? "             LOQ Mode - Uni Mode [DRY RUN]             " : "               LOQ Mode - Activating Uni Mode          ");
            Console.WriteLine("======================================================");

            // Read Current State
            int curGsync = LenovoWmi.GetGSyncStatus();
            int curIgpu = LenovoWmi.GetIGPUModeStatus();
            int curHz = DisplayManager.GetCurrentRefreshRate();
            int curThermal = LenovoWmi.GetSmartFanMode();
            int curKbd = LenovoWmi.GetKeyboardBacklight();
            Guid curWinPower = PowerManager.GetActiveOverlayScheme();

            // Desired Targets
            int targetHz = 60;
            int targetThermal = 1; // Quiet
            int targetKbd = 1;     // Off
            Guid targetWinPower = PowerManager.BestPowerEfficiency;
            bool mUxRebootRequired = (curGsync == 1);

            Console.WriteLine("Target Profile Changes:");
            Console.WriteLine(string.Format("  * GPU Mode:         {0} -> iGPU-only {1}",
                curGsync == 1 ? "dGPU (Discrete)" : (curIgpu == 1 ? "iGPU-only (Current)" : "Hybrid"),
                mUxRebootRequired ? "(Reboot required to switch MUX)" : "(Dynamic)"));
            Console.WriteLine(string.Format("  * Display Rate:     {0} Hz -> {1} Hz", curHz, targetHz));
            Console.WriteLine(string.Format("  * Lenovo Thermal:   {0} -> Quiet (Blue LED)", FormatThermal(curThermal)));
            Console.WriteLine(string.Format("  * Keyboard Light:   {0} -> Off", FormatKbd(curKbd)));
            Console.WriteLine(string.Format("  * Windows Power:    {0} -> Best Power Efficiency", FormatWinPower(curWinPower)));
            if (reboot)
            {
                Console.WriteLine("  * Reboot Action:    System will REBOOT in 3 seconds to complete iGPU MUX transition.");
            }
            Console.WriteLine("------------------------------------------------------");

            if (dryRun)
            {
                if (reboot)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[DRY RUN] Would initiate system restart in 3 seconds to activate iGPU-only mode.");
                    Console.ResetColor();
                }
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[DRY RUN] Simulation complete. No system settings were changed.");
                Console.ResetColor();
                return 0;
            }

            // Save State before entering Uni Mode if not already in Uni mode or no state saved
            var existingState = StateManager.LoadState();
            if (existingState == null || existingState.ActiveProfile != "UNI")
            {
                var newState = new SystemState
                {
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    GSyncStatus = curGsync,
                    IGPUModeStatus = curIgpu,
                    RefreshRate = curHz,
                    ThermalMode = curThermal,
                    KeyboardLight = curKbd,
                    WindowsPowerMode = curWinPower.ToString(),
                    ActiveProfile = "UNI"
                };
                StateManager.SaveState(newState);
                Logger.Info("Saved pre-Uni state to state.json");
                Console.WriteLine("[OK] Saved pre-Uni configuration to state.json for Gaming Mode restoration.");
            }
            else
            {
                Console.WriteLine("[INFO] Preserving existing pre-Uni state previously saved.");
            }

            // 1. Set Lenovo Thermal Mode to Quiet (1)
            try
            {
                LenovoWmi.SetSmartFanMode(targetThermal);
                int verifiedThermal = LenovoWmi.GetSmartFanMode();
                if (verifiedThermal == targetThermal)
                {
                    Console.WriteLine("[OK] Lenovo Thermal Mode set to Quiet (Blue LED).");
                    Logger.Info("Set thermal mode to Quiet (1).");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[WARN] Set thermal mode to Quiet, but reported value is " + verifiedThermal);
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAIL] Failed to set thermal mode: " + ex.Message);
                Console.ResetColor();
                Logger.Error("Thermal mode error: " + ex.ToString());
            }

            // 2. Set Keyboard Backlight to Off (1)
            try
            {
                LenovoWmi.SetKeyboardBacklight(targetKbd);
                int verifiedKbd = LenovoWmi.GetKeyboardBacklight();
                if (verifiedKbd == targetKbd)
                {
                    Console.WriteLine("[OK] Keyboard backlight turned Off.");
                    Logger.Info("Set keyboard backlight to Off (1).");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[WARN] Keyboard backlight set to Off, but reported value is " + verifiedKbd);
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAIL] Failed to set keyboard backlight: " + ex.Message);
                Console.ResetColor();
                Logger.Error("Keyboard backlight error: " + ex.ToString());
            }

            // 3. Set Display Refresh Rate to 60 Hz
            try
            {
                if (curHz != targetHz)
                {
                    bool hzOk = DisplayManager.SetRefreshRate(targetHz);
                    int verifiedHz = DisplayManager.GetCurrentRefreshRate();
                    if (hzOk && verifiedHz == targetHz)
                    {
                        Console.WriteLine("[OK] Display refresh rate set to 60 Hz.");
                        Logger.Info("Set refresh rate to 60 Hz.");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("[WARN] Requested 60 Hz, but active refresh rate is " + verifiedHz + " Hz.");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.WriteLine("[OK] Display refresh rate is already 60 Hz.");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAIL] Failed to set refresh rate: " + ex.Message);
                Console.ResetColor();
                Logger.Error("Refresh rate error: " + ex.ToString());
            }

            // 4. Set Windows Power Mode to Best Power Efficiency
            try
            {
                PowerManager.SetActiveOverlayScheme(targetWinPower);
                Console.WriteLine("[OK] Windows Power Mode set to Best Power Efficiency.");
                Logger.Info("Set Windows Power Mode to Best Power Efficiency.");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAIL] Failed to set Windows power mode: " + ex.Message);
                Console.ResetColor();
                Logger.Error("Windows power mode error: " + ex.ToString());
            }

            // 5. Switch GPU Mode to iGPU-only
            try
            {
                if (curGsync == 1)
                {
                    // Laptop is currently in dGPU mode.
                    LenovoWmi.SetGSyncStatus(0);
                    LenovoWmi.SetIGPUModeStatus(1);
                    Logger.Info("Queued MUX switch: SetGSyncStatus(0), SetIGPUModeStatus(1). Reboot required.");

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("------------------------------------------------------");
                    Console.WriteLine("[IMPORTANT] MUX Switch Queued -> System Reboot Required!");
                    Console.WriteLine("Your laptop was in discrete dGPU mode.");
                    Console.WriteLine("The firmware has been instructed to switch the MUX to Hybrid/iGPU-only.");
                    Console.WriteLine("A system restart is required for the BIOS/firmware to physically");
                    Console.WriteLine("route the internal display to Intel iGPU.");
                    Console.ResetColor();
                }
                else
                {
                    // Laptop is already in Hybrid mode.
                    if (curIgpu != 1)
                    {
                        LenovoWmi.SetIGPUModeStatus(1);
                        LenovoWmi.NotifyDGPUStatus(0);
                        Logger.Info("Set IGPUModeStatus(1) and notified dGPU(0).");
                        Console.WriteLine("[OK] GPU switched to iGPU-only (Discrete GPU powered off).");
                    }
                    else
                    {
                        Console.WriteLine("[OK] GPU is already in iGPU-only mode.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAIL] Failed to switch GPU mode: " + ex.Message);
                Console.ResetColor();
                Logger.Error("GPU mode switch error: " + ex.ToString());
            }

            Console.WriteLine("======================================================");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[SUCCESS] Uni Mode configuration applied successfully!");
            Console.ResetColor();

            if (reboot)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("======================================================");
                Console.WriteLine("[REBOOT] Initiating system restart in 3 seconds to complete iGPU-only transition...");
                Console.WriteLine("         (To abort: run 'shutdown /a' in command prompt)");
                Console.WriteLine("======================================================");
                Console.ResetColor();
                Logger.Info("Initiating system restart for iGPU mode via shutdown.exe /r /t 3");

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "shutdown.exe",
                        Arguments = "/r /t 3 /c \"Rebooting into LOQ Uni Mode (iGPU-only)...\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[FAIL] Failed to trigger system restart: " + ex.Message);
                    Console.ResetColor();
                    Logger.Error("Reboot error: " + ex.ToString());
                }
            }

            return 0;
        }

        static int HandleGaming(bool dryRun, bool force, bool reboot = false)
        {
            Console.WriteLine("======================================================");
            Console.WriteLine(dryRun ? "            LOQ Mode - Gaming Mode [DRY RUN]           " : "             LOQ Mode - Restoring Gaming Mode          ");
            Console.WriteLine("======================================================");

            var saved = StateManager.LoadState();
            if (saved == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] No saved pre-Uni state found!");
                Console.WriteLine("State file is missing at: " + StateManager.StateFilePath);
                Console.WriteLine("Cannot blindly apply arbitrary defaults.");
                Console.WriteLine("Run 'loq-mode uni' first to establish and save your baseline configuration.");
                Console.ResetColor();
                return 1;
            }

            // Current State
            int curGsync = LenovoWmi.GetGSyncStatus();
            int curIgpu = LenovoWmi.GetIGPUModeStatus();
            int curHz = DisplayManager.GetCurrentRefreshRate();
            int curThermal = LenovoWmi.GetSmartFanMode();
            int curKbd = LenovoWmi.GetKeyboardBacklight();
            Guid curWinPower = PowerManager.GetActiveOverlayScheme();

            Guid targetWinPower = Guid.Empty;
            try { targetWinPower = new Guid(saved.WindowsPowerMode); } catch { }

            bool mUxRebootRequired = (curGsync != saved.GSyncStatus);

            Console.WriteLine("Restoring Previous Baseline Settings (Saved " + saved.Timestamp + "):");
            Console.WriteLine(string.Format("  * GPU Mode:         Current -> {0} {1}",
                saved.GSyncStatus == 1 ? "dGPU (Discrete Only)" : (saved.IGPUModeStatus == 1 ? "iGPU Only" : "Hybrid"),
                mUxRebootRequired ? "(Reboot required to switch MUX)" : "(Dynamic)"));
            Console.WriteLine(string.Format("  * Display Rate:     {0} Hz -> {1} Hz", curHz, saved.RefreshRate));
            Console.WriteLine(string.Format("  * Lenovo Thermal:   {0} -> {1}", FormatThermal(curThermal), FormatThermal(saved.ThermalMode)));
            Console.WriteLine(string.Format("  * Keyboard Light:   {0} -> {1}", FormatKbd(curKbd), FormatKbd(saved.KeyboardLight)));
            Console.WriteLine(string.Format("  * Windows Power:    {0} -> {1}", FormatWinPower(curWinPower), FormatWinPower(targetWinPower)));
            if (reboot)
            {
                Console.WriteLine("  * Reboot Action:    System will REBOOT in 3 seconds to complete Gaming Mode transition.");
            }
            Console.WriteLine("------------------------------------------------------");

            if (dryRun)
            {
                if (reboot)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[DRY RUN] Would initiate system restart in 3 seconds to restore Gaming Mode.");
                    Console.ResetColor();
                }
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[DRY RUN] Simulation complete. No system settings were restored.");
                Console.ResetColor();
                return 0;
            }

            // 1. Restore Lenovo Thermal Mode
            try
            {
                LenovoWmi.SetSmartFanMode(saved.ThermalMode);
                int verified = LenovoWmi.GetSmartFanMode();
                Console.WriteLine(string.Format("[OK] Restored Lenovo Thermal Mode to {0}.", FormatThermal(verified)));
                Logger.Info("Restored thermal mode to " + saved.ThermalMode);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAIL] Failed to restore thermal mode: " + ex.Message);
                Console.ResetColor();
                Logger.Error("Thermal restore error: " + ex.ToString());
            }

            // 2. Restore Keyboard Backlight
            try
            {
                LenovoWmi.SetKeyboardBacklight(saved.KeyboardLight);
                int verified = LenovoWmi.GetKeyboardBacklight();
                Console.WriteLine(string.Format("[OK] Restored Keyboard Backlight to {0}.", FormatKbd(verified)));
                Logger.Info("Restored keyboard backlight to " + saved.KeyboardLight);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAIL] Failed to restore keyboard backlight: " + ex.Message);
                Console.ResetColor();
                Logger.Error("Keyboard restore error: " + ex.ToString());
            }

            // 3. Restore Display Refresh Rate
            try
            {
                if (curHz != saved.RefreshRate)
                {
                    bool ok = DisplayManager.SetRefreshRate(saved.RefreshRate);
                    int verifiedHz = DisplayManager.GetCurrentRefreshRate();
                    if (ok && verifiedHz == saved.RefreshRate)
                    {
                        Console.WriteLine(string.Format("[OK] Restored display refresh rate to {0} Hz.", verifiedHz));
                        Logger.Info("Restored refresh rate to " + saved.RefreshRate);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine(string.Format("[WARN] Attempted {0} Hz, active is {1} Hz.", saved.RefreshRate, verifiedHz));
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.WriteLine(string.Format("[OK] Display refresh rate is already {0} Hz.", curHz));
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAIL] Failed to restore refresh rate: " + ex.Message);
                Console.ResetColor();
                Logger.Error("Refresh rate restore error: " + ex.ToString());
            }

            // 4. Restore Windows Power Mode
            try
            {
                PowerManager.SetActiveOverlayScheme(targetWinPower);
                Console.WriteLine(string.Format("[OK] Restored Windows Power Mode to {0}.", FormatWinPower(targetWinPower)));
                Logger.Info("Restored Windows Power Mode to " + targetWinPower);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAIL] Failed to restore Windows power mode: " + ex.Message);
                Console.ResetColor();
                Logger.Error("Power mode restore error: " + ex.ToString());
            }

            // 5. Restore GPU Mode
            try
            {
                if (curGsync != saved.GSyncStatus)
                {
                    LenovoWmi.SetGSyncStatus(saved.GSyncStatus);
                    LenovoWmi.SetIGPUModeStatus(saved.IGPUModeStatus);
                    if (saved.GSyncStatus == 0 && saved.IGPUModeStatus != 1)
                        LenovoWmi.NotifyDGPUStatus(1);

                    Logger.Info("Restored GPU mode: GSync=" + saved.GSyncStatus + ", IGPUMode=" + saved.IGPUModeStatus);

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("------------------------------------------------------");
                    Console.WriteLine("[IMPORTANT] MUX Switch Queued -> System Reboot Required!");
                    Console.WriteLine(string.Format("Restoring to {0} requires a system restart for firmware",
                        saved.GSyncStatus == 1 ? "discrete dGPU mode" : "Hybrid mode"));
                    Console.WriteLine("to physically reconfigure display pipeline routing.");
                    Console.ResetColor();
                }
                else
                {
                    if (curIgpu != saved.IGPUModeStatus)
                    {
                        LenovoWmi.SetIGPUModeStatus(saved.IGPUModeStatus);
                        if (saved.IGPUModeStatus != 1)
                            LenovoWmi.NotifyDGPUStatus(1);

                        Logger.Info("Restored IGPUMode to " + saved.IGPUModeStatus);
                        Console.WriteLine("[OK] Restored GPU mode to " + (saved.IGPUModeStatus == 0 ? "Hybrid" : "Auto"));
                    }
                    else
                    {
                        Console.WriteLine("[OK] GPU mode is already restored.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAIL] Failed to restore GPU mode: " + ex.Message);
                Console.ResetColor();
                Logger.Error("GPU mode restore error: " + ex.ToString());
            }

            // Update saved state profile to GAMING
            saved.ActiveProfile = "GAMING";
            StateManager.SaveState(saved);

            Console.WriteLine("======================================================");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[SUCCESS] Gaming Mode configuration restored successfully!");
            Console.ResetColor();

            if (reboot)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("======================================================");
                Console.WriteLine("[REBOOT] Initiating system restart in 3 seconds to complete Gaming Mode transition...");
                Console.WriteLine("         (To abort: run 'shutdown /a' in command prompt)");
                Console.WriteLine("======================================================");
                Console.ResetColor();
                Logger.Info("Initiating system restart for Gaming mode via shutdown.exe /r /t 3");

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "shutdown.exe",
                        Arguments = "/r /t 3 /c \"Rebooting into LOQ Gaming Mode...\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[FAIL] Failed to trigger system restart: " + ex.Message);
                    Console.ResetColor();
                    Logger.Error("Reboot error: " + ex.ToString());
                }
            }

            return 0;
        }

        static int HandleAuto(bool dryRun, bool force)
        {
            Console.WriteLine("======================================================");
            Console.WriteLine("               LOQ Mode - Auto Detection              ");
            Console.WriteLine("======================================================");

            var pwr = PowerManager.GetPowerStatus();
            var saved = StateManager.LoadState();
            string currentProfile = (saved != null) ? saved.ActiveProfile : "UNKNOWN";

            Console.WriteLine(string.Format("Power Source: {0} ({1}%)", pwr.IsOnBattery ? "Battery" : "AC Power", pwr.BatteryLifePercent));
            Console.WriteLine(string.Format("Current Active Profile: {0}", currentProfile));
            Console.WriteLine("------------------------------------------------------");

            if (pwr.IsOnBattery)
            {
                if (currentProfile == "UNI")
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[OK] Laptop is on battery and already configured for Uni Mode.");
                    Console.ResetColor();
                    return 0;
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[RECOMMENDATION] Laptop is on battery! Switching to Uni Mode will maximize runtime.");
                Console.ResetColor();

                return HandleUni(dryRun, force);
            }
            else
            {
                if (currentProfile == "GAMING" || currentProfile == "UNKNOWN")
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[OK] Laptop is plugged into AC power. Gaming / Default performance active.");
                    Console.ResetColor();
                    return 0;
                }

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("[RECOMMENDATION] Laptop is connected to AC power. Restoring Gaming Mode configuration.");
                Console.ResetColor();

                return HandleGaming(dryRun, force);
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("LOQ Mode - Lenovo LOQ Automation Tool v" + Version);
            Console.WriteLine("Automates switching between Uni Mode (power-saving) and Gaming Mode (performance).");
            Console.WriteLine();
            Console.WriteLine("USAGE:");
            Console.WriteLine("  loq-mode <command> [options]");
            Console.WriteLine();
            Console.WriteLine("COMMANDS:");
            Console.WriteLine("  uni         Activate Uni Mode:");
            Console.WriteLine("                * GPU: iGPU-only mode (MUX reboot queued if coming from dGPU)");
            Console.WriteLine("                * Display: 60 Hz refresh rate");
            Console.WriteLine("                * Lenovo Thermal: Quiet Mode (Blue LED)");
            Console.WriteLine("                * Keyboard Backlight: Off");
            Console.WriteLine("                * Windows Power: Best Power Efficiency");
            Console.WriteLine("              (Saves previous baseline state before changing)");
            Console.WriteLine();
            Console.WriteLine("  gaming      Restore Gaming Mode:");
            Console.WriteLine("                * Restores your exact previous saved settings (Refresh rate,");
            Console.WriteLine("                  Lenovo mode, Keyboard light, Windows power, GPU mode).");
            Console.WriteLine();
            Console.WriteLine("  status      Display current hardware, thermal, display, keyboard, and power status.");
            Console.WriteLine();
            Console.WriteLine("  auto        Detect AC / Battery status and recommend or apply appropriate profile.");
            Console.WriteLine();
            Console.WriteLine("OPTIONS:");
            Console.WriteLine("  --reboot, -r Automatically restart the system after applying settings to complete");
            Console.WriteLine("              the hardware MUX switch into iGPU-only mode.");
            Console.WriteLine("  --dry-run   Simulate the command without applying any hardware or OS changes.");
            Console.WriteLine("  --force     Skip confirmation prompts if any.");
            Console.WriteLine("  --no-elevate Do not attempt to auto-elevate (fails if admin is required).");
            Console.WriteLine("  --help, -h  Display this help message.");
            Console.WriteLine("  --version   Display tool version.");
            Console.WriteLine();
            Console.WriteLine("DATA & LOGS:");
            Console.WriteLine("  State file: " + StateManager.StateFilePath);
            Console.WriteLine("  Log file:   " + Logger.LogFilePath);
        }

        static string FormatThermal(int mode)
        {
            switch (mode)
            {
                case 1: return "Quiet";
                case 2: return "Balanced";
                case 3: return "Performance";
                case 255: return "Custom/GodMode";
                default: return "Unknown (" + mode + ")";
            }
        }

        static string FormatKbd(int kbd)
        {
            switch (kbd)
            {
                case 1: return "Off";
                case 2: return "Low";
                case 3: return "High";
                default: return "Unknown (" + kbd + ")";
            }
        }

        static string FormatWinPower(Guid g)
        {
            if (g == PowerManager.BestPowerEfficiency) return "Best Power Efficiency";
            if (g == PowerManager.BestPerformance) return "Best Performance";
            if (g == PowerManager.Balanced) return "Balanced";
            return g.ToString("B");
        }

        #endregion
    }

    #region Lenovo WMI Manager

    static class LenovoWmi
    {
        private static ManagementScope _scope;

        private static ManagementScope GetScope()
        {
            if (_scope == null)
            {
                var options = new ConnectionOptions { EnablePrivileges = true, Impersonation = ImpersonationLevel.Impersonate };
                _scope = new ManagementScope(@"root\wmi", options);
                _scope.Connect();
            }
            return _scope;
        }

        public static string GetModel()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Model, SystemFamily FROM Win32_ComputerSystem"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var fam = mo["SystemFamily"] == null ? null : mo["SystemFamily"].ToString();
                        var mod = mo["Model"] == null ? null : mo["Model"].ToString();
                        return string.IsNullOrEmpty(fam) ? mod : fam + " (" + mod + ")";
                    }
                }
            }
            catch { }
            return "Lenovo LOQ";
        }

        public static string GetBiosVersion()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        return mo["SMBIOSBIOSVersion"] == null ? null : mo["SMBIOSBIOSVersion"].ToString();
                    }
                }
            }
            catch { }
            return "Unknown";
        }

        public static int GetSmartFanMode()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(GetScope(), new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA")))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var outParams = mo.InvokeMethod("GetSmartFanMode", null, null);
                        return Convert.ToInt32(outParams["Data"]);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("GetSmartFanMode failed: " + ex.Message);
            }
            return -1;
        }

        public static void SetSmartFanMode(int mode)
        {
            using (var searcher = new ManagementObjectSearcher(GetScope(), new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA")))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    var inParams = mo.GetMethodParameters("SetSmartFanMode");
                    inParams["Data"] = (uint)mode;
                    mo.InvokeMethod("SetSmartFanMode", inParams, null);
                    return;
                }
            }
            throw new InvalidOperationException("LENOVO_GAMEZONE_DATA instance not found.");
        }

        public static int GetGSyncStatus()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(GetScope(), new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA")))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var outParams = mo.InvokeMethod("GetGSyncStatus", null, null);
                        return Convert.ToInt32(outParams["Data"]);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("GetGSyncStatus failed: " + ex.Message);
            }
            return -1;
        }

        public static void SetGSyncStatus(int status)
        {
            using (var searcher = new ManagementObjectSearcher(GetScope(), new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA")))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    var inParams = mo.GetMethodParameters("SetGSyncStatus");
                    inParams["Data"] = (uint)status;
                    mo.InvokeMethod("SetGSyncStatus", inParams, null);
                    return;
                }
            }
            throw new InvalidOperationException("LENOVO_GAMEZONE_DATA instance not found.");
        }

        public static int GetIGPUModeStatus()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(GetScope(), new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA")))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var outParams = mo.InvokeMethod("GetIGPUModeStatus", null, null);
                        return Convert.ToInt32(outParams["Data"]);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("GetIGPUModeStatus failed: " + ex.Message);
            }
            return -1;
        }

        public static void SetIGPUModeStatus(int mode)
        {
            using (var searcher = new ManagementObjectSearcher(GetScope(), new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA")))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    var inParams = mo.GetMethodParameters("SetIGPUModeStatus");
                    inParams["mode"] = (uint)mode;
                    mo.InvokeMethod("SetIGPUModeStatus", inParams, null);
                    return;
                }
            }
            throw new InvalidOperationException("LENOVO_GAMEZONE_DATA instance not found.");
        }

        public static void NotifyDGPUStatus(int status)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(GetScope(), new ObjectQuery("SELECT * FROM LENOVO_GAMEZONE_DATA")))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var inParams = mo.GetMethodParameters("NotifyDGPUStatus");
                        inParams["status"] = (uint)status;
                        mo.InvokeMethod("NotifyDGPUStatus", inParams, null);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("NotifyDGPUStatus failed: " + ex.Message);
            }
        }

        public static int GetKeyboardBacklight()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(GetScope(), new ObjectQuery("SELECT * FROM LENOVO_LIGHTING_METHOD")))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        var inParams = mo.GetMethodParameters("Get_Lighting_Current_Status");
                        inParams["Lighting_ID"] = (uint)0;
                        var outParams = mo.InvokeMethod("Get_Lighting_Current_Status", inParams, null);
                        return Convert.ToInt32(outParams["Current_Brightness_Level"]);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("GetKeyboardBacklight failed: " + ex.Message);
            }
            return -1;
        }

        public static void SetKeyboardBacklight(int level)
        {
            using (var searcher = new ManagementObjectSearcher(GetScope(), new ObjectQuery("SELECT * FROM LENOVO_LIGHTING_METHOD")))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    var inParams = mo.GetMethodParameters("Set_Lighting_Current_Status");
                    inParams["Lighting_ID"] = (uint)0;
                    inParams["Current_State_Type"] = (uint)0;
                    inParams["Current_Brightness_Level"] = (uint)level;
                    mo.InvokeMethod("Set_Lighting_Current_Status", inParams, null);
                    return;
                }
            }
            throw new InvalidOperationException("LENOVO_LIGHTING_METHOD instance not found.");
        }
    }

    #endregion

    #region Display Manager

    static class DisplayManager
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessWindowStation(IntPtr hWinSta);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetThreadDesktop(IntPtr hDesktop);

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray, ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(uint numPathArrayElements, [In] DISPLAYCONFIG_PATH_INFO[] pathInfoArray, uint numModeInfoArrayElements, [In] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int DISP_CHANGE_SUCCESSFUL = 0;
        private const uint CDS_UPDATEREGISTRY = 0x01;
        private const int DM_DISPLAYFREQUENCY = 0x400000;

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
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            public bool targetAvailable;
            public uint statusFlags;
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
            public uint width;
            public uint height;
            public uint pixelFormat;
            public POINTL position;
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
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DEVMODE
        {
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

        public static string GetPrimaryDeviceName()
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
            uint devNum = 0;
            while (EnumDisplayDevices(null, devNum, ref dd, 0))
            {
                if ((dd.StateFlags & 1) != 0 && (dd.StateFlags & 4) != 0) // Attached to Desktop & Primary Device
                {
                    return dd.DeviceName;
                }
                devNum++;
            }
            return @"\\.\DISPLAY1";
        }

        public static int GetCurrentRefreshRate()
        {
            EnsureDesktopAccess();
            try
            {
                uint pathCount = 0, modeCount = 0;
                int bufErr = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);
                if (bufErr == 0 && pathCount > 0)
                {
                    var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                    var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                    int qErr = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                    if (qErr == 0 && paths[0].targetInfo.refreshRate.Denominator > 0)
                    {
                        double hz = (double)paths[0].targetInfo.refreshRate.Numerator / paths[0].targetInfo.refreshRate.Denominator;
                        return (int)Math.Round(hz);
                    }
                }
            }
            catch { }

            string device = GetPrimaryDeviceName();
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
            if (EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref dm))
            {
                return dm.dmDisplayFrequency;
            }
            return 0;
        }

        public static List<int> GetAvailableRefreshRates()
        {
            string device = GetPrimaryDeviceName();
            var current = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
            var set = new SortedSet<int>();
            if (EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref current))
            {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                int modeNum = 0;
                while (EnumDisplaySettings(device, modeNum, ref dm))
                {
                    if (dm.dmPelsWidth == current.dmPelsWidth && dm.dmPelsHeight == current.dmPelsHeight)
                    {
                        set.Add(dm.dmDisplayFrequency);
                    }
                    modeNum++;
                }
            }
            return new List<int>(set);
        }

        public static bool SetRefreshRate(int hz)
        {
            int cur = GetCurrentRefreshRate();
            if (cur == hz) return true;

            EnsureDesktopAccess();

            // First attempt: Windows CCD API (QueryDisplayConfig / SetDisplayConfig)
            try
            {
                uint pathCount = 0, modeCount = 0;
                int bufErr = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);
                if (bufErr == 0 && pathCount > 0)
                {
                    var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                    var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                    int qErr = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                    if (qErr == 0)
                    {
                        paths[0].targetInfo.refreshRate.Numerator = (uint)(hz * 1000);
                        paths[0].targetInfo.refreshRate.Denominator = 1000;

                        uint tgtIdx = paths[0].targetInfo.modeInfoIdx;
                        if (tgtIdx < modeCount && modes[tgtIdx].infoType == 2)
                        {
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
                        if (sErr == 0)
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }

            // Second attempt: Fallback to ChangeDisplaySettingsEx
            try
            {
                string device = GetPrimaryDeviceName();
                var current = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                if (EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref current))
                {
                    var target = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                    int modeNum = 0;
                    while (EnumDisplaySettings(device, modeNum, ref target))
                    {
                        if (target.dmPelsWidth == current.dmPelsWidth &&
                            target.dmPelsHeight == current.dmPelsHeight &&
                            target.dmDisplayFrequency == hz)
                        {
                            target.dmFields = DM_DISPLAYFREQUENCY;
                            int res = ChangeDisplaySettingsEx(device, ref target, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
                            if (res == DISP_CHANGE_SUCCESSFUL) return true;
                            break;
                        }
                        modeNum++;
                    }
                }
            }
            catch { }

            return false;
        }
    }

    #endregion

    #region Power Manager

    static class PowerManager
    {
        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveOverlayScheme(Guid OverlaySchemeGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetEffectiveOverlayScheme(out Guid EffectiveOverlaySchemeGuid);

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        public class PowerInfo
        {
            public bool IsOnBattery;
            public int BatteryLifePercent;
        }

        public static readonly Guid BestPowerEfficiency = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        public static readonly Guid BestPerformance = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");
        public static readonly Guid Balanced = Guid.Empty;

        public static PowerInfo GetPowerStatus()
        {
            var info = new PowerInfo { IsOnBattery = false, BatteryLifePercent = 100 };
            SYSTEM_POWER_STATUS status;
            if (GetSystemPowerStatus(out status))
            {
                info.IsOnBattery = (status.ACLineStatus == 0);
                info.BatteryLifePercent = (status.BatteryLifePercent <= 100) ? status.BatteryLifePercent : 100;
            }
            return info;
        }

        public static Guid GetActiveOverlayScheme()
        {
            Guid guid;
            if (PowerGetEffectiveOverlayScheme(out guid) == 0)
                return guid;

            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes"))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("ActiveOverlayAcPowerScheme");
                        if (val != null && !string.IsNullOrEmpty(val.ToString()))
                            return new Guid(val.ToString());
                    }
                }
            }
            catch { }

            return Balanced;
        }

        public static void SetActiveOverlayScheme(Guid scheme)
        {
            uint res = PowerSetActiveOverlayScheme(scheme);
            if (res != 0)
            {
                Logger.Warn("PowerSetActiveOverlayScheme returned code " + res);
            }

            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", true))
                {
                    if (key != null)
                    {
                        key.SetValue("ActiveOverlayAcPowerScheme", scheme.ToString());
                        key.SetValue("ActiveOverlayDcPowerScheme", scheme.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not update registry PowerScheme overlay: " + ex.Message);
            }
        }
    }

    #endregion

    #region State & Logging

    public class SystemState
    {
        public string Timestamp { get; set; }
        public int GSyncStatus { get; set; }
        public int IGPUModeStatus { get; set; }
        public int RefreshRate { get; set; }
        public int ThermalMode { get; set; }
        public int KeyboardLight { get; set; }
        public string WindowsPowerMode { get; set; }
        public string ActiveProfile { get; set; }
    }

    static class StateManager
    {
        public static string AppDataDir
        {
            get
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LOQMode");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string StateFilePath
        {
            get { return Path.Combine(AppDataDir, "state.json"); }
        }

        public static void SaveState(SystemState state)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"timestamp\": \"" + Escape(state.Timestamp) + "\",");
            sb.AppendLine("  \"gSyncStatus\": " + state.GSyncStatus + ",");
            sb.AppendLine("  \"igpuModeStatus\": " + state.IGPUModeStatus + ",");
            sb.AppendLine("  \"refreshRate\": " + state.RefreshRate + ",");
            sb.AppendLine("  \"thermalMode\": " + state.ThermalMode + ",");
            sb.AppendLine("  \"keyboardLight\": " + state.KeyboardLight + ",");
            sb.AppendLine("  \"windowsPowerMode\": \"" + Escape(state.WindowsPowerMode) + "\",");
            sb.AppendLine("  \"activeProfile\": \"" + Escape(state.ActiveProfile) + "\"");
            sb.AppendLine("}");

            File.WriteAllText(StateFilePath, sb.ToString(), Encoding.UTF8);
        }

        public static SystemState LoadState()
        {
            if (!File.Exists(StateFilePath))
                return null;

            try
            {
                string json = File.ReadAllText(StateFilePath, Encoding.UTF8);
                var state = new SystemState();
                state.Timestamp = ExtractString(json, "timestamp");
                state.GSyncStatus = ExtractInt(json, "gSyncStatus", 0);
                state.IGPUModeStatus = ExtractInt(json, "igpuModeStatus", 0);
                state.RefreshRate = ExtractInt(json, "refreshRate", 60);
                state.ThermalMode = ExtractInt(json, "thermalMode", 2);
                state.KeyboardLight = ExtractInt(json, "keyboardLight", 1);
                state.WindowsPowerMode = ExtractString(json, "windowsPowerMode");
                state.ActiveProfile = ExtractString(json, "activeProfile");

                if (string.IsNullOrEmpty(state.Timestamp))
                    return null;

                return state;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to parse state.json: " + ex.Message);
                return null;
            }
        }

        private static string Escape(string val)
        {
            if (val == null) return "";
            return val.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string ExtractString(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return "";
            int colon = json.IndexOf(':', idx + pattern.Length);
            if (colon < 0) return "";
            int quoteStart = json.IndexOf('\"', colon);
            if (quoteStart < 0) return "";
            int quoteEnd = json.IndexOf('\"', quoteStart + 1);
            if (quoteEnd < 0) return "";
            return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        private static int ExtractInt(string json, string key, int defaultVal)
        {
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return defaultVal;
            int colon = json.IndexOf(':', idx + pattern.Length);
            if (colon < 0) return defaultVal;
            int comma = json.IndexOfAny(new[] { ',', '}', '\r', '\n' }, colon + 1);
            if (comma < 0) comma = json.Length;
            string numStr = json.Substring(colon + 1, comma - colon - 1).Trim();
            int val;
            if (int.TryParse(numStr, out val))
                return val;
            return defaultVal;
        }
    }

    static class Logger
    {
        public static string LogFilePath
        {
            get { return Path.Combine(StateManager.AppDataDir, "loq-mode.log"); }
        }

        public static void Init()
        {
            try
            {
                if (!File.Exists(LogFilePath))
                {
                    File.WriteAllText(LogFilePath, "=== LOQ Mode Log Initialized " + DateTime.Now.ToString("s") + " ===\r\n", Encoding.UTF8);
                }
            }
            catch { }
        }

        public static void Info(string message) { Log("INFO", message); }
        public static void Warn(string message) { Log("WARN", message); }
        public static void Error(string message) { Log("ERROR", message); }

        private static void Log(string level, string message)
        {
            try
            {
                string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}\r\n", DateTime.Now, level, message);
                File.AppendAllText(LogFilePath, line, Encoding.UTF8);
            }
            catch { }
        }
    }

    #endregion
}
