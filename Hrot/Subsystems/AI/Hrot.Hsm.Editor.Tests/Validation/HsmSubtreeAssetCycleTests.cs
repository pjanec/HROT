using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Validation;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Validation;

/// <summary>
/// ⭐⭐⭐ <b><c>E5</c> item 7 — rule 10, <c>SubtreeAssetCycle</c>, on the HSM side.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.16.
///
/// <para>🔒 <c>HsmValidator.SubtreeHostsUnder</c> carried the note <i>"that is a walk over ASSETS,
/// needs a resolver this validator does not have, and belongs to whoever builds subtree hosting for
/// real"</i> for four batches. These rails are what closed it.</para>
///
/// <para>⚠ <b>The catalogue arm is what makes the rule real.</b> Without one the rule is skipped —
/// so the no-catalogue rail below is not a curiosity, it pins the SKIP so a future reader cannot
/// mistake silence for "no cycle".</para>
/// </summary>
public sealed class HsmSubtreeAssetCycleTests
{
    /// <summary>A stand-in for a BTree asset: the HSM test assembly cannot see the real one.</summary>
    private sealed class FakeHostingAsset : IEditableAsset, ISubtreeHostingAsset
    {
        public Guid AssetId { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "Child";
        public AssetKind Kind => AssetKind.BTree;
        public string SourceFilePath => "/child.btree.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
        public List<Guid> Hosts { get; } = new();
        public IReadOnlyCollection<Guid> GetHostedSubtreeAssetIds() => Hosts;
#pragma warning disable 67
        public event Action? Changed;
#pragma warning restore 67
    }

    private sealed class FakeCatalog : IAssetCatalog
    {
        private readonly Dictionary<Guid, IEditableAsset> _byId = new();
        public FakeCatalog(params IEditableAsset[] assets)
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

