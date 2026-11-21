using Xunit;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Compiler
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-383</c> — an EXPLICIT action/guard id overrides the name hash.</b>
    /// 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §3.2.
    ///
    /// <para>🔴 <b>Why the override has to exist at all.</b> A named action resolves through
    /// <c>FNV1a16(FQN)</c> (<c>E6</c>(A)); a BLUEPRINT-hosted thunk registers under its
    /// <c>BlueprintId</c>, which is FNV-1a32 of the asset GUID truncated to a <c>ushort</c>.
    /// ⛔ <b>No authorable string hashes to that</b> — so the id must be BAKED, and the flattener has
    /// to prefer it over the name.</para>
    ///
    /// <para>⭐⭐ <b>The mechanism already existed for entry/exit</b> (<c>EntryActionId</c>,
    /// <c>ExitActionId</c>, added for the JSON parser) and was simply unreachable for the two slots
    /// this programme needs. ⇒ this is an EXTENSION of a shipped pattern, not a new one.</para>
    /// </summary>
    public class ExplicitActionIdOverrideTests
    {
        private const ushort BakedId = 0xBEEF;

        private static HsmFlattener.FlattenedData Flatten(HsmBuilder b, out StateMachineGraph graph)
        {
            graph = b.Build();
            HsmNormalizer.Normalize(graph);
            return HsmFlattener.Flatten(graph);
        }

        // ── ACTIVITY ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void StateNode_ActivityActionId_DefaultsToZero_MeaningUnset()
            => Assert.Equal(0, new StateNode("S").ActivityActionId);

        /// <summary>⭐⭐ The headline for the activity slot: the baked id WINS over the name hash.</summary>
        [Fact]
        public void AnExplicitActivityId_OverridesTheNameHash()
        {
            var b = new HsmBuilder("M");
            b.RegisterAction("Some.Type.Activity");
            b.State("A").Initial().Activity("Some.Type.Activity").ActivityId(BakedId);

            var flat = Flatten(b, out var graph);
            int a = graph.FindState("A")!.FlatIndex;

            Assert.Equal(BakedId, flat.States[a].ActivityActionId);
        }

        /// <summary>
        /// ⭐ The converse, and it is what protects every existing asset: with NO explicit id the
        /// name hash still decides. ⛔ An override that also changed the default would silently
        /// re-point every shipped activity.
        /// </summary>
        [Fact]
        public void WithNoExplicitActivityId_TheNameHashStillDecides()
        {
            var b = new HsmBuilder("M");
            b.RegisterAction("Some.Type.Activity");
            b.State("A").Initial().Activity("Some.Type.Activity");

            var flat = Flatten(b, out var graph);
            int a = graph.FindState("A")!.FlatIndex;

            Assert.NotEqual(BakedId, flat.States[a].ActivityActionId);
            Assert.NotEqual((ushort)0xFFFF, flat.States[a].ActivityActionId);
        }

        [Fact]
        public void AStateWithNeitherNameNorId_GetsTheNoActionSentinel()
        {
            var b = new HsmBuilder("M");
            b.State("A").Initial();

            var flat = Flatten(b, out var graph);
            int a = graph.FindState("A")!.FlatIndex;

            Assert.Equal((ushort)0xFFFF, flat.States[a].ActivityActionId);
        }

        // ── GUARD ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void AnExplicitGuardId_OverridesTheNameHash()
        {
            var b = new HsmBuilder("M");
            b.RegisterGuard("Some.Type.Guard");
            var a = b.State("A").Initial();
            b.State("B");
            a.On(ReservedEventIds.Completion).GoTo("B").Guard("Some.Type.Guard").GuardId(BakedId);

            var flat = Flatten(b, out _);
            Assert.Equal(BakedId, flat.Transitions[0].GuardId);
        }

        [Fact]
        public void WithNoExplicitGuardId_TheNameHashStillDecides()
        {
            var b = new HsmBuilder("M");
            b.RegisterGuard("Some.Type.Guard");
            var a = b.State("A").Initial();
            b.State("B");
            a.On(ReservedEventIds.Completion).GoTo("B").Guard("Some.Type.Guard");

            var flat = Flatten(b, out _);
            Assert.NotEqual(BakedId, flat.Transitions[0].GuardId);
            Assert.NotEqual((ushort)0xFFFF, flat.Transitions[0].GuardId);
        }

        // ── The entry/exit pattern this extends must keep working ────────────────────────

        /// <summary>
        /// ⚠ <b>A regression rail for the pattern being COPIED, not the one being added.</b> Entry
        /// and exit have honoured an explicit id since the JSON parser needed it; a refactor of this
        /// expression must not lose them.
        /// </summary>
        [Fact]
        public void TheEntryAndExitOverridesStillWork()
        {
            var b = new HsmBuilder("M");
            b.State("A").Initial();

            var graph = b.Build();
            var node  = graph.FindState("A")!;
            node.EntryActionId = 0x1111;
            node.ExitActionId  = 0x2222;

            HsmNormalizer.Normalize(graph);
            var flat = HsmFlattener.Flatten(graph);

            Assert.Equal((ushort)0x1111, flat.States[node.FlatIndex].OnEntryActionId);
            Assert.Equal((ushort)0x2222, flat.States[node.FlatIndex].OnExitActionId);
        }

        // ── The silent drop this fixed ───────────────────────────────────────────────────

        /// <summary>
        /// 🔴 <b><c>CE-383</c> also FIXED a silent drop, and this rail is the evidence.</b>
        /// <c>TransitionNode.ActionId</c> already existed and <c>JsonStateMachineParser:92</c> already
        /// SET it — but the flattener ignored it, so a JSON-authored transition action was parsed and
        /// discarded. ⚠ Zero production blast radius (that parser has test-only callers), which is
        /// why it was safe to repair in the same expression rather than filed for later.
        /// </summary>
        [Fact]
        public void AnExplicitTransitionActionId_IsNoLongerSilentlyDropped()
        {
            var b = new HsmBuilder("M");
            var a = b.State("A").Initial();
            b.State("B");
            var t = a.On(ReservedEventIds.Completion).GoTo("B");

            var graph = b.Build();
            graph.FindState("A")!.Transitions[0].ActionId = BakedId;

            HsmNormalizer.Normalize(graph);
            var flat = HsmFlattener.Flatten(graph);

            Assert.Equal(BakedId, flat.Transitions[0].ActionId);
        }
    }
}
