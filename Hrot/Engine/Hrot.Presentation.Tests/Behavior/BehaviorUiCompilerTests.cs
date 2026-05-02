using System.Text.Json;
using Fdp.Toolkit.Behavior.Params;
using Hrot.Presentation.Behavior;
using Xunit;

namespace Hrot.Presentation.Tests.Behavior
{
    /// <summary>Tests for TASK-C009: BehaviorUiCompiler and BehaviorUiRegistry.</summary>
    public sealed class BehaviorUiCompilerTests
    {
        // Null implementation of IPickInteractionContext for tests that do not exercise pick flow.
        private sealed class NullPickContext : IPickInteractionContext
        {
            public bool IsPickPendingFor(int taskIndex, string propertyName) => false;
            public bool TryConsumeEntityPick(int taskIndex, string propertyName, out long entityId)
            { entityId = 0; return false; }
            public bool TryConsumeLocationPick(int taskIndex, string propertyName, out PickableGeoPoint location)
            { location = default; return false; }
            public void RequestEntityPick(int taskIndex, string propertyName, string[]? filterPresets) { }
            public void RequestLocationPick(int taskIndex, string propertyName) { }
        }

        // ── C009 SC1: Compile<T> returns non-null delegate ────────────────────

        /// <summary>C009 SC1: Compile returns a non-null draw delegate.</summary>
        [Fact]
        public void C009_Compile_ReturnsNonNullDelegate()
        {
            var drawDelegate = BehaviorUiCompiler.Compile<FireAtTargetParamsJsonDto>();
            Assert.NotNull(drawDelegate);
        }

        // ── C009 SC2: Compile increments CompileCallCount (expression-tree path taken) ──

        /// <summary>
        /// C009 SC2: CompileCallCount is incremented once per unique DTO type,
        /// confirming that the expression-tree compilation path is taken (not
        /// PropertyInfo.GetValue/SetValue) and that results are cached.
        /// </summary>
        [Fact]
        public void C009_CompileCallCount_IncrementedOnce_PerType()
        {
            // Use a private DTO not previously compiled to get a guaranteed cache miss.
            int before = BehaviorUiCompiler.CompileCallCount;

            var d1 = BehaviorUiCompiler.Compile<CompilerProbeDto>();
            var d2 = BehaviorUiCompiler.Compile<CompilerProbeDto>();
            var d3 = BehaviorUiCompiler.Compile<CompilerProbeDto>();

            // Count must have increased by at least 1 (compilation happened).
            // It may exceed before+1 if other test classes compiled unrelated types in
            // parallel; the reference-equality assertions below confirm per-type caching.
            Assert.True(BehaviorUiCompiler.CompileCallCount >= before + 1,
                "CompileCallCount should increase by at least 1 for the first Compile<CompilerProbeDto>");
            Assert.True(ReferenceEquals(d1, d2), "d2 must return cached delegate from d1");
            Assert.True(ReferenceEquals(d1, d3), "d3 must return cached delegate from d1");
        }

        // ── C009 SC3: TestHook_ApplyChange verifies JSON round-trip ──────────

        /// <summary>C009 SC3: JSON round-trip — updated value is reflected, other fields preserved.</summary>
        [Fact]
        public void C009_TestHookApplyChange_JsonRoundTrip_UpdatesCorrectField()
        {
            const string json =
                "{\"targetNetworkId\":42,\"maxRounds\":5,\"cooldownSeconds\":1.0}";

            string result = BehaviorUiCompiler.TestHook_ApplyChange<FireAtTargetParamsJsonDto>(
                json,
                dto => dto.CooldownSeconds = 2.5f);

            Assert.NotNull(result);
            Assert.Contains("\"cooldownSeconds\":2.5", result);
            Assert.Contains("\"targetNetworkId\":42", result);
            Assert.Contains("\"maxRounds\":5", result);
        }

        // ── C009 SC4: No ImGui context -> same JSON reference returned ────────

        /// <summary>
        /// C009 SC4: When no ImGui context is present (normal test environment)
        /// the delegate returns the original JSON string reference — no allocation.
        /// </summary>
        [Fact]
        public void C009_DelegateWithNoImGuiContext_ReturnsSameReference()
        {
            var drawDelegate = BehaviorUiCompiler.Compile<FireAtTargetParamsJsonDto>();
            const string json = "{\"targetNetworkId\":42,\"maxRounds\":5,\"cooldownSeconds\":1.0}";
            var context      = new NullPickContext();

            string result = drawDelegate(json, 0, context);

            Assert.Same(json, result);
        }

        // ── BehaviorUiRegistry tests ──────────────────────────────────────────

        /// <summary>BehaviorUiRegistry: registered delegate is retrievable.</summary>
        [Fact]
        public void BehaviorUiRegistry_RegisterAndTryGet_ReturnsDelegate()
        {
            var registry = new BehaviorUiRegistry();
            registry.Register<FireAtTargetParamsJsonDto>(Hrot.Map.Definitions.Behavior.FireAtTargetParamsJsonDto.BehaviorId);

            bool found = registry.TryGet(Hrot.Map.Definitions.Behavior.FireAtTargetParamsJsonDto.BehaviorId, out var drawDelegate);

            Assert.True(found);
            Assert.NotNull(drawDelegate);
        }

        /// <summary>BehaviorUiRegistry: unknown behavior ID returns false.</summary>
        [Fact]
        public void BehaviorUiRegistry_TryGet_UnknownId_ReturnsFalse()
        {
            var registry = new BehaviorUiRegistry();

            bool found = registry.TryGet("UnknownBehavior", out var drawDelegate);

            Assert.False(found);
            Assert.Null(drawDelegate);
        }

        // Private DTO used only for caching test to guarantee a fresh cache entry.
        private class CompilerProbeDto
        {
            public float Value { get; set; }
        }

        // ── PickableGeoPoint compilation test ─────────────────────────────────

        /// <summary>
        /// Compile&lt;MoveToLocationParamsJsonDto&gt; succeeds after the PickableLocation
        /// facade was introduced, producing a non-null cached delegate.
        /// </summary>
        [Fact]
        public void C009_Compile_MoveToLocationDto_WithPickableGeoPoint_Succeeds()
        {
            var drawDelegate = BehaviorUiCompiler.Compile<MoveToLocationParamsJsonDto>();
            Assert.NotNull(drawDelegate);
        }

        /// <summary>
        /// Invoking the MoveToLocation compiled delegate without an ImGui context (test env)
        /// returns the same JSON reference — no allocation.
        /// </summary>
        [Fact]
        public void C009_MoveToLocationDelegate_NoImGuiContext_ReturnsSameReference()
        {
            var drawDelegate = BehaviorUiCompiler.Compile<MoveToLocationParamsJsonDto>();
            const string json = "{\"targetLat\":52.5,\"targetLon\":13.4,\"speed\":5.0,\"arrivalRadius\":10.0}";
            var context      = new NullPickContext();

            string result = drawDelegate(json, 0, context);

            Assert.Same(json, result);
        }
    }
}
