using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Tests.Utility
{
    /// <summary>
    /// Tests for <see cref="UtilityAutoDiscovery"/>.
    /// Success criteria: SC-P2-04-1, SC-P2-04-2, plus a negative test.
    /// </summary>
    public class UtilityAutoDiscoveryTests
    {
        // ── SC-P2-04-1: ScanAndRegister invokes [UtilityRegistrar] class in the current assembly ──

        // Flag set by the registrar below.
        private static bool s_scanInvokeFlag = false;

        [UtilityRegistrar]
        public static class TestRegistrar_ScanInvoke
        {
            public static void RegisterAll() { s_scanInvokeFlag = true; }
        }

        [Fact]
        public void ScanAndRegister_InvokesRegistrarInCurrentAssembly()
        {
            s_scanInvokeFlag = false;
            UtilityAutoDiscovery.ResetForTesting();

            UtilityAutoDiscovery.ScanAndRegister();

            Assert.True(s_scanInvokeFlag);
        }

        // ── SC-P2-04-2: Second call does not re-invoke RegisterAll ──

        private static int s_scanCallCount = 0;

        [UtilityRegistrar]
        public static class TestRegistrar_CallCount
        {
            public static void RegisterAll() { s_scanCallCount++; }
        }

        [Fact]
        public void ScanAndRegister_SecondCallDoesNotReinvoke()
        {
            s_scanCallCount = 0;
            UtilityAutoDiscovery.ResetForTesting();

            UtilityAutoDiscovery.ScanAndRegister();
            UtilityAutoDiscovery.ScanAndRegister();

            // RegisterAll from each [UtilityRegistrar] in the assembly is called exactly once.
            // Since multiple [UtilityRegistrar] classes may exist (including production ones
            // loaded by this test process), we only verify the call counter has not incremented
            // a second time by checking the total stays at 1 per single scan cycle.
            Assert.Equal(1, s_scanCallCount);
        }

        // ── Negative: class without [UtilityRegistrar] is not invoked ──

        private static bool s_noAttrFlag = false;

        // No [UtilityRegistrar] on purpose.
        public static class TestRegistrar_NoAttribute
        {
            public static void RegisterAll() { s_noAttrFlag = true; }
        }

        [Fact]
        public void ScanAndRegister_IgnoresClassesWithoutAttribute()
        {
            s_noAttrFlag = false;
            UtilityAutoDiscovery.ResetForTesting();

            UtilityAutoDiscovery.ScanAndRegister();

            Assert.False(s_noAttrFlag);
        }

        // ── SC-P2-04-3: ScanAndRegisterDecisions invokes decision registrar ──

        private static bool s_decisionScanFlag = false;

        [UtilityRegistrar]
        public static class TestRegistrar_DecisionScan
        {
            public static void RegisterAll(out UtilityRegistry registry)
            {
                s_decisionScanFlag = true;
                registry = new UtilityRegistry();
            }
        }

        [Fact]
        public void ScanAndRegisterDecisions_InvokesDecisionRegistrar()
        {
            s_decisionScanFlag = false;
            UtilityAutoDiscovery.ResetDecisionsForTesting();

            UtilityAutoDiscovery.ScanAndRegisterDecisions(out var registry);

            Assert.True(s_decisionScanFlag);
            Assert.NotNull(registry);
        }

        // ── SC-P2-04-4: Second call to ScanAndRegisterDecisions does not re-invoke ──

        private static int s_decisionCallCount = 0;

        [UtilityRegistrar]
        public static class TestRegistrar_DecisionCallCount
        {
            public static void RegisterAll(out UtilityRegistry registry)
            {
                s_decisionCallCount++;
                registry = new UtilityRegistry();
            }
        }

        [Fact]
        public void ScanAndRegisterDecisions_SecondCallDoesNotReinvoke()
        {
            s_decisionCallCount = 0;
            UtilityAutoDiscovery.ResetDecisionsForTesting();

            UtilityAutoDiscovery.ScanAndRegisterDecisions(out _);
            UtilityAutoDiscovery.ScanAndRegisterDecisions(out _);

            // RegisterAll(out) from each [UtilityRegistrar] in the assembly is called at most once.
            // Verify this registrar's counter did not increment a second time.
            Assert.Equal(1, s_decisionCallCount);
        }
    }
}
