using System.Collections.Generic;
using Fdp.Core.Threading;
using Xunit;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ N-14 (CE-4476; §19) — the Windows scheduling start-up helper, with the Win32 calls faked: the process opts out of power throttling with
    /// EXACTLY execution speed + ignore timer resolution (once), a thread with execution speed; <c>HROT_ECOQOS=1</c> keeps Windows' default (no
    /// call); off Windows the real API is the no-op one.
    /// 🔴 Red-proof (run): <c>OptOutProcess</c> passing only <c>ProcessExecutionSpeed</c> ⇒ the mask assertion fails.
    /// </summary>
    [Collection("WindowsScheduling")]   // the helper's state is static
    public class WindowsSchedulingTests
    {
        private sealed class FakeApi : WindowsScheduling.IApi
        {
            public readonly List<(string, uint, uint)> Calls = new();
            public bool SetProcessPowerThrottling(uint c, uint s) { Calls.Add(("process", c, s)); return true; }
            public bool SetCurrentThreadPowerThrottling(uint c, uint s) { Calls.Add(("thread", c, s)); return true; }
            public bool ClearConsoleQuickEdit() { Calls.Add(("quickedit", 0, 0)); return true; }
        }

        [Fact]
        public void TheProcessAndAThread_OptOutWithTheExactMasks_TheProcessOnce()
        {
            var keep = WindowsScheduling.KeepWindowsDefault; var api = WindowsScheduling.Api;
            try
            {
                var fake = new FakeApi();
                WindowsScheduling.Api = fake; WindowsScheduling.KeepWindowsDefault = false; WindowsScheduling.ResetForTest();
                Assert.True(WindowsScheduling.OptOutProcess());
                Assert.False(WindowsScheduling.OptOutProcess());   // once
                Assert.True(WindowsScheduling.OptOutCurrentThread());
                Assert.True(WindowsScheduling.DisableConsoleQuickEdit());
                Assert.Equal(new[] { ("process", 0x1u | 0x4u, 0u), ("thread", 0x1u, 0u), ("quickedit", 0u, 0u) }, fake.Calls);
            }
            finally { WindowsScheduling.Api = api; WindowsScheduling.KeepWindowsDefault = keep; WindowsScheduling.ResetForTest(); }
        }

        [Fact]
        public void KeepingWindowsDefault_MakesNoThrottlingCall()
        {
            var keep = WindowsScheduling.KeepWindowsDefault; var api = WindowsScheduling.Api;
            try
            {
                var fake = new FakeApi();
                WindowsScheduling.Api = fake; WindowsScheduling.KeepWindowsDefault = true; WindowsScheduling.ResetForTest();
                Assert.False(WindowsScheduling.OptOutProcess());
                Assert.False(WindowsScheduling.OptOutCurrentThread());
                Assert.Empty(fake.Calls);
            }
            finally { WindowsScheduling.Api = api; WindowsScheduling.KeepWindowsDefault = keep; WindowsScheduling.ResetForTest(); }
        }

        [Fact]
        public void OffWindows_TheRealApiIsTheNoOp()
        {
            if (System.OperatingSystem.IsWindows()) return;
            Assert.IsType<WindowsScheduling.NoOpApi>(WindowsScheduling.Api);
        }
    }
}
