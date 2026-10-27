// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FluentFlyout.Classes;

/// <summary>
/// Opts this process out of Windows power throttling (EcoQoS / "Efficiency mode").
/// When Battery Saver is on, Windows can slow down background processes and coarsen their timers,
/// which delays the taskbar widget updates, auto-hide timers and animations.
/// </summary>
/// <remarks>
/// This does NOT bring back acrylic transparency: when Battery Saver or "Transparency effects = off"
/// is active, Windows itself replaces acrylic with a solid color and no app can override that.
/// </remarks>
public static partial class PowerThrottlingHelper
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // PROCESS_INFORMATION_CLASS.ProcessPowerThrottling
    private const int ProcessPowerThrottling = 4;

    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;

    // Execution speed throttling (EcoQoS)
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    // Timer resolution throttling (Windows 11+, ignored on older builds)
    private const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessPowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(
        IntPtr hProcess,
        int processInformationClass,
        ref ProcessPowerThrottlingState processInformation,
        uint processInformationSize);

    /// <summary>
    /// Explicitly disables execution-speed and timer-resolution throttling for the current process.
    /// Safe to call on any Windows version; failures are logged and ignored.
    /// </summary>
    public static void DisableThrottling()
    {
        try
        {
            // ControlMask = which policies we are taking control of,
            // StateMask   = 0 means "do not throttle" for those policies.
            var state = new ProcessPowerThrottlingState
            {
                Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
                StateMask = 0
            };

            using var process = Process.GetCurrentProcess();
            bool ok = SetProcessInformation(
                process.Handle,
                ProcessPowerThrottling,
                ref state,
                (uint)Marshal.SizeOf<ProcessPowerThrottlingState>());

            if (!ok)
            {
                // Older Windows builds may reject the timer-resolution bit; retry with execution speed only.
                state.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
                ok = SetProcessInformation(
                    process.Handle,
                    ProcessPowerThrottling,
                    ref state,
                    (uint)Marshal.SizeOf<ProcessPowerThrottlingState>());
            }

            if (ok)
                Logger.Info("Power throttling disabled for this process");
            else
                Logger.Warn("Could not disable power throttling (Win32 error {Error})", Marshal.GetLastWin32Error());
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to disable power throttling");
        }
    }
}
