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
        /// The two emitters that still write the params projection — the analyzer (3-param reusable bridge) and the asset
        /// bridge — must produce the SAME text (<c>BP-306</c>: two spellings of it, and one was wrong). Asserted against
        /// each other, not against a literal restated here.
        /// </summary>
        [Fact]
        public void TheAnalyzerAndTheBridge_EmitTheSameParamsProjection()
        {
            var (_, generated, _) = Run(ProbeSource, assetJson: null);
            var fromAnalyzer = ParamsProjections(string.Join("\n", generated.Select(t => t.ToString())));
            var fromBridge = BridgeProjections();

            fromAnalyzer.Should().NotBeEmpty("the analyzer must emit at least one params projection");
            fromBridge.Should().NotBeEmpty("the bridge must emit at least one params projection");
            fromAnalyzer.Select(Normalise).Distinct()
                .Should().BeEquivalentTo(fromBridge.Select(Normalise).Distinct(),
                    "one expression, one spelling — BP-306 was two spellings of it, and one was wrong");
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

        // ---- helpers -----------------------------------------------------------

        private static readonly Regex ProjectionRegex = new(
            @"ref Unsafe\.AddByteOffset\(ref global::Fdp\.Toolkit\.Behavior\.BehaviorBlock\.Require\(ref bb\), \((nint|IntPtr)\)\d+\)",
            RegexOptions.Compiled);

        private static IReadOnlyList<string> ParamsProjections(string source) =>
            ProjectionRegex.Matches(source).Cast<Match>().Select(m => m.Value).ToList();

        private static string Normalise(string projection) =>
            Regex.Replace(projection, @"\)\d+\)$", ")N)");

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