    /// <summary>A minimal, VALID machine whose single state hosts <paramref name="childId"/>.</summary>
    private static HsmAsset MakeHostingMachine(Guid childId, string name = "HostMachine")
    {
        var root = new StateNode("__root__");
        var s    = new StateNode("S") { IsInitial = true, Parent = root, SubtreeAssetId = childId };
        root.Children.Add(s);

        return new HsmAsset(
            Guid.NewGuid(), name, "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(),
            root,
            new List<StateNode> { s },
            new List<TransitionNode>(),
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The named case: the HSM hosts a child that hosts the HSM back.</b>
    /// ⛔ Hosting is expanded inline by <c>BrainTickSystem.TickHostedChildren</c>, so the ring has no
    /// base case — it recurses until the stack dies. That is why this is an ERROR.
    /// </summary>
    [Fact]
    public void E5_Item7_AHostsBHostsA_IsAHardError()
    {
        var child = new FakeHostingAsset { Name = "ChildTree" };
        var hsm   = MakeHostingMachine(child.AssetId);
        child.Hosts.Add(hsm.AssetId);                       // ⭐ closes the ring

        var catalog = new FakeCatalog(hsm, child);
        var d = new HsmValidator(catalog: catalog).Validate(hsm);

        var cycle = d.Should().ContainSingle(x => x.Code == HsmDiagnosticCode.SubtreeAssetCycle).Subject;
        cycle.Severity.Should().Be(HsmDiagnosticSeverity.Error);
        cycle.Message.Should().Contain("HostMachine").And.Contain("ChildTree");
    }

    /// <summary>
    /// 🔴 <b>THE RED-PROOF, and it is the control arm of the rail above:</b> the SAME machine and the
    /// SAME child, with the back-edge removed. ⛔ If this ever reddens, the rule is reporting ordinary
    /// hosting as a cycle — the one failure mode worse than not having the rule.
    /// </summary>
    [Fact]
    public void E5_Item7_OrdinaryHosting_IsNotACycle()
    {
        var child = new FakeHostingAsset { Name = "ChildTree" };
        var hsm   = MakeHostingMachine(child.AssetId);
        // ⛔ no back-edge

        var d = new HsmValidator(catalog: new FakeCatalog(hsm, child)).Validate(hsm);

        d.Should().NotContain(x => x.Code == HsmDiagnosticCode.SubtreeAssetCycle);
    }

    /// <summary>⭐ A state hosting its OWN machine is the degenerate ring, and must be caught too.</summary>
    [Fact]
    public void E5_Item7_AMachineHostingItself_IsAHardError()
    {
        var root = new StateNode("__root__");
        var s    = new StateNode("S") { IsInitial = true, Parent = root };
        root.Children.Add(s);
        var hsm = new HsmAsset(
            Guid.NewGuid(), "SelfHost", "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(), root,
            new List<StateNode> { s }, new List<TransitionNode>(),
            new List<GlobalTransitionNode>(), new List<RegionNode>(), new List<EventDefinition>());
        s.SubtreeAssetId = hsm.AssetId;

        var d = new HsmValidator(catalog: new FakeCatalog(hsm)).Validate(hsm);

        d.Should().Contain(x => x.Code == HsmDiagnosticCode.SubtreeAssetCycle);
    }

    /// <summary>
    /// ⚠⚠ <b>Without a catalogue the rule is SKIPPED — pinned deliberately.</b> ⛔ This is a silent
    /// default, and the honest control for it is the forwarding rail on the constructed object
    /// (<c>HsmDocumentFactoryTests</c>) plus this one, which makes the skip explicit rather than
    /// letting a future reader read the silence as "no cycle found".
    /// </summary>
    [Fact]
    public void E5_Item7_WithNoCatalog_TheRuleIsSkipped_NotSilentlyPassed()
    {
        var child = new FakeHostingAsset { Name = "ChildTree" };
        var hsm   = MakeHostingMachine(child.AssetId);
        child.Hosts.Add(hsm.AssetId);                       // a REAL cycle exists

        var d = new HsmValidator().Validate(hsm);           // ⛔ no catalogue

        d.Should().NotContain(x => x.Code == HsmDiagnosticCode.SubtreeAssetCycle);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The diagnostic TARGETS the hosting state — which is what lets the canvas badge it.</b>
    /// 🔴 The first cut of this rule emitted an EMPTY target list, on the argument that no state of
    /// the open asset is at fault. ⛔ Wrong: a ring has no innocent edge, and the empty list made the
    /// rule invisible on the canvas. ⚠ Caught by the forwarding rail, not by review.
    /// </summary>
    [Fact]
    public void E5_Item7_TheDiagnosticTargetsTheHostingState_SoTheCanvasCanBadgeIt()
    {
        var child = new FakeHostingAsset { Name = "ChildTree" };
        var hsm   = MakeHostingMachine(child.AssetId);
        child.Hosts.Add(hsm.AssetId);

        var d = new HsmValidator(catalog: new FakeCatalog(hsm, child)).Validate(hsm);

        var cycle = d.Single(x => x.Code == HsmDiagnosticCode.SubtreeAssetCycle);
        var hostingState = hsm.AllStates.Single(s => s.SubtreeAssetId == child.AssetId);
        cycle.TargetStableIds.Should().Equal(hostingState.StableId);
    }

    /// <summary>
    /// ⭐ <c>HsmAsset.GetHostedSubtreeAssetIds</c> is the forward edge itself — deduplicated, and
    /// never carrying <see cref="Guid.Empty"/>.
    /// </summary>
    [Fact]
    public void E5_Item7_GetHostedSubtreeAssetIds_IsADedupedSetWithoutEmpty()
    {
        var childId = Guid.NewGuid();
        var root = new StateNode("__root__");
        var a = new StateNode("A") { IsInitial = true, Parent = root, SubtreeAssetId = childId };
        var b = new StateNode("B") { Parent = root, SubtreeAssetId = childId };   // the same child twice
        var c = new StateNode("C") { Parent = root };                             // hosts nothing
        root.Children.Add(a); root.Children.Add(b); root.Children.Add(c);

        var hsm = new HsmAsset(
            Guid.NewGuid(), "M", "", false, "",
            new HsmDefinitionBlob(), new MachineMetadata(), root,
            new List<StateNode> { a, b, c }, new List<TransitionNode>(),
            new List<GlobalTransitionNode>(), new List<RegionNode>(), new List<EventDefinition>());

        hsm.GetHostedSubtreeAssetIds().Should().BeEquivalentTo(new[] { childId });
    }
}
