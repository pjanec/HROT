using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Fdp.Presentation.Icons;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Editor.AiShared.Documents;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Interfaces;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Host;

/// <summary>
/// Tests for <see cref="HsmDocumentFactory"/> (AIE-022).
/// All tests are headless — no GPU / ImGui context needed.
/// </summary>
public sealed class HsmDocumentFactoryTests : IDisposable
{
    // ── Shared atlas (fake GPU handle = 1) ────────────────────────────────────

    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);

    public void Dispose() => _atlas.Dispose();

    // ── Helpers ────────────────────────────────────────────────────────────────

    private AiEditorAdapterBundle MakeBundle() => new(_atlas);

    private static (HsmDefinitionBlob blob, MachineMetadata meta) Compile(HsmBuilder builder)
    {
        var graph    = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flatData = HsmFlattener.Flatten(graph);
        var blob     = HsmEmitter.Emit(flatData);
        var meta     = HsmEmitter.BuildMachineMetadata(graph);
        return (blob, meta);
    }

    private static HsmAsset Project(HsmDefinitionBlob blob, MachineMetadata meta,
        string name = "Test") =>
        HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), name, "", false, "");

    private static HsmAsset MakeSimpleAsset()
    {
        var b = new HsmBuilder("Simple");
        b.State("Idle").Initial();
        var (blob, meta) = Compile(b);
        return Project(blob, meta, "Simple");
    }

    private static HsmAsset MakeAssetWithTransition()
    {
        var b = new HsmBuilder("WithTrans");
        b.Event("Trigger", 1);
        b.State("Active").Final();
        b.State("Idle").Initial().On("Trigger").GoTo("Active");
        var (blob, meta) = Compile(b);
        return Project(blob, meta, "WithTrans");
    }

    private static HsmAsset MakeCompositeAsset()
    {
        var b = new HsmBuilder("Composite");
        b.State("Parent").Initial()
            .Child("Child1", c => c.Initial())
            .Child("Child2", c => { });
        var (blob, meta) = Compile(b);
        return Project(blob, meta, "Composite");
    }

    /// <summary>
    /// Builds a parallel asset directly from model objects (the HsmBuilder parallel DSL
    /// uses a different API; this mirrors the pattern from HsmValidatorBlackboardConflictTests).
    /// </summary>
    private static HsmAsset MakeParallelAsset()
    {
        var root     = new StateNode("__root__");
        var parallel = new StateNode("Par") { IsParallel = true, Parent = root, IsInitial = true };
        root.Children.Add(parallel);

        var rn0 = new RegionNode("R0") { RegionIndex = 0 };
        var rn1 = new RegionNode("R1") { RegionIndex = 1 };
        parallel.RegionNodes.Add(rn0);
        parallel.RegionNodes.Add(rn1);

        var child0 = new StateNode("A") { IsInitial = true, RegionIndex = 0, Parent = parallel };
        var child1 = new StateNode("B") { IsInitial = true, RegionIndex = 1, Parent = parallel };
        parallel.Children.Add(child0);
        parallel.Children.Add(child1);

        var allStates  = new List<StateNode> { parallel, child0, child1 };
        var regionList = new List<RegionNode> { rn0, rn1 };

        var blob = new HsmDefinitionBlob();
        return new HsmAsset(
            Guid.NewGuid(), "Parallel", "", false, "",
            blob, new MachineMetadata(),
            root, allStates,
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            regionList,
            new List<EventDefinition>());
    }

    // ── E5 items 6-7: the CANVAS gets the resolvers the Diagnostics window has ──
    //
    // 🔒 The silent-default rule's checkable control: "a forwarding rail PER DEPENDENCY, asserted on
    //    the CONSTRUCTED OBJECT — not on the registrar's source."
    // 📄 DESIGN_Occurrence_Scoped_Storage.md §32.15.

    /// <summary>A parallel composite running the SAME sub-tree asset in two regions — rule 8's shape.</summary>
    private static HsmAsset MakeRuleEightViolation(Guid subtreeId)
    {
        var root     = new StateNode("__root__");
        var parallel = new StateNode("Parallel") { IsParallel = true, Parent = root };
        root.Children.Add(parallel);

        var c0 = new StateNode("C0") { IsInitial = true, RegionIndex = 0, Parent = parallel, SubtreeAssetId = subtreeId };
        var c1 = new StateNode("C1") { RegionIndex = 1, Parent = parallel, SubtreeAssetId = subtreeId };
        parallel.Children.Add(c0);
        parallel.Children.Add(c1);

        var rn0 = new RegionNode("R0") { RegionIndex = 0, InitialChild = c0 };
        var rn1 = new RegionNode("R1") { RegionIndex = 1, InitialChild = c1 };
        parallel.RegionNodes.Add(rn0);
        parallel.RegionNodes.Add(rn1);

        return new HsmAsset(
            Guid.NewGuid(), "RuleEightHost", "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(),
            root,
            new List<StateNode> { parallel, c0, c1 },
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            new List<RegionNode> { rn0, rn1 },
            new List<EventDefinition>());
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The resolver REACHES THE CANVAS — rule 8 badges a node.</b>
    ///
    /// <para>🔴 <b>RED before this change.</b> <c>HsmDocumentFactory</c> built
    /// <c>new HsmGraphModel(hsmAsset)</c> with NO resolvers, while the composition root handed both
    /// to <c>HsmAssetValidator</c> ⇒ rules 8/8b fired in the Diagnostics window and were inert on the
    /// node badges. ⛔ That is precisely the split <c>HsmGraphModel</c>'s own remarks warn about:
    /// <i>"a resolver on only one of them would make a state light up in one surface and not the
    /// other."</i></para>
    ///
    /// <para>⭐ The two arms differ ONLY by the resolver, so this rail red-proves itself: pass
    /// <c>null</c> and the badge is gone.</para>
    /// </summary>
    [Fact]
    public void HsmDocumentFactory_ForwardsIsStatefulSubtree_SoRuleEightBadgesTheCanvas()
    {
        var subtreeId = new Guid("6700000e-0000-0000-0000-00000000e567");
        var bundle    = MakeBundle();

        // ⛔ WITHOUT the resolver — the pre-2026-09-26 behaviour, and the control arm.
        var without = HsmDocumentFactory.Build(MakeRuleEightViolation(subtreeId), bundle);
        without.View.Model.Nodes.Should().NotContain(n => n.State == NodeEditor.Primitives.NodeState.Error,
            "with no resolver the validator's rule 8 cannot fire — that WAS the defect");

        // ⭐ WITH it — the same asset, the same factory, one argument different.
        var with = HsmDocumentFactory.Build(
            MakeRuleEightViolation(subtreeId), bundle,
            isStatefulSubtree: id => id == subtreeId);

        with.View.Model.Nodes.Should().Contain(n => n.State == NodeEditor.Primitives.NodeState.Error,
            "the canvas must badge a rule-8 violation exactly as the Diagnostics window reports it");
    }

    /// <summary>
    /// ⭐⭐ <b>And rule 8b's resolver is forwarded too</b> — ⛔ threading only the first would have
    /// half-fixed the split, leaving 8b inert on the canvas alone.
    ///
    /// <para>⚠ Asserted as <b>reached</b> rather than as a diagnostic: rule 8b needs two DIFFERENT
    /// sub-trees colliding on one scope key, and the cheap honest check that the delegate arrives is
    /// that the factory asks it about the asset's hosts.</para>
    /// </summary>
    [Fact]
    public void HsmDocumentFactory_ForwardsSharedScopeKeys()
    {
        var subtreeId = new Guid("6800000e-0000-0000-0000-00000000e568");
        var asked     = new List<Guid>();

        HsmDocumentFactory.Build(
            MakeRuleEightViolation(subtreeId), MakeBundle(),
            isStatefulSubtree: _ => false,
            sharedScopeKeys:   id => { asked.Add(id); return System.Array.Empty<int>(); });

        asked.Should().Contain(subtreeId,
            "rule 8b's resolver must reach the canvas's validator, not just the Diagnostics window's");
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>E5</c> item 7's forwarding rail — the CATALOGUE reaches the canvas's validator,
    /// so rule 10 (<c>SubtreeAssetCycle</c>) badges here too.</b>
    /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.16.
    ///
    /// <para>🔒 <b>The silent-default rule's prescribed control: asserted on the CONSTRUCTED OBJECT,
    /// not on the registrar's source.</b> ⛔ <c>HsmValidator</c> skips the cycle rule when handed no
    /// catalogue — that is a deliberate optional dependency, and this rail is what stops it becoming
    /// the *"a production caller that HAS a dependency did not pass it"* defect.</para>
    ///
    /// <para>⭐ The two arms differ ONLY by the catalogue, so the control arm is the red-proof.</para>
    /// </summary>
    [Fact]
    public void HsmDocumentFactory_ForwardsTheCatalog_SoTheCycleRuleBadgesTheCanvas()
    {
        // ⭐ A machine whose single state hosts a child that hosts the machine back.
        var root = new StateNode("__root__");
        var s    = new StateNode("S") { IsInitial = true, Parent = root };
        root.Children.Add(s);
        var hsm = new HsmAsset(
            Guid.NewGuid(), "CycleHost", "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(), root,
            new List<StateNode> { s }, new List<TransitionNode>(),
            new List<GlobalTransitionNode>(), new List<RegionNode>(), new List<EventDefinition>());
        s.SubtreeAssetId = hsm.AssetId;    // the degenerate ring — one asset, no second type needed

        var catalog = new CycleFakeCatalog(hsm);

        // ⛔ WITHOUT the catalogue — the rule cannot fire. That WAS the state before this change.
        var without = HsmDocumentFactory.Build(hsm, MakeBundle());
        without.View.Model.Nodes.Should().NotContain(n => n.State == NodeEditor.Primitives.NodeState.Error,
            "with no catalogue the cycle rule is skipped — the control arm");

        // ⭐ WITH it — the same asset, the same factory, one argument different.
        var with = HsmDocumentFactory.Build(hsm, MakeBundle(), catalog: catalog);
        with.View.Model.Nodes.Should().Contain(n => n.State == NodeEditor.Primitives.NodeState.Error,
            "the canvas must badge a hosting cycle, not only the Diagnostics window");
    }

    /// <summary>Minimal catalogue for the cycle forwarding rail.</summary>
    private sealed class CycleFakeCatalog : Hrot.Editor.AiShared.Catalog.IAssetCatalog
    {
        private readonly Dictionary<Guid, IEditableAsset> _byId = new();
        public CycleFakeCatalog(params IEditableAsset[] assets)
        {
            foreach (var a in assets) _byId[a.AssetId] = a;
        }
        public IReadOnlyList<IEditableAsset> All => _byId.Values.ToList();
        public IEditableAsset? FindByAssetId(Guid id) => _byId.GetValueOrDefault(id);
        public IEditableAsset? FindByName(string n) => _byId.Values.FirstOrDefault(a => a.Name == n);
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid id) => Array.Empty<IEditableAsset>();
#pragma warning disable 67
        public event Action<AssetKind>? Changed;
#pragma warning restore 67
    }

    // ── AIE-022 Tests ──────────────────────────────────────────────────────────

    [Fact]
    public void HsmDocumentFactory_Build_ProducesHostServices()
    {
        var asset  = MakeSimpleAsset();
        var bundle = MakeBundle();

        var ctx = HsmDocumentFactory.Build(asset, bundle);

        ctx.Should().NotBeNull();
        ctx.View.Should().NotBeNull();
        ctx.Kind.Should().Be("HSM");

        var host = ctx.View.Host;
        host.NodeCatalog  .Should().NotBeNull();
        host.TypeSystem   .Should().NotBeNull();
        host.LinkValidator.Should().NotBeNull();
        host.CommandSink  .Should().NotBeNull();
        host.Pickers      .Should().NotBeNull();
        host.Clipboard    .Should().NotBeNull();
        host.Icons        .Should().NotBeNull();
        host.Input        .Should().NotBeNull();
        host.Theme        .Should().NotBeNull();
        host.Diagnostics  .Should().NotBeNull();
        host.Debug        .Should().BeNull();
    }

    [Fact]
    public void HsmDocumentFactory_GraphView_ExposesStatesAndTransitions()
    {
        // Build an asset with a transition and verify states + links are exposed.
        var asset  = MakeAssetWithTransition();
        var bundle = MakeBundle();

        var ctx = HsmDocumentFactory.Build(asset, bundle);

        // Model exposes all states (excluding the synthetic __root__).
        ctx.View.Model.Nodes.Count.Should().Be(asset.AllStates.Count);

        // Model exposes all transitions.
        ctx.View.Model.Links.Count.Should().Be(asset.AllTransitions.Count);

        // FindNode returns the correct state.
        var firstState = asset.AllStates.First();
        var found = ctx.View.Model.FindNode(new NodeEditor.Primitives.NodeId(firstState.StableId));
        found.Should().NotBeNull();
        found.Should().BeSameAs(firstState);
    }

    [Fact]
    public void HsmDocumentFactory_GraphView_CompositeState_IsContainer()
    {
        // Composite states (children present) must report IsContainer == true (IContainerNodeModel).
        var asset  = MakeCompositeAsset();
        var bundle = MakeBundle();

        var ctx = HsmDocumentFactory.Build(asset, bundle);

        // Find the composite "Parent" state.
        var parentState = asset.AllStates.First(s => s.Name == "Parent");
        var nodeModel   = ctx.View.Model.FindNode(
            new NodeEditor.Primitives.NodeId(parentState.StableId));

        nodeModel.Should().NotBeNull();
        // StateNode implements IContainerNodeModel — check via IsContainer property.
        (nodeModel as StateNode)!.IsContainer.Should().BeTrue();

        // Children are accessible.
        var container = nodeModel as StateNode;
        container!.Children.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void HsmDocumentFactory_GraphView_ParallelState_IsContainer()
    {
        var asset  = MakeParallelAsset();
        var bundle = MakeBundle();
        var ctx    = HsmDocumentFactory.Build(asset, bundle);

        var parState = asset.AllStates.First(s => s.Name == "Par");
        parState.IsParallel.Should().BeTrue();
        parState.IsContainer.Should().BeTrue();

        // The parallel state must appear in the model.
        var found = ctx.View.Model.FindNode(new NodeEditor.Primitives.NodeId(parState.StableId));
        found.Should().NotBeNull();
    }

    [Fact]
    public void HsmDocumentFactory_GraphView_RegionNodes_PresentForParallel()
    {
        var asset  = MakeParallelAsset();
        var bundle = MakeBundle();
        var ctx    = HsmDocumentFactory.Build(asset, bundle);

        var parState = asset.AllStates.First(s => s.Name == "Par");
        // ParallelState should have RegionNodes.
        parState.RegionNodes.Should().NotBeEmpty();
    }

    [Fact]
    public void HsmDocumentFactory_Build_CustomRenderers_ArePresent()
    {
        var asset  = MakeSimpleAsset();
        var bundle = MakeBundle();
        var ctx    = HsmDocumentFactory.Build(asset, bundle);

        // At least the 4 standard HSM renderers (TransitionLabel, InitialArrow, HistoryGlyphs, RegionConflicts).
        ctx.View.Host.CustomCanvasRenderers.Should().HaveCountGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void HsmDocumentFactory_Build_Throws_WhenAssetIsWrongType()
    {
        var wrongAsset = new FakeBTreeAsset();
        var bundle     = MakeBundle();

        var act = () => HsmDocumentFactory.Build(wrongAsset, bundle);
        act.Should().Throw<ArgumentException>().WithMessage("*HsmAsset*");
    }

    [Fact]
    public void HsmDocumentFactory_Build_MinimalMachine_BuildsWithoutThrowing()
    {
        // A minimal HSM (single state) builds without throwing and produces nodes.
        var asset  = MakeSimpleAsset();
        var bundle = MakeBundle();

        var act = () => HsmDocumentFactory.Build(asset, bundle);
        act.Should().NotThrow();

        var ctx = HsmDocumentFactory.Build(asset, bundle);
        ctx.Should().NotBeNull();
        ctx.View.Should().NotBeNull();
        ctx.View.Model.Nodes.Count.Should().BeGreaterThan(0);
    }

    // ── Fake wrong-type asset ──────────────────────────────────────────────────

    private sealed class FakeBTreeAsset : IEditableAsset
    {
        public Guid      AssetId        => Guid.NewGuid();
        public string    Name           => "fake";
        public AssetKind Kind           => AssetKind.BTree;
        public string    SourceFilePath => "";
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => false;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }
}
