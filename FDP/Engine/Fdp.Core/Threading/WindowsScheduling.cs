using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Fdp.Core.Threading
{
    /// <summary>
    /// ⭐ N-14 (CE-4476; CE-4452 / CE-4458 W-2 + W-5; 📄 docs/DESIGN_Parallel_Simulation.md §19) — the ONE start-up helper for how Windows schedules
    /// an HROT process. Measured (CE-4453, Windows): a node with no visible window is put into EcoQoS by default and runs at about HALF speed (clock
    /// ~2.6 vs ~3.8–4.1 GHz; hidden CGF 228 → 121 ms, SimHost 101 → 44 ms with the opt-out).
    /// <list type="bullet">
    /// <item><see cref="OptOutProcess"/> — the PROCESS opts out of power throttling: execution speed + timer resolution (called once at start-up,
    /// by the runner's <c>Main</c> — the one entry every HROT host passes).</item>
    /// <item><see cref="OptOutCurrentThread"/> — the same per THREAD (the simulation thread, every worker-pool thread), at thread start.</item>
    /// <item><see cref="DisableConsoleQuickEdit"/> — a click in a console window otherwise freezes every write to it (and a node that logs with it).</item>
    /// </list>
    /// ⭐ Linux / macOS: a no-op (the API is <see cref="NoOpApi"/>). <c>HROT_ECOQOS=1</c> keeps Windows' default (for the comparison run).
    /// The Win32 calls sit behind <see cref="IApi"/> so a rail checks the exact masks without Windows. Every call happens at start-up / thread
    /// creation only (0 B per frame).
    /// </summary>
    public static class WindowsScheduling
    {
        // PROCESS_POWER_THROTTLING_STATE / THREAD_POWER_THROTTLING_STATE masks (processthreadsapi.h)
        public const uint ProcessExecutionSpeed = 0x1, ProcessIgnoreTimerResolution = 0x4, ThreadExecutionSpeed = 0x1;

        /// <summary>The calls (faked by the rail).</summary>
        public interface IApi
        {
            bool SetProcessPowerThrottling(uint controlMask, uint stateMask);
            bool SetCurrentThreadPowerThrottling(uint controlMask, uint stateMask);
            bool ClearConsoleQuickEdit();
        }

        /// <summary>Windows' default kept (<c>HROT_ECOQOS=1</c>): nothing is opted out — the comparison run.</summary>
        public static bool KeepWindowsDefault { get; set; } = Environment.GetEnvironmentVariable("HROT_ECOQOS") == "1";

        /// <summary>The API in use: Win32 on Windows, a no-op elsewhere. Tests replace it.</summary>
        public static IApi Api { get; set; } = OperatingSystem.IsWindows() ? new Win32Api() : new NoOpApi();

        private static int s_processDone;

        /// <summary>Opts the process out of power throttling (execution speed and timer resolution) — once. Returns whether the call was made and succeeded.</summary>
        public static bool OptOutProcess()
        {
            if (KeepWindowsDefault || Interlocked.Exchange(ref s_processDone, 1) == 1) return false;
            return Api.SetProcessPowerThrottling(ProcessExecutionSpeed | ProcessIgnoreTimerResolution, stateMask: 0);
        }

        /// <summary>Opts the CALLING thread out of power throttling (execution speed). Call at the start of the simulation thread and each pool thread.</summary>
        public static bool OptOutCurrentThread()
            => !KeepWindowsDefault && Api.SetCurrentThreadPowerThrottling(ThreadExecutionSpeed, stateMask: 0);

        /// <summary>Turns QuickEdit off on the process's console (a no-op without a console).</summary>
        public static bool DisableConsoleQuickEdit() => Api.ClearConsoleQuickEdit();

        /// <summary>
        /// ⭐ F-5 (CE-4505) — the one start-up line saying what the opt-out DID (the runner's <c>Main</c> logs it once): opted out, failed, the
        /// default kept (<c>HROT_ECOQOS=1</c>), or not Windows. 📌 The Windows run verified the masks by hand (ControlMask 5 / StateMask 0) because
        /// the result was ignored.
        /// </summary>
        public static string Describe(bool processOptedOut, bool quickEditOff, bool isWindows)
        {
            string throttling = !isWindows ? "not Windows (nothing to do)"
                              : KeepWindowsDefault ? "Windows' default kept (HROT_ECOQOS=1)"
                              : processOptedOut ? "opted out (execution speed + timer resolution)"
                              : "the call FAILED — the process may run throttled";
            string quickEdit = !isWindows ? "-" : quickEditOff ? "off" : "unchanged (no console, or the call failed)";
            return $"[Scheduling] power throttling: {throttling} · console QuickEdit: {quickEdit}";
        }

        /// <summary>Tests: forget that the process was opted out.</summary>
        public static void ResetForTest() => Interlocked.Exchange(ref s_processDone, 0);

        /// <summary>Linux / macOS: nothing to do.</summary>
        public sealed class NoOpApi : IApi
        {
            public bool SetProcessPowerThrottling(uint controlMask, uint stateMask) => false;
            public bool SetCurrentThreadPowerThrottling(uint controlMask, uint stateMask) => false;
            public bool ClearConsoleQuickEdit() => false;
        }

        private sealed class Win32Api : IApi
        {
            [StructLayout(LayoutKind.Sequential)]
            private struct PowerThrottlingState { public uint Version; public uint ControlMask; public uint StateMask; }

            private const int ProcessPowerThrottling = 4, ThreadPowerThrottling = 3;
            private const int StdInputHandle = -10;
            private const uint EnableQuickEditMode = 0x40, EnableExtendedFlags = 0x80;

            [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetCurrentProcess();
            [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetCurrentThread();
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, uint size);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetThreadInformation(IntPtr thread, int infoClass, ref PowerThrottlingState info, uint size);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetStdHandle(int handle);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetConsoleMode(IntPtr handle, uint mode);

            public bool SetProcessPowerThrottling(uint controlMask, uint stateMask)
            {
                var st = new PowerThrottlingState { Version = 1, ControlMask = controlMask, StateMask = stateMask };
                try { return SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref st, (uint)Marshal.SizeOf<PowerThrottlingState>()); }
                catch (EntryPointNotFoundException) { return false; }   // older Windows: nothing to opt out of
            }

            public bool SetCurrentThreadPowerThrottling(uint controlMask, uint stateMask)
            {
                var st = new PowerThrottlingState { Version = 1, ControlMask = controlMask, StateMask = stateMask };
                try { return SetThreadInformation(GetCurrentThread(), ThreadPowerThrottling, ref st, (uint)Marshal.SizeOf<PowerThrottlingState>()); }
                catch (EntryPointNotFoundException) { return false; }
            }

            public bool ClearConsoleQuickEdit()
            {
                var h = GetStdHandle(StdInputHandle);
                if (h == IntPtr.Zero || h == new IntPtr(-1) || !GetConsoleMode(h, out uint mode)) return false;   // no console
                return SetConsoleMode(h, (mode & ~EnableQuickEditMode) | EnableExtendedFlags);
            }
        }
    }
}
