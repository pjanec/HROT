using System;
using System.Collections.Generic;
using FluentAssertions;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>S4</c> — <c>details.parametersync</c>, AND IT SAYS <i>WHICH</i> REFUSAL.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.3's catalogue · §7.6 ④ — the LAST of
/// <c>BP-399</c>'s five rows.
///
/// <para>⛔⛔ <b>The retired arm interleaved FOUR refusals with the draw</b>, so not one of them could be
/// asserted: <i>"Subtree not resolved"</i> · <i>"Sub-asset resolver not configured"</i> · <i>"Sub-asset
/// not found"</i> · <i>"Sub-tree has no blackboard variables"</i>. ⭐ They are a <b>VALUE</b> now
/// *(<see cref="ParameterSyncModel.Refusal"/>)* — 📌 <c>B98b</c>'s discipline: say which one fired, never
/// one sentence for four causes.</para>
///
/// <para>⭐⭐ <b>Why <c>R-99</c> is satisfied rather than waived.</b> This panel was held back because
/// <i>"promoting an inert panel is worse than leaving it buried"</i> — the bindings it authors reached
/// nothing. ⚠ <c>Q49</c> made the sub-tree identity survive a reload and <c>Q50</c> made the master
/// blackboard declare the slice the emitted body writes through ⇒ ⭐ the data now reaches the runtime.
/// ⛔ <b>One limit stands</b> *(<c>BP-446</c>)*: a callee with a GENERATED blackboard is still skipped at
/// generation with a <c>BTREE0002</c> — the authoring is real, the emission is not yet universal.</para>
/// </summary>
public sealed class TheParameterSyncPanelSaysWhichRefusalTests
{
    // ══ when the view does NOT claim the panel ═══════════════════════════════

    /// <summary>⛔ Not a subtree node ⇒ no model at all, so the view never claims the panel and the
    /// shell draws <c>R-117</c>'s grey line. ⚠ Distinct from a REFUSAL, which is a claim.</summary>
    [Fact]
    public void APlainNode_YieldsNoModel()
        => Wired().ModelFor(ContextWith(new SyncAsset(subtreeNode: false),
                                        new BTreeNodeSelection(NodeId)))
            .Should().BeNull();

    /// <summary>⛔ An asset that is not syncable at all ⇒ no model. ⚠ Blueprint and HSM assets land here.</summary>
    [Fact]
    public void ANonSyncableAsset_YieldsNoModel()
        => Wired().ModelFor(ContextWith(null, new BTreeNodeSelection(NodeId)))
            .Should().BeNull();

    /// <summary>⛔ Two nodes is not "the first one" — 📌 <c>R-118</c>: the <c>Count == 1</c> rule lives in
    /// the predicate, and a sync table cannot honestly edit two nodes at once.</summary>
    [Fact]
    public void TwoSelectedNodes_YieldNoModel()
        => Wired().ModelFor(ContextWith(new SyncAsset(subtreeNode: true),
                                        new BTreeNodeSelection(NodeId), new BTreeNodeSelection(Guid.NewGuid())))
            .Should().BeNull();

    // ══ the four refusals, each nameable ═════════════════════════════════════

    /// <summary>⭐ An unresolved subtree reference — the node points somewhere, but nothing resolved it.</summary>
    [Fact]
    public void AnUnresolvedSubtree_SaysSo()
        => Refusal(Wired(), new SyncAsset(subtreeNode: true, resolved: false))
            .Should().Contain("not resolved");

    /// <summary>
    /// ⭐⭐⭐ <b>NO RESOLVER — the refusal that was a real production defect</b> *(<c>92d</c>)*: the
    /// registrar held the catalog and did not pass it, so every designer saw this line and no sync
    /// binding could be authored at all. ⚠ Kept as a refusal, ⛔ not removed: a host with no catalog is
    /// still a legal host, and it must say why rather than show an empty table.
    /// </summary>
    [Fact]
    public void NoResolver_SaysSo()
        => Refusal(new ParameterSyncSource(), new SyncAsset(subtreeNode: true))
            .Should().Contain("resolver not configured");

    /// <summary>⭐ The subtree asset was DELETED — a real state, and distinct from "no resolver".</summary>
    [Fact]
    public void AMissingSubAsset_SaysSo()
        => Refusal(WiredTo(_ => null), new SyncAsset(subtreeNode: true))
            .Should().Contain("not found");

