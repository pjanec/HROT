using System;
using System.Collections.Generic;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 2) — <c>ParameterSyncDetailsView</c> converted to the <c>PanelSnapshot</c>
/// contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example; <c>BP-462</c>.
///
/// <para>⛔⛔ <b>CORRECTED ORDER, like the design's own AS-BUILT ①</b> — the original body opened with
/// the ImGui-context guard, so a headless call never reached <c>Model(context)</c> at all.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class ParameterSyncDetailsViewDumpsItsStateTests : IDisposable
{
    private static readonly Guid NodeId = Guid.NewGuid();

    /// <summary>⭐ Minimal fake — a subtree node whose reference is UNRESOLVED, so
    /// <c>ParameterSyncSource.ModelFor</c> yields a REFUSAL without needing a sub-asset resolver.</summary>
    private sealed class UnresolvedSyncAsset : IEditableAsset, IBTreeSyncableAsset
    {
        public SubtreeNodeInfo? GetSubtreeNodeInfo(Guid nodeVisualId) => new(false, Guid.NewGuid());
        public IReadOnlyList<SubtreeSyncBinding> GetSyncBindings(Guid n) => Array.Empty<SubtreeSyncBinding>();
        public void SetSyncBinding(Guid n, SubtreeSyncBinding b) { }
        public IReadOnlyList<BlackboardVariableEntry> GetVariablesOfType(string typeName) => Array.Empty<BlackboardVariableEntry>();
        public void RecordSubtreeNodeMeta(Guid n, string a, string b, string? c) { }
        public void ClearSyncBindings(Guid n) { }
        public IReadOnlyList<ApproachBSyncGroup> GetApproachBSyncGroups() => Array.Empty<ApproachBSyncGroup>();
        public IReadOnlyList<BlackboardVariableEntry> GetAutoAllocatedVariables() => Array.Empty<BlackboardVariableEntry>();

        public Guid      AssetId        { get; } = Guid.NewGuid();
        public string    Name           => "MasterAI";
        public AssetKind Kind           => AssetKind.BTree;
        public string    SourceFilePath => "/master.btree.json";
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => true;
        public event Action? Changed { add { } remove { } }
    }

    public ParameterSyncDetailsViewDumpsItsStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static DetailsContext RefusalContext() =>
        DetailsContext.Empty("BTree") with
        {
            Selection = new IAssetSubSelection[] { new BTreeNodeSelection(NodeId) },
            Asset     = new UnresolvedSyncAsset(),
        };

    private static string Addr(string idScope) =>
        $"{idScope}/{ParameterSyncDetailsViewDescriptor.ViewId}";

    [Fact]
    public void FirstDraw_DeclaresItInstrumented_AtTheComposedAddress_EvenHeadless()
    {
        var view = new ParameterSyncDetailsView(new ParameterSyncSource());
        var addr = Addr("host1");
        Assert.DoesNotContain(addr, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        view.SimulateDraw(RefusalContext(), "host1");

        Assert.Contains(addr, PanelSnapshot.RegisteredPanels);
    }

    [Fact]
    public void AfterABuild_TheDumpCarriesTheRefusal()
    {
        PanelSnapshot.CaptureEnabled = true;
        var view = new ParameterSyncDetailsView(new ParameterSyncSource());

        view.SimulateDraw(RefusalContext(), "host1");

        var stored = PanelSnapshot.TryGet(Addr("host1"));
        Assert.NotNull(stored);
        Assert.Equal(Addr("host1"), stored!.PanelId);
        Assert.Equal(ParameterSyncDetailsViewDescriptor.ViewId, stored.PanelKind);

        var dump = stored.Dump();
        Assert.Equal("Subtree not resolved -- sync unavailable.", dump["refusal"]!.GetValue<string>());
        Assert.Equal(0, dump["subVariableCount"]!.GetValue<int>());
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing_ButStaysRegistered()
    {
        var view = new ParameterSyncDetailsView(new ParameterSyncSource());   // CaptureEnabled stays false

        var vm = view.SimulateDraw(RefusalContext(), "host1");

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(Addr("host1"), PanelSnapshot.RegisteredPanels);
        Assert.Equal("Subtree not resolved -- sync unavailable.", vm.Refusal);
    }
}
