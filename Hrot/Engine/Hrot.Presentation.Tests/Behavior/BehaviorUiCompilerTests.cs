using System.Text.Json;
using Fdp.Toolkit.Behavior.Params;
using Hrot.Map.Definitions.Behavior;
using Hrot.Presentation.Behavior;
using Xunit;

namespace Hrot.Presentation.Tests.Behavior
{
    /// <summary>Tests for TASK-C009: BehaviorUiCompiler and BehaviorUiRegistry.</summary>
    public sealed class BehaviorUiCompilerTests
    {
        // Null pick context for tests that do not exercise the pick flow.
        private sealed class NullPickContext : Fdp.Presentation.Editing.IComponentPickerContext
        {
            public bool IsPickPendingFor(string path) => false;
            public bool TryConsumeEntityPick(string path, out Fdp.Toolkit.Replication.EntityRef picked)
            { picked = default; return false; }
            public bool TryConsumeLocationPick(string path, out PickableGeoPoint location)
            { location = default; return false; }
            public void RequestEntityPick(string path, string[]? filterPresets) { }
            public void RequestLocationPick(string path) { }
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

        // ── CE-2023 ③ (S8n): the contracts are STRUCTS — a pick must land in the JSON, not in a copy ──

        private sealed class PickingContext : Fdp.Presentation.Editing.IComponentPickerContext
        {
            public long Entity;
            public PickableGeoPoint? Location;
            public readonly System.Collections.Generic.List<string> AskedPaths = new();
            public bool IsPickPendingFor(string path) => false;
            public bool TryConsumeEntityPick(string path, out Fdp.Toolkit.Replication.EntityRef picked)
            { AskedPaths.Add(path); picked = new Fdp.Toolkit.Replication.EntityRef(Entity); return Entity != 0; }
            public bool TryConsumeLocationPick(string path, out PickableGeoPoint location)
            { AskedPaths.Add(path); location = Location ?? default; return Location.HasValue; }
            public void RequestEntityPick(string path, string[]? filterPresets) { }
            public void RequestLocationPick(string path) { }
        }

        private static string DrawOneFrame(BehaviorUiDrawDelegate draw, string json, Fdp.Presentation.Editing.IComponentPickerContext pick)
        {
            var ctx = ImGuiNET.ImGui.CreateContext();
            ImGuiNET.ImGui.SetCurrentContext(ctx);
            var io = ImGuiNET.ImGui.GetIO();
            io.DisplaySize = new System.Numerics.Vector2(1024, 768);
            io.DeltaTime   = 1.0f / 60.0f;
            io.Fonts.AddFontDefault();
            io.Fonts.Build();
            try
            {
                ImGuiNET.ImGui.NewFrame();
                ImGuiNET.ImGui.Begin("CE2023");
                string result = draw(json, 0, pick);
                ImGuiNET.ImGui.End();
                ImGuiNET.ImGui.Render();
                return result;
            }
            finally { ImGuiNET.ImGui.DestroyContext(ctx); }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2023</c> ③ — an entity pick on the (now struct) <c>FireAtTarget</c> contract is written into the JSON the
        /// mission panel stores. 🔴 With a by-value setter the pick lands in a COPY and the old id comes back.
        /// </summary>
        [Fact]
        public void CE2023_AnEntityPickOnAStructContract_LandsInTheJson()
        {
            Assert.True(typeof(FireAtTargetParamsJsonDto).IsValueType);
            var draw = BehaviorUiCompiler.Compile<FireAtTargetParamsJsonDto>();

            var ctx = new PickingContext { Entity = 99 };
            string result = DrawOneFrame(draw, "{\"targetNetworkId\":42,\"maxRounds\":5,\"cooldownSeconds\":1.0}", ctx);

            Assert.Contains("\"targetNetworkId\":99", result);
            Assert.Contains("$.tasks[0].TargetNetworkId", ctx.AskedPaths);   // ⭐ P4 — the one context, path-keyed
            Assert.Contains("\"maxRounds\":5", result);
        }

        /// <summary>⭐ <c>CE-2023</c> ③ — the same for a location pick on the struct <c>MoveToLocation</c> contract.</summary>
        [Fact]
        public void CE2023_ALocationPickOnAStructContract_LandsInTheJson()
        {
            Assert.True(typeof(MoveToLocationParamsJsonDto).IsValueType);
            var draw = BehaviorUiCompiler.Compile<MoveToLocationParamsJsonDto>();

            string result = DrawOneFrame(draw, "{\"targetLat\":1,\"targetLon\":2,\"speed\":5}",
                new PickingContext { Location = new PickableGeoPoint(50.5, 14.25) });

            Assert.Contains("\"targetLat\":50.5", result);
            Assert.Contains("\"targetLon\":14.25", result);
            Assert.Contains("\"speed\":5", result);
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
                (ref FireAtTargetParamsJsonDto dto) => dto.CooldownSeconds = 2.5f);   // CE-2023 ③ — by ref: the contract is a struct

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