    /// <summary>⭐ The callee has no blackboard variables ⇒ there is nothing to bind, and an empty table
    /// would read as a rendering bug.</summary>
    [Fact]
    public void ASubtreeWithNoVariables_SaysSo()
        => Refusal(WiredTo(_ => new SubAsset(Array.Empty<BlackboardVariableEntry>())),
                   new SyncAsset(subtreeNode: true))
            .Should().Contain("no blackboard variables");

    /// <summary>⛔⛔ <b>The four are DISTINCT sentences.</b> ⚠ The anti-vacuity half: collapsing them into
    /// one message would satisfy every rail above individually and destroy the whole point.</summary>
    [Fact]
    public void TheFourRefusalsAreAllDifferent()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal)
        {
            Refusal(Wired(),                       new SyncAsset(subtreeNode: true, resolved: false)),
            Refusal(new ParameterSyncSource(),     new SyncAsset(subtreeNode: true)),
            Refusal(WiredTo(_ => null),            new SyncAsset(subtreeNode: true)),
            Refusal(WiredTo(_ => new SubAsset(Array.Empty<BlackboardVariableEntry>())),
                                                   new SyncAsset(subtreeNode: true)),
        };

        seen.Should().HaveCount(4);
    }

    // ══ the happy path ═══════════════════════════════════════════════════════

    /// <summary>⭐⭐⭐ <b>Everything wired ⇒ a table-ready model</b> carrying the callee's variables and
    /// the master asset the checkboxes write back to.</summary>
    [Fact]
    public void FullyWired_YieldsTheTable()
    {
        var model = Wired().ModelFor(ContextWith(new SyncAsset(subtreeNode: true),
                                                 new BTreeNodeSelection(NodeId)));

        model.Should().NotBeNull();
        model!.Refusal.Should().BeNull();
        model.NodeVisualId.Should().Be(NodeId);
        model.SubVariables.Should().ContainSingle().Which.Name.Should().Be("Health");
        model.SyncAsset.Should().NotBeNull("the checkboxes call SetSyncBinding on it");
    }

    /// <summary>⭐ The predicate and the draw ask ONE question — 📌 the lesson `NodePropertiesSource`
    /// records: two answers to *"is there anything to show?"* produce a view that claims the panel and
    /// renders nothing.</summary>
    [Fact]
    public void CanShowAgreesWithModelFor()
    {
        var source = Wired();
        var ctx    = ContextWith(new SyncAsset(subtreeNode: true), new BTreeNodeSelection(NodeId));

        source.CanShow(ctx).Should().Be(source.ModelFor(ctx) is not null);
        source.CanShow(ContextWith(null)).Should().BeFalse();
    }

    // ══ the descriptor ═══════════════════════════════════════════════════════

    /// <summary>⭐ §7.6 ④'s id, and Rank 15 — ⛔ BELOW node properties (20) on purpose: selecting a
    /// subtree node, a designer most often means *"what is this node"*, and `R-98` makes the toolbar pick
    /// sticky so parameter wiring is chosen once.</summary>
    [Fact]
    public void TheDescriptorCarriesTheDesignsIdAndRank()
    {
        var d = ParameterSyncDetailsViewDescriptor.For(Wired());

        d.Id.Should().Be("details.parametersync");
        d.Rank.Should().Be(15);
        d.Rank.Should().BeLessThan(NodePropertiesDetailsViewDescriptor.Rank);
    }

    /// <summary>⛔ The source is REQUIRED — a descriptor without one would offer a view that can never
    /// answer.</summary>
    [Fact]
    public void TheDescriptorRefusesToExistWithoutASource()
        => Assert.Throws<ArgumentNullException>(() => ParameterSyncDetailsViewDescriptor.For(null!));

    // ── helpers ─────────────────────────────────────────────────────────────

    private static readonly Guid NodeId = Guid.NewGuid();

    private static ParameterSyncSource Wired()
        => WiredTo(_ => new SubAsset(new[] { new BlackboardVariableEntry("Health", typeof(float), null) }));

    private static ParameterSyncSource WiredTo(Func<Guid, IBlackboardManagedAsset?> resolve)
    {
        var s = new ParameterSyncSource();
        s.SetSubAssetResolver(resolve);
        return s;
    }

    private static string Refusal(ParameterSyncSource source, SyncAsset asset)
    {
        var model = source.ModelFor(ContextWith(asset, new BTreeNodeSelection(NodeId)));
        model.Should().NotBeNull("a subtree node claims the panel even when it must refuse");
        model!.Refusal.Should().NotBeNull();
        return model.Refusal!;
    }

    private static DetailsContext ContextWith(IEditableAsset? asset, params IAssetSubSelection[] selection)
        => new(
            Focus:       SelectionOrigin.GraphCanvas,
            Selection:   selection,
            Entities:    Array.Empty<Fdp.Core.Entity>(),
            Asset:       asset,
            Perspective: "BehaviorTree",
            Mode:        VariableRunState.Planning);

    // ── fakes ───────────────────────────────────────────────────────────────

    private sealed class SyncAsset : IEditableAsset, IBTreeSyncableAsset
    {
        private readonly bool _subtreeNode;
        private readonly bool _resolved;
        private readonly Dictionary<Guid, List<SubtreeSyncBinding>> _bindings = new();

        public SyncAsset(bool subtreeNode, bool resolved = true)
        { _subtreeNode = subtreeNode; _resolved = resolved; }

        public SubtreeNodeInfo? GetSubtreeNodeInfo(Guid nodeVisualId)
            => _subtreeNode ? new SubtreeNodeInfo(_resolved, Guid.NewGuid()) : null;

        public IReadOnlyList<SubtreeSyncBinding> GetSyncBindings(Guid n)
            => _bindings.TryGetValue(n, out var l) ? l : Array.Empty<SubtreeSyncBinding>();

        public void SetSyncBinding(Guid n, SubtreeSyncBinding b)
        {
            if (!_bindings.TryGetValue(n, out var l)) _bindings[n] = l = new();
            l.RemoveAll(x => x.FieldName == b.FieldName);
            l.Add(b);
        }

        public IReadOnlyList<BlackboardVariableEntry> GetVariablesOfType(string typeName)
            => Array.Empty<BlackboardVariableEntry>();

        public void RecordSubtreeNodeMeta(Guid n, string a, string b, string? c) { }
        public void ClearSyncBindings(Guid n) => _bindings.Remove(n);
        public IReadOnlyList<ApproachBSyncGroup> GetApproachBSyncGroups()
            => Array.Empty<ApproachBSyncGroup>();
        public IReadOnlyList<BlackboardVariableEntry> GetAutoAllocatedVariables()
            => Array.Empty<BlackboardVariableEntry>();

        public Guid      AssetId        { get; } = Guid.NewGuid();
        public string    Name           => "MasterAI";
        public AssetKind Kind           => AssetKind.BTree;
        public string    SourceFilePath => "/master.btree.json";
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => true;
        public event Action? Changed { add { } remove { } }
    }

    private sealed class SubAsset : IEditableAsset, IBlackboardManagedAsset
    {
        private readonly List<BlackboardVariableEntry> _vars;
        public SubAsset(IReadOnlyList<BlackboardVariableEntry> vars) => _vars = new(vars);

        public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _vars;
        public string BlackboardTypeName => "Hrot.Game.ShootBlackboard";

        public bool IsBlackboardEditorManaged => true;
        public void SetBlackboardEditorManaged(bool managed) { }
        public void AddVariable(BlackboardVariableEntry entry) => _vars.Add(entry);
        public void RemoveVariable(string name) => _vars.RemoveAll(v => v.Name == name);
        public void RemoveVariables(IReadOnlyList<string> names) { }
        public void UpdateVariableComment(string name, string? comment) { }
        public void UpdateVariableDefaultValueJson(string name, string? json) { }
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public void RenameVariable(string oldName, string newName) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName)
            => Array.Empty<BlackboardAliasBinding>();
        public void AddAlias(string variableName, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }

        public Guid      AssetId        { get; } = Guid.NewGuid();
        public string    Name           => "ShootBT";
        public AssetKind Kind           => AssetKind.BTree;
        public string    SourceFilePath => "/shoot.btree.json";
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => true;
        public event Action? Changed { add { } remove { } }
    }
}
