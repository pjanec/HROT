using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Fdp.Toolkit.Behavior.Analyzers;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Generator
{
    /// <summary>
    /// ⭐⭐ <b><c>CE-417</c> B-2 (a′), F8 — a <c>[SharedAiAction]</c> bound in a BTree ASSET compiles to ONE call per binding.</b>
    /// 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §4 B-2, slice 3b.
    ///
    /// <para>🔴 <b>What it replaces.</b> <c>BP-306</c>'s rail asserted that <c>BTreeActionGenerator</c>'s per-METHOD
    /// <c>[SharedAi*]</c> adapter compiled. That adapter was keyed by the ATTRIBUTE DTO's field offset, so an asset binding
    /// (<c>Fqn@hostOffset</c>) reached it only when the two offsets agreed — and the asset's own 3-param call had the wrong
    /// signature for a <c>(ref T, Entity, EntityRepository)</c> method, so the asset was skipped (BTREE0002). ⭐ CE-417 retired
    /// the adapter; the asset's bridge now calls the method per binding, at the host offset its packer baked.</para>
    ///
    /// <para>The probe lives in a synthetic compilation, not in the repo, for <c>BP-306</c>'s reason:
    /// <c>HsmDtoBoundActionTripwireTests</c> deliberately trips on a committed <c>[SharedAiAction]</c>.</para>
    /// </summary>
    public sealed class SharedAiBindingCompilesTests
    {
        private const string ProbeSource = @"
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;

namespace Probe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ProbeParams { public float Value; public int Seen; }

    [StructLayout(LayoutKind.Sequential)]
    public struct OtherParams { public int Count; public int Pad; }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProbeSlot { public ProbeParams Params; }

    public static class ProbeNodes
    {
        // A 4-param [BTreeAction] — without one the analyzer creates no group and emits nothing at all.
        [BTreeAction]
        public static NodeStatus PlainAction(ref byte bb, ref BehaviorTreeState st, ref BTreeContext ctx, int pi)
            => NodeStatus.Success;

        // A 3-param reusable [BTreeAction]: the analyzer's own params projection, compared below with the bridge's.
        [BTreeAction]
        public static NodeStatus ReusableAction(ref ProbeParams p, ref BehaviorTreeState st, ref BTreeContext ctx)
            => NodeStatus.Success;

        [SharedAiAction(typeof(ProbeSlot), nameof(ProbeSlot.Params))]
        public static NodeStatus SharedAction(ref ProbeParams p, Entity self, EntityRepository world)
        { p.Seen++; return NodeStatus.Success; }

        [SharedAiAction(typeof(ProbeSlot), nameof(ProbeSlot.Params))]
        [WritesChannel(0)]
        public static NodeStatus SharedMover(ref ProbeParams p, Entity self, EntityRepository world)
            => NodeStatus.Failure;

        [SharedAiCondition(typeof(ProbeSlot), nameof(ProbeSlot.Params))]
        public static bool SharedCheck(ref ProbeParams p, Entity self, EntityRepository world)
            => p.Value > 0;
    }
}";

        /// <summary>An asset with a sentinel at offset 0 and the bound variables after it — so the BAKED offset is non-zero
        /// and differs from the attribute DTO's (0), which is exactly the case the retired adapter got wrong.</summary>
        private static string Asset(string boundVarType = "Probe.ProbeParams") => $$"""
            { "$meta": { "docType": "Hrot.BTree", "schemaVersion": 2 },
              "AssetId": "bb000417-0000-0000-0000-0000000000aa", "Name": "SharedAiProbeTree",
              "TargetNamespace": "Probe.Trees",
              "BlackboardTypeName": "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
              "ContextTypeName": "Fdp.Toolkit.Behavior.BTreeContext",
              "Nodes": [
                { "kind": "Root", "VisualId": "bb000417-0000-0000-0000-000000000001", "ChildVisualIds": [ "bb000417-0000-0000-0000-000000000002" ] },
                { "kind": "Sequence", "VisualId": "bb000417-0000-0000-0000-000000000002",
                  "ChildVisualIds": [ "bb000417-0000-0000-0000-000000000003", "bb000417-0000-0000-0000-000000000004", "bb000417-0000-0000-0000-000000000005" ] },
                { "kind": "Condition", "VisualId": "bb000417-0000-0000-0000-000000000003", "ChildVisualIds": [], "DelegateShape": "ThreeParamReusable",
                  "Condition": { "MethodFqn": "Probe.ProbeNodes.SharedCheck", "ExpressionTargetField": "bound" } },
                { "kind": "Action", "VisualId": "bb000417-0000-0000-0000-000000000004", "ChildVisualIds": [], "DelegateShape": "ThreeParamReusable",
                  "Action": { "MethodFqn": "Probe.ProbeNodes.SharedAction", "ExpressionTargetField": "bound" } },
                { "kind": "Action", "VisualId": "bb000417-0000-0000-0000-000000000005", "ChildVisualIds": [], "DelegateShape": "ThreeParamReusable",
                  "Action": { "MethodFqn": "Probe.ProbeNodes.SharedMover", "ExpressionTargetField": "bound" } } ],
              "Blackboard": { "Managed": true, "TypeName": "Fdp.Toolkit.Behavior.Components.BrainBlackboard", "Variables": [
                { "Name": "sentinel", "Type": { "TypeId": "Probe.ProbeParams" } },
                { "Name": "bound",    "Type": { "TypeId": "{{boundVarType}}" } } ] } }
            """;

        [Fact]
        public void ABTreeAssetBindingSharedAiMethods_CompilesToOnePerBindingCall_AtTheHostOffset()
        {
            var (compilation, generated, _) = Run(ProbeSource, Asset());
            string all = string.Join("\n", generated.Select(t => t.ToString()));

            // ⭐ one call per binding, the method's own signature — action, bool condition, channel-writing action
            all.Should().Contain("Probe.ProbeNodes.SharedAction(ref dto, ctx.Self, ctx.World);");
            all.Should().Contain("Probe.ProbeNodes.SharedCheck(ref dto, ctx.Self, ctx.World) ? Fbt.NodeStatus.Success : Fbt.NodeStatus.Failure;");
            all.Should().Contain("Probe.ProbeNodes.SharedMover(ref dto, ctx.Self, ctx.World);");
            all.Should().Contain("LocomotionChannel>(ctx.Self)", "a [WritesChannel] action releases its channel on Failure");

            // ⭐ keyed at the HOST offset (the sentinel sits at 0), never the attribute DTO's offset
            var keys = Regex.Matches(all, @"""Probe\.ProbeNodes\.Shared\w+@(\d+)""").Select(m => m.Groups[1].Value).Distinct().ToList();
            keys.Should().ContainSingle().Which.Should().NotBe("0");

            // ⛔ and the analyzer no longer emits a per-METHOD adapter for them
            generated.Where(t => t.FilePath.Contains("FbtActionRegistrar")).Select(t => t.ToString())
                .Should().NotContain(s => s.Contains("SharedAction") || s.Contains("SharedCheck") || s.Contains("SharedMover"));

            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            errors.Should().BeEmpty("the per-binding calls must compile: " + Environment.NewLine +
                                    string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        }

        /// <summary>⛔ The bound variable must BE the method's <c>ref</c> type — the call projects the host variable as it.
        /// Refused as a BTREE0002 skip (BTree's convention for an unbindable leaf), never a silent type-pun.</summary>
        [Fact]
        public void ABindingToAVariableOfAnotherType_IsRefused_NotPunned()
        {
            var (_, generated, diagnostics) = Run(ProbeSource, Asset(boundVarType: "Probe.OtherParams"));

            diagnostics.Where(d => d.Id == "BTREE0002").Select(d => d.GetMessage(null))
                .Should().Contain(m => m.Contains("[SharedAi]"));
            generated.Select(t => t.FilePath).Should().NotContain(p => p.Contains("SharedAiProbeTree"));
        }

        /// <summary>
        /// ⭐ <c>CE-504</c> slice 4 — ONE writer of the params projection. <c>BP-306</c> was two spellings of it (the analyzer's
        /// 3-param reusable bridge and the asset bridge) and one was wrong; this test used to hold them equal. Slice 4 retired the
        /// analyzer's arm (<c>BHU_022</c>), so the hazard is gone by construction: the analyzer emits NO projection, the asset
        /// bridge emits it. <para>✅ Red-proof: restore the analyzer's 3-param bridge arm ⇒ the first assertion reddens.</para>
        /// </summary>
        [Fact]
        public void OnlyTheAssetBridge_EmitsTheParamsProjection()
        {
            var (_, generated, _) = Run(ProbeSource, assetJson: null);
            ParamsProjections(string.Join("\n", generated.Select(t => t.ToString())))
                .Should().BeEmpty("the analyzer no longer writes a params projection (CE-504 slice 4)");
            BridgeProjections().Should().NotBeEmpty("the asset bridge is the one writer");
        }

        // ---- ⭐ CE-2079: the SOP order on an action node ("Do when idle" / "React") -----------------------------------

        private const string SopSource = @"
using System.Runtime.InteropServices;
namespace Probe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CoverParams { public float Radius; public int Rounds; }
}";

        private const string SopAsset = """
            { "$meta": { "docType": "Hrot.BTree", "schemaVersion": 2 },
              "AssetId": "bb002079-0000-0000-0000-0000000000aa", "Name": "SopOrderProbeTree",
              "TargetNamespace": "Probe.Trees",
              "BlackboardTypeName": "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
              "ContextTypeName": "Fdp.Toolkit.Behavior.BTreeContext",
              "Nodes": [
                { "kind": "Root", "VisualId": "bb002079-0000-0000-0000-000000000001", "ChildVisualIds": [ "bb002079-0000-0000-0000-000000000002" ] },
                { "kind": "Selector", "VisualId": "bb002079-0000-0000-0000-000000000002",
                  "ChildVisualIds": [ "bb002079-0000-0000-0000-000000000003", "bb002079-0000-0000-0000-000000000004" ] },
                { "kind": "Action", "VisualId": "bb002079-0000-0000-0000-000000000003", "ChildVisualIds": [],
                  "SopOrder": { "Kind": "React", "BehaviorName": "TakeCover", "ParamsVariable": "cover", "Urgency": "Hit" } },
                { "kind": "Action", "VisualId": "bb002079-0000-0000-0000-000000000004", "ChildVisualIds": [],
                  "SopOrder": { "Kind": "DoWhenIdle", "BehaviorName": "Patrol" } } ],
              "Blackboard": { "Managed": true, "TypeName": "Fdp.Toolkit.Behavior.Components.BrainBlackboard", "Variables": [
                { "Name": "sentinel", "Type": { "TypeId": "System.Int32" } },
                { "Name": "cover",    "Type": { "TypeId": "Probe.CoverParams" } } ] } }
            """;

        /// <summary>
        /// ⭐⭐ <b><c>CE-2079</c> — an SOP order compiles to ONE call into <c>Fdp.Toolkit.Behavior.SopActions</c>, the params
        /// variable projected at its HOST offset, keyed the same in the topology and the registration.</b>
        /// 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.6.
        /// </summary>
        [Fact]
        public void CE2079_AnSopOrder_CompilesToOneSopActionsCall_KeyedAsTheTopologyKeysIt()
        {
            var (compilation, generated, diagnostics) = Run(SopSource, SopAsset);
            string all = string.Join("\n", generated.Select(t => t.ToString()));

            diagnostics.Where(d => d.Id == "BTREE0002").Select(d => d.GetMessage(null)).Should().BeEmpty();
            diagnostics.Should().NotContain(d => d.Id == "BTREE0004", "this SOP drives no channel");
            all.Should().Contain("global::Fdp.Toolkit.Behavior.SopActions.React(ctx.World, ctx.Self, \"TakeCover\", " +
                                 "global::Fdp.Toolkit.Behavior.Components.ReactionUrgency.Hit, in dto)");
            all.Should().Contain("global::Fdp.Toolkit.Behavior.SopActions.DoWhenIdle(ctx.World, ctx.Self, \"Patrol\", \"{}\")");

            // ⭐ one key spelling, twice each: the topology's .Action(key) and the registry's Register(key)
            var reactKeys = Regex.Matches(all, @"""Fdp\.Toolkit\.Behavior\.SopActions\.React:TakeCover:Hit@(\d+)""")
                .Select(m => m.Groups[1].Value).ToList();
            reactKeys.Should().HaveCount(2).And.OnlyContain(k => k == reactKeys[0]);
            reactKeys[0].Should().NotBe("0", "the sentinel sits at 0 — the HOST offset is baked");
            Regex.Matches(all, @"""Fdp\.Toolkit\.Behavior\.SopActions\.DoWhenIdle:Patrol:-@-1""").Count.Should().Be(2);

            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            errors.Should().BeEmpty(string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        }

        /// <summary>
        /// ⭐ <c>CE-2079</c> (§4.5 ③) — a tree carrying SOP orders that ALSO binds a <c>[WritesChannel]</c> method is warned
        /// (<c>BTREE0004</c>), once per method; the asset still builds (the runtime guard is the backstop).
        /// </summary>
        [Fact]
        public void CE2079_AnSopTreeBindingAChannelWriter_IsWarned()
        {
            string asset = SopAsset
                .Replace("\"bb002079-0000-0000-0000-000000000004\" ] },",
                         "\"bb002079-0000-0000-0000-000000000004\", \"bb002079-0000-0000-0000-000000000005\" ] },")
                .Replace("\"SopOrder\": { \"Kind\": \"DoWhenIdle\", \"BehaviorName\": \"Patrol\" } } ],",
                         "\"SopOrder\": { \"Kind\": \"DoWhenIdle\", \"BehaviorName\": \"Patrol\" } },\n" +
                         "                { \"kind\": \"Action\", \"VisualId\": \"bb002079-0000-0000-0000-000000000005\", \"ChildVisualIds\": [],\n" +
                         "                  \"Action\": { \"MethodFqn\": \"Probe.ProbeNodes.SharedMover\", \"ExpressionTargetField\": \"bound\" } } ],")
                .Replace("{ \"Name\": \"cover\",    \"Type\": { \"TypeId\": \"Probe.CoverParams\" } } ] } }",
                         "{ \"Name\": \"cover\",    \"Type\": { \"TypeId\": \"Probe.CoverParams\" } },\n" +
                         "                { \"Name\": \"bound\",    \"Type\": { \"TypeId\": \"Probe.ProbeParams\" } } ] } }");
            asset.Should().Contain("SharedMover", "the probe asset must actually bind the mover");

            var (_, _, diagnostics) = Run(ProbeSource + SopSource, asset);

            diagnostics.Where(d => d.Id == "BTREE0004").Select(d => d.GetMessage(null))
                .Should().ContainSingle().Which.Should().Contain("Probe.ProbeNodes.SharedMover");
            diagnostics.Should().NotContain(d => d.Id == "BTREE0002", "a warning, not a skip");
        }

        // ---- CE-504 C-2/C-3: the stateful and the param-less shared forms ----------------------

        private const string FormsSource = @"
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;

namespace Probe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct StepParams { public int Limit; }

    [StructLayout(LayoutKind.Sequential)]
    public struct StepState { public int Cursor; }

    public static class FormNodes
    {
        [SharedAiAction]
        public static NodeStatus SharedStep(ref StepParams p, ref StepState ws, Entity self, EntityRepository world)
            => ++ws.Cursor >= p.Limit ? NodeStatus.Success : NodeStatus.Running;

        [SharedAiAction]
        public static NodeStatus SharedWander(Entity self, EntityRepository world) => NodeStatus.Running;

        [SharedAiCondition]
        public static bool SharedAlways(Entity self, EntityRepository world) => true;
    }
}";

        private const string FormsAsset = """
            { "$meta": { "docType": "Hrot.BTree", "schemaVersion": 2 },
              "AssetId": "bb000504-0000-0000-0000-0000000000aa", "Name": "SharedFormsProbeTree",
              "TargetNamespace": "Probe.Trees",
              "BlackboardTypeName": "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
              "ContextTypeName": "Fdp.Toolkit.Behavior.BTreeContext",
              "Nodes": [
                { "kind": "Root", "VisualId": "bb000504-0000-0000-0000-000000000001", "ChildVisualIds": [ "bb000504-0000-0000-0000-000000000002" ] },
                { "kind": "Sequence", "VisualId": "bb000504-0000-0000-0000-000000000002",
                  "ChildVisualIds": [ "bb000504-0000-0000-0000-000000000003", "bb000504-0000-0000-0000-000000000004", "bb000504-0000-0000-0000-000000000005" ] },
                { "kind": "Condition", "VisualId": "bb000504-0000-0000-0000-000000000003", "ChildVisualIds": [],
                  "Condition": { "MethodFqn": "Probe.FormNodes.SharedAlways" } },
                { "kind": "Action", "VisualId": "bb000504-0000-0000-0000-000000000004", "ChildVisualIds": [],
                  "Action": { "MethodFqn": "Probe.FormNodes.SharedStep", "ExpressionTargetField": "step",
                              "WorkingStateTypeId": "Probe.StepState" } },
                { "kind": "Action", "VisualId": "bb000504-0000-0000-0000-000000000005", "ChildVisualIds": [],
                  "Action": { "MethodFqn": "Probe.FormNodes.SharedWander" } } ],
              "Blackboard": { "Managed": true, "TypeName": "Fdp.Toolkit.Behavior.Components.BrainBlackboard", "Variables": [
                { "Name": "sentinel", "Type": { "TypeId": "Probe.StepParams" } },
                { "Name": "step",     "Type": { "TypeId": "Probe.StepParams" } } ] } }
            """;

        /// <summary>
        /// ⭐⭐ <b><c>CE-504</c> slice 2 — a BTree asset binds the shared STATEFUL form <c>(ref P, ref WS, Entity, EntityRepository)</c>
        /// and the shared PARAM-LESS form <c>(Entity, EntityRepository)</c>; both compile to a call with the method's own
        /// signature, and the attributes need no arguments.</b> 📄 <c>DESIGN_BTree_Node_Call_Shapes.md</c> §4 C-2/C-3, §5.
        /// <para>✅ Red-proof: drop the shared arm from <c>EmitStatefulActionThunks</c> ⇒ the stateful call is emitted with the old
        /// 4-param argument list and the compilation fails.</para>
        /// </summary>
        [Fact]
        public void TheSharedStatefulAndParamLessForms_CompileToCallsWithTheirOwnSignatures()
        {
            var (compilation, generated, diagnostics) = Run(FormsSource, FormsAsset);
            string all = string.Join("\n", generated.Select(t => t.ToString()));

            diagnostics.Where(d => d.Id == "BTREE0002").Select(d => d.GetMessage(null))
                .Should().BeEmpty("all three bindings are valid shared forms");
            all.Should().Contain("Probe.FormNodes.SharedStep(ref dto, ref ws, ctx.Self, ctx.World)");
            all.Should().Contain("Probe.FormNodes.SharedWander(ctx.Self, ctx.World)");
            all.Should().Contain("Probe.FormNodes.SharedAlways(ctx.Self, ctx.World) ? Fbt.NodeStatus.Success : Fbt.NodeStatus.Failure");
            all.Should().Contain("Action(\"Probe.FormNodes.SharedWander\"", "a param-less node is keyed by its bare FQN");

            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            errors.Should().BeEmpty("the calls must compile: " + Environment.NewLine +
                                    string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        }

        // ---- CE-2069: the stateful CONDITION, sharing a working state with a stateful action ----------

        private const string StatefulConditionSource = @"
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;

namespace Probe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PickParams { public int Limit; }

    [StructLayout(LayoutKind.Sequential)]
    public struct AtParams { public int At; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Choice { public int Winner; }

    public static class ChoiceNodes
    {
        [SharedAiAction]
        public static NodeStatus Pick(ref PickParams p, ref Choice ws, Entity self, EntityRepository world)
        { ws.Winner = p.Limit; return NodeStatus.Running; }

        [SharedAiCondition]
        public static bool IsAt(ref AtParams p, ref Choice ws, Entity self, EntityRepository world) => ws.Winner == p.At;
    }
}";

        // ⚠ The condition names NO WorkingStateTypeId: the emitter must take it from the method's own `ref WS` (CE-2099).
        private const string StatefulConditionAsset = """
            { "$meta": { "docType": "Hrot.BTree", "schemaVersion": 2 },
              "AssetId": "bb002069-0000-0000-0000-0000000000aa", "Name": "StatefulConditionProbeTree",
              "TargetNamespace": "Probe.Trees",
              "BlackboardTypeName": "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
              "ContextTypeName": "Fdp.Toolkit.Behavior.BTreeContext",
              "Nodes": [
                { "kind": "Root", "VisualId": "bb002069-0000-0000-0000-000000000001", "ChildVisualIds": [ "bb002069-0000-0000-0000-000000000002" ] },
                { "kind": "Sequence", "VisualId": "bb002069-0000-0000-0000-000000000002",
                  "ChildVisualIds": [ "bb002069-0000-0000-0000-000000000003", "bb002069-0000-0000-0000-000000000004" ] },
                { "kind": "Action", "VisualId": "bb002069-0000-0000-0000-000000000003", "ChildVisualIds": [],
                  "Action": { "MethodFqn": "Probe.ChoiceNodes.Pick", "ExpressionTargetField": "pick",
                              "WorkingStateTypeId": "Probe.Choice", "WorkingStateTargetField": "choice" } },
                { "kind": "Condition", "VisualId": "bb002069-0000-0000-0000-000000000004", "ChildVisualIds": [],
                  "Condition": { "MethodFqn": "Probe.ChoiceNodes.IsAt", "ExpressionTargetField": "at",
                                 "WorkingStateTargetField": "choice" } } ],
              "Blackboard": { "Managed": true, "TypeName": "Fdp.Toolkit.Behavior.Components.BrainBlackboard", "Variables": [
                { "Name": "pick",   "Type": { "TypeId": "Probe.PickParams" } },
                { "Name": "at",     "Type": { "TypeId": "Probe.AtParams" } },
                { "Name": "choice", "Type": { "TypeId": "Probe.Choice" }, "Role": "State", "Scope": "Behavior" } ] } }
            """;

        /// <summary>
        /// ⭐⭐ <b><c>CE-2069</c> — a BTree binds a STATEFUL CONDITION <c>(ref P, ref WS, Entity, EntityRepository) → bool</c>,
        /// sharing one Behavior-scoped working state with a stateful action</b> (📄 <c>DESIGN_Decision_Layer.md</c> §3.3
        /// "CE-2069 build design"). 🔴 Before: the topology threw "has no call form" ⇒ <c>BTREE0002</c>, asset skipped.
        /// ⭐ Both nodes bake the SAME slot key (one shared slot), and the condition's working-state type comes from the
        /// method's signature (it names none — <c>CE-2099</c>; the old name guess would emit a non-existent <c>+IsAtState</c>).
        /// </summary>
        [Fact]
        public void CE2069_ABTreeBindsAStatefulCondition_SharingTheActionsWorkingState_AndItCompiles()
        {
            var (compilation, generated, diagnostics) = Run(StatefulConditionSource, StatefulConditionAsset);
            string all = string.Join("\n", generated.Select(t => t.ToString()));

            diagnostics.Where(d => d.Id == "BTREE0002").Select(d => d.GetMessage(null))
                .Should().BeEmpty("a stateful condition is a valid shared form now");
            all.Should().Contain("Probe.ChoiceNodes.IsAt(ref dto, ref ws, ctx.Self, ctx.World) ? Fbt.NodeStatus.Success : Fbt.NodeStatus.Failure");
            all.Should().NotContain("IsAtState", "the working-state type comes from the signature, not the name guess");
            var keys = System.Text.RegularExpressions.Regex.Matches(all, @"Probe\.ChoiceNodes\.(Pick|IsAt)@\d+@(-?\d+)")
                .Select(m => (m.Groups[1].Value, m.Groups[2].Value)).Distinct().ToList();
            keys.Select(k => k.Item2).Distinct().Should().ContainSingle("both nodes bind the Behavior-scoped 'choice': one slot");

            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            errors.Should().BeEmpty("the calls must compile: " + Environment.NewLine +
                                    string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        }

        // ⚠ NODE-scoped (no WorkingStateTargetField) and naming NO WorkingStateTypeId: the occurrence-slot path needs the type,
        //   and only the method's signature can supply it (CE-2099). A shared variable's type comes from the variable instead.
        private const string NodeScopedStatefulAsset = """
            { "$meta": { "docType": "Hrot.BTree", "schemaVersion": 2 },
              "AssetId": "bb002099-0000-0000-0000-0000000000aa", "Name": "NodeScopedStatefulProbeTree",
              "TargetNamespace": "Probe.Trees",
              "BlackboardTypeName": "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
              "ContextTypeName": "Fdp.Toolkit.Behavior.BTreeContext",
              "Nodes": [
                { "kind": "Root", "VisualId": "bb002099-0000-0000-0000-000000000001", "ChildVisualIds": [ "bb002099-0000-0000-0000-000000000002" ] },
                { "kind": "Sequence", "VisualId": "bb002099-0000-0000-0000-000000000002",
                  "ChildVisualIds": [ "bb002099-0000-0000-0000-000000000003", "bb002099-0000-0000-0000-000000000004" ] },
                { "kind": "Action", "VisualId": "bb002099-0000-0000-0000-000000000003", "ChildVisualIds": [],
                  "Action": { "MethodFqn": "Probe.ChoiceNodes.Pick", "ExpressionTargetField": "pick" } },
                { "kind": "Condition", "VisualId": "bb002099-0000-0000-0000-000000000004", "ChildVisualIds": [],
                  "Condition": { "MethodFqn": "Probe.ChoiceNodes.IsAt", "ExpressionTargetField": "at" } } ],
              "Blackboard": { "Managed": true, "TypeName": "Fdp.Toolkit.Behavior.Components.BrainBlackboard", "Variables": [
                { "Name": "pick", "Type": { "TypeId": "Probe.PickParams" } },
                { "Name": "at",   "Type": { "TypeId": "Probe.AtParams" } } ] } }
            """;

        /// <summary>
        /// ⭐⭐ <b><c>CE-2099</c> — a node-scoped stateful node that names no working-state type compiles: the type comes from
        /// the method's signature.</b> 🔴 Before: the emitter guessed <c>+PickState</c> / <c>+IsAtState</c> from the names —
        /// types that do not exist — and the generated code did not compile (the same would hit <c>EqsTacticsNodes.TakeCover</c>).
        /// </summary>
        [Fact]
        public void CE2099_ANodeScopedStatefulNode_WithNoTypeNamed_TakesItFromTheSignature_AndCompiles()
        {
            var (compilation, generated, diagnostics) = Run(StatefulConditionSource, NodeScopedStatefulAsset);
            string all = string.Join("\n", generated.Select(t => t.ToString()));

            diagnostics.Where(d => d.Id == "BTREE0002").Select(d => d.GetMessage(null)).Should().BeEmpty();
            all.Should().NotContain("PickState").And.NotContain("IsAtState");
            all.Should().Contain("Probe.Choice", "the signature's working-state type");
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            errors.Should().BeEmpty("the calls must compile: " + Environment.NewLine +
                                    string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        }

        private const string StatefulGuardHsm = """
            { "$meta": { "docType": "Hrot.Hsm", "schemaVersion": 2 },
              "AssetId": "00002069-0000-0000-0000-0000000000aa", "Name": "StatefulGuardProbeMachine",
              "TargetNamespace": "Probe.Machines", "BlackboardTypeName": "StatefulGuardProbeMachine_Blackboard",
              "States": [
                { "StableId": "20690000-0000-0000-0000-000000000000", "Name": "__Root",
                  "ChildStableIds": [ "20690000-0000-0000-0000-00000000000a", "20690000-0000-0000-0000-00000000000b" ], "ParentStableId": null, "IsInitial": false, "RegionIndex": 0,
                  "Activity": { "MethodFqn": "Probe.ChoiceNodes.Pick", "ExpressionTargetField": "pick",
                                "WorkingStateTargetField": "choice", "WorkingStateTypeId": "Probe.Choice" } },
                { "StableId": "20690000-0000-0000-0000-00000000000a", "Name": "A",
                  "ChildStableIds": [], "ParentStableId": "20690000-0000-0000-0000-000000000000", "IsInitial": true, "RegionIndex": 0 },
                { "StableId": "20690000-0000-0000-0000-00000000000b", "Name": "B",
                  "ChildStableIds": [], "ParentStableId": "20690000-0000-0000-0000-000000000000", "IsInitial": false, "RegionIndex": 0 } ],
              "Regions": [],
              "Transitions": [
                { "VisualId": "20690000-0000-0000-0000-0000000000c1", "SourceStableId": "20690000-0000-0000-0000-00000000000a",
                  "TargetStableId": "20690000-0000-0000-0000-00000000000b", "EventName": null, "Priority": 0, "Kind": "External",
                  "SyncGroupId": 0, "IsPolled": true, "Waypoints": [],
                  "Guard": { "MethodFqn": "Probe.ChoiceNodes.IsAt", "ExpressionTargetField": "at",
                             "WorkingStateTargetField": "choice", "WorkingStateTypeId": "Probe.Choice" } } ],
              "GlobalTransitions": [], "Events": [],
              "Blackboard": { "Managed": true, "TypeName": "StatefulGuardProbeMachine_Blackboard", "Variables": [
                { "Name": "pick",   "Type": { "TypeId": "Probe.PickParams" } },
                { "Name": "at",     "Type": { "TypeId": "Probe.AtParams" } },
                { "Name": "choice", "Type": { "TypeId": "Probe.Choice" }, "Role": "State", "Scope": "Behavior" } ] } }
            """;

        /// <summary>
        /// ⭐ <b><c>CE-2069</c> — an HSM binds a STATEFUL GUARD over the same <c>St</c> member a parent's stateful activity
        /// writes</b> (the agent's measurement: the HSM path already supports it, `SharedAiBindings.cs:243-287` — this rail pins it).
        /// </summary>
        [Fact]
        public void CE2069_AnHsmBindsAStatefulGuard_OverTheActivitysWorkingState_AndItCompiles()
        {
            var (compilation, generated, diagnostics) = RunHsm(StatefulConditionSource, StatefulGuardHsm);
            string all = string.Join("\n", generated.Select(t => t.ToString()));

            diagnostics.Where(d => d.Id.StartsWith("HSM")).Select(d => d.GetMessage(null))
                .Should().BeEmpty("a stateful guard is a valid shared form on an HSM");
            all.Should().Contain("global::Probe.ChoiceNodes.IsAt(", "the guard calls the shared method");
            all.Should().Contain(".St.choice;", "its working state is the block's St member the activity writes");

            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            errors.Should().BeEmpty("the calls must compile: " + Environment.NewLine +
                                    string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        }

        // ---- S8: the HSM binds the same shared forms -----------------------------------------------

        private static string HsmFormsAsset(string wsVariable) => $$"""
            { "$meta": { "docType": "Hrot.Hsm", "schemaVersion": 2 },
              "AssetId": "00000508-0000-0000-0000-0000000000aa", "Name": "SharedFormsProbeMachine",
              "TargetNamespace": "Probe.Machines", "BlackboardTypeName": "SharedFormsProbeMachine_Blackboard",
              "States": [
                { "StableId": "58000000-0000-0000-0000-000000000000", "Name": "__Root",
                  "ChildStableIds": [ "58000000-0000-0000-0000-00000000000a" ], "ParentStableId": null, "IsInitial": false, "RegionIndex": 0 },
                { "StableId": "58000000-0000-0000-0000-00000000000a", "Name": "Working",
                  "ChildStableIds": [], "ParentStableId": "58000000-0000-0000-0000-000000000000", "IsInitial": true, "RegionIndex": 0,
                  "OnEntry":  { "MethodFqn": "Probe.FormNodes.SharedWander" },
                  "Activity": { "MethodFqn": "Probe.FormNodes.SharedStep", "ExpressionTargetField": "step",
                                "WorkingStateTargetField": "{{wsVariable}}", "WorkingStateTypeId": "Probe.StepState" } } ],
              "Regions": [], "Transitions": [], "GlobalTransitions": [], "Events": [],
              "Blackboard": { "Managed": true, "TypeName": "SharedFormsProbeMachine_Blackboard", "Variables": [
                { "Name": "step",      "Type": { "TypeId": "Probe.StepParams" } },
                { "Name": "cursor",    "Type": { "TypeId": "Probe.StepState" }, "Role": "State", "Scope": "Behavior" },
                { "Name": "wrongType", "Type": { "TypeId": "System.Int32" },     "Role": "State", "Scope": "Behavior" } ] } }
            """;

        /// <summary>
        /// ⭐⭐ <b><c>S8</c> — an HSM binds the shared STATEFUL and PARAM-LESS forms, as the BTree does</b>
        /// (📄 <c>DESIGN_Behavior_Action_Binding.md</c> §5.3b). The stateful method's working state is the HSM block's own
        /// <c>St</c> member, so it needs no occurrence slot. The key is the BTree's spelling, <c>Fqn@offset@slotKey</c>; a
        /// param-less method is keyed by its bare FQN. 🔴 Before <c>S8</c> both were <c>HSM0003</c> errors.
        /// ✅ Red-proof: project <c>__ws</c> from <c>St</c> of a different member ⇒ the call no longer matches / compiles.
        /// </summary>
        [Fact]
        public void S8_AnHsmBindsTheStatefulAndParamLessSharedForms_AndTheCallsCompile()
        {
            var (compilation, generated, diagnostics) = RunHsm(FormsSource, HsmFormsAsset("cursor"));
            string all = string.Join("\n", generated.Select(t => t.ToString()));

            diagnostics.Where(d => d.Id.StartsWith("HSM")).Select(d => d.GetMessage(null))
                .Should().BeEmpty("both bindings are valid shared forms on an HSM now");
            all.Should().Contain("global::Probe.FormNodes.SharedStep(ref *(global::Probe.StepParams*)(__root + 0), ref __ws, __bridge->Self, __repo)");
            all.Should().Contain(".St.cursor;", "the working state is the block's own St member (S8-1)");
            all.Should().Contain("global::Probe.FormNodes.SharedWander(__bridge->Self, __repo)");
            int slotKey = Hrot.AiEditor.Persistence.Emit.SharedAiBindings.StatefulSlotKey(
                new Guid("00000508-0000-0000-0000-0000000000aa"), "cursor");
            all.Should().Contain($"Probe.FormNodes.SharedStep@0@{slotKey}", "the BTree's stateful key spelling (S8-2)");

            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            errors.Should().BeEmpty("the calls must compile: " + Environment.NewLine +
                                    string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        }

        /// <summary>
        /// ⛔ <c>S8</c> — a stateful binding whose working-state variable is not a block <c>St</c> member of the method's
        /// <c>WS</c> type is <c>HSM0003</c>, naming the variable, and the asset is not emitted. It is never a silent skip.
        /// </summary>
        [Fact]
        public void S8_AnHsmStatefulBindingWithTheWrongWorkingStateVariable_IsHsm0003()
        {
            var (_, generated, diagnostics) = RunHsm(FormsSource, HsmFormsAsset("wrongType"));

            var hits = diagnostics.Where(d => d.Id == "HSM0003").ToList();
            hits.Should().ContainSingle();
            hits[0].GetMessage(null).Should().Contain("wrongType").And.Contain("Probe.StepState");
            generated.Should().BeEmpty();
        }

        // ---- CE-2083: an HSM state issues an SOP order ---------------------------------------------

        private static string SopHsm(string extraOnIdle = "") => $$"""
            { "$meta": { "docType": "Hrot.Hsm", "schemaVersion": 2 },
              "AssetId": "00002083-0000-0000-0000-0000000000aa", "Name": "SopOrderProbeMachine",
              "TargetNamespace": "Probe.Machines", "BlackboardTypeName": "SopOrderProbeMachine_Blackboard",
              "States": [
                { "StableId": "20830000-0000-0000-0000-000000000000", "Name": "__Root",
                  "ChildStableIds": [ "20830000-0000-0000-0000-00000000000a", "20830000-0000-0000-0000-00000000000b" ],
                  "ParentStableId": null, "IsInitial": false, "RegionIndex": 0 },
                { "StableId": "20830000-0000-0000-0000-00000000000a", "Name": "Idle",
                  "ChildStableIds": [], "ParentStableId": "20830000-0000-0000-0000-000000000000", "IsInitial": true, "RegionIndex": 0,
                  "SopOrder": { "Kind": "DoWhenIdle", "BehaviorName": "Patrol" }{{extraOnIdle}} },
                { "StableId": "20830000-0000-0000-0000-00000000000b", "Name": "UnderFire",
                  "ChildStableIds": [], "ParentStableId": "20830000-0000-0000-0000-000000000000", "IsInitial": false, "RegionIndex": 0,
                  "SopOrder": { "Kind": "React", "BehaviorName": "TakeCover", "ParamsVariable": "cover", "Urgency": "Hit" } } ],
              "Regions": [], "Transitions": [], "GlobalTransitions": [], "Events": [],
              "Blackboard": { "Managed": true, "TypeName": "SopOrderProbeMachine_Blackboard", "Variables": [
                { "Name": "sentinel", "Type": { "TypeId": "System.Int32" } },
                { "Name": "cover",    "Type": { "TypeId": "Probe.CoverParams" } } ] } }
            """;

        /// <summary>
        /// ⭐⭐ <b><c>CE-2083</c> — an HSM state's SOP order runs as its ACTIVITY: the topology names the order's key, the
        /// bridge registers ONE <c>SopActions</c> call under that key's id, the params variable read LIVE at its host
        /// offset.</b> 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.10 (D1, D2).
        /// </summary>
        [Fact]
        public void CE2083_AnHsmStatesSopOrder_RunsAsItsActivity_OneSopActionsCall_KeyedAsTheTopologyKeysIt()
        {
            var (compilation, generated, diagnostics) = RunHsm(SopSource, SopHsm());
            string all = string.Join("\n", generated.Select(t => t.ToString()));

            diagnostics.Where(d => d.Id.StartsWith("HSM")).Select(d => d.GetMessage(null)).Should().BeEmpty();
            all.Should().Contain("global::Fdp.Toolkit.Behavior.SopActions.React(__repo, __bridge->Self, \"TakeCover\", " +
                                 "global::Fdp.Toolkit.Behavior.Components.ReactionUrgency.Hit, in *(global::Probe.CoverParams*)(__root + ");
            all.Should().Contain("global::Fdp.Toolkit.Behavior.SopActions.DoWhenIdle(__repo, __bridge->Self, \"Patrol\", \"{}\")");

            // ⭐ the BTree's key spelling; the topology's .Activity(key) and the registration's comment name the same key
            var reactKeys = Regex.Matches(all, @"Fdp\.Toolkit\.Behavior\.SopActions\.React:TakeCover:Hit@(\d+)")
                .Select(m => m.Groups[1].Value).ToList();
            reactKeys.Should().HaveCountGreaterThanOrEqualTo(2).And.OnlyContain(k => k == reactKeys[0]);
            reactKeys[0].Should().NotBe("0", "the sentinel sits at 0 — the HOST offset is baked");
            all.Should().Contain(".Activity(\"Fdp.Toolkit.Behavior.SopActions.React:TakeCover:Hit@" + reactKeys[0] + "\")");
            ushort id = Fdp.Toolkit.Behavior.Shared.HsmActionKey.ForCompoundKey(
                "Fdp.Toolkit.Behavior.SopActions.React:TakeCover:Hit@" + reactKeys[0]);
            all.Should().Contain($"HsmActionDispatcher.RegisterAction({id}, ", "registered under the id the activity name hashes to");

            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            errors.Should().BeEmpty("the calls must compile: " + Environment.NewLine +
                                    string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
        }

        /// <summary>⛔ <c>CE-2083</c> D3 — an order AND an activity binding on one state is one slot with two owners: an error
        /// naming the state, and the asset is not emitted.</summary>
        [Fact]
        public void CE2083_AStateWithAnSopOrderAndAnActivity_IsAnError()
        {
            var (_, generated, diagnostics) = RunHsm(SopSource,
                SopHsm(extraOnIdle: ", \"Activity\": { \"MethodFqn\": \"Probe.Nowhere.Run\" }"));

            diagnostics.Where(d => d.Id == "HSM0001").Select(d => d.GetMessage(null))
                .Should().ContainSingle().Which.Should().Contain("Idle").And.Contain("SOP order");
            generated.Should().NotContain(t => t.FilePath.EndsWith(".Registrar.g.cs"));
        }

        private static (Compilation Compilation, IReadOnlyList<SyntaxTree> Generated, IReadOnlyList<Diagnostic> Diagnostics)
            RunHsm(string source, string hsmJson)
        {
            var input = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "S8Probe_" + Guid.NewGuid().ToString("N"),
                new[] { input },
                ReferenceSet(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                    nullableContextOptions: NullableContextOptions.Enable));
            var driver = CSharpGeneratorDriver.Create(
                    new ISourceGenerator[] { new HsmJsonGenerator().AsSourceGenerator() },
                    new AdditionalText[] { new StringAdditionalText("/probe/SharedFormsProbeMachine.hsm.json", hsmJson) });
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
            return (output, output.SyntaxTrees.Where(t => t != input).ToList(), diagnostics);
        }

        // ---- helpers -----------------------------------------------------------

        private static readonly Regex ProjectionRegex = new(
            @"ref Unsafe\.AddByteOffset\(ref global::Fdp\.Toolkit\.Behavior\.BehaviorBlock\.Require\(ref bb\), \((nint|IntPtr)\)\d+\)",
            RegexOptions.Compiled);

        private static IReadOnlyList<string> ParamsProjections(string source) =>
            ProjectionRegex.Matches(source).Cast<Match>().Select(m => m.Value).ToList();

        /// <summary>The bridge side, over the REAL corpus (a constant size resolver: only the SHAPE is compared).</summary>
        private static IReadOnlyList<string> BridgeProjections()
        {
            var found = new List<string>();
            foreach (var file in Golden.AiAssetCorpus.EnumerateFiles(Golden.AiAssetKind.BTree))
            {
                var dto = BTreeJsonServices.Deserialize(File.ReadAllText(file));
                if (dto == null) continue;
                found.AddRange(ParamsProjections(BTreeBridgeEmitCore.EmitBridge(dto, _ => 8)));
            }
            return found;
        }

        private static (Compilation Compilation, IReadOnlyList<SyntaxTree> Generated, IReadOnlyList<Diagnostic> Diagnostics)
            Run(string source, string? assetJson)
        {
            var input = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "Ce417Probe_" + Guid.NewGuid().ToString("N"),
                new[] { input },
                ReferenceSet(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                    nullableContextOptions: NullableContextOptions.Enable));

            var texts = assetJson == null
                ? Array.Empty<AdditionalText>()
                : new AdditionalText[] { new StringAdditionalText("/probe/SharedAiProbeTree.btree.json", assetJson) };
            var driver = CSharpGeneratorDriver.Create(
                    new ISourceGenerator[] { new BTreeActionGenerator().AsSourceGenerator(), new BTreeJsonGenerator().AsSourceGenerator() },
                    texts);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
            return (output, output.SyntaxTrees.Where(t => t != input).ToList(), diagnostics);
        }

        /// <summary>Every assembly loaded in this test process, plus the framework facades.</summary>
        private static IReadOnlyList<MetadataReference> ReferenceSet()
        {
            var refs = new List<MetadataReference>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in new[]
                     {
                         // ⚠ Only the ASSEMBLY matters (Roslyn metadata references) — load them before the walk below.
                         typeof(Fdp.Toolkit.Behavior.Components.BehaviorState),
                         typeof(Fdp.Toolkit.Behavior.BTreeContext),
                         typeof(Fdp.Toolkit.Blueprints.BlueprintRegistry),
                         typeof(Fdp.Core.Entity),
                         typeof(Fbt.NodeStatus),
                         typeof(Fbt.Kernel.SharedAiActionAttribute),
                         typeof(Fbt.Compiler.BTreeBuilder<,>),   // the asset's topology core builds through it
                         typeof(Fhsm.Compiler.HsmBuilder),        // S8: the HSM topology core builds through it
                         typeof(Fhsm.Kernel.HsmActionDispatcher),
                         typeof(System.Text.Json.JsonSerializer),   // the HSM registrar's params options
                     })
                if (seen.Add(t.Assembly.Location))
                    refs.Add(MetadataReference.CreateFromFile(t.Assembly.Location));

            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic || string.IsNullOrEmpty(a.Location)) continue;
                if (!seen.Add(a.Location)) continue;
                refs.Add(MetadataReference.CreateFromFile(a.Location));
            }

            string dir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            foreach (var name in new[] { "System.Runtime.dll", "netstandard.dll" })
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p) && seen.Add(p))
                    refs.Add(MetadataReference.CreateFromFile(p));
            }
            return refs;
        }
    }
}
