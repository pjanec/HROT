using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 3) — <c>BlueprintNodeDetailsView</c> converted to the <c>PanelSnapshot</c>
/// contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example; <c>BP-462</c>.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class BlueprintNodeDetailsViewDumpsItsStateTests : System.IDisposable
{
    private sealed class StubDrawer<T> : IBlueprintNodeDrawer where T : Node
    {
        public bool Handles(Node node) => node is T;
        public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset) => new StubSession();
    }

    private sealed class StubSession : INodeEditSession
    {
        public bool IsDirty => false;
        public void Draw() { }
        public void ResetDirty() { }
        public void Dispose() { }
    }

    private sealed class FakeEditableAsset : Hrot.Editor.AiShared.IEditableAsset
    {
        public Guid   AssetId        { get; }
        public string Name           => "";
        public Hrot.Editor.AiShared.AssetKind Kind => Hrot.Editor.AiShared.AssetKind.Blueprint;
        public string SourceFilePath => "";
        public bool   IsDirty        => false;
        public bool   IsEditorOwned  => false;
        public event System.Action? Changed;
        public FakeEditableAsset(Guid id) { AssetId = id; }
    }

    private static DetailsContext NodeSelected(Guid graphId, Guid nodeId, Guid assetId)
        => new(SelectionOrigin.GraphCanvas,
               new IAssetSubSelection[] { new BlueprintNodeSelection(graphId, nodeId) },
               Array.Empty<Fdp.Core.Entity>(),
               new FakeEditableAsset(assetId),
               "Blueprint",
               Hrot.Editor.AiShared.Variables.VariableRunState.Planning);

    private static BlueprintNodeDrawerRegistry MakeRegistry()
    {
        var registry = new BlueprintNodeDrawerRegistry();
        registry.Register(typeof(WhenNode), new StubDrawer<WhenNode>());
        return registry;
    }

    private static string Addr(string idScope) =>
        $"{idScope}/{BlueprintNodeDetailsViewDescriptor.ViewId}";

    public BlueprintNodeDetailsViewDumpsItsStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    [Fact]
    public void FirstDraw_DeclaresItInstrumented_AtTheComposedAddress()
    {
        var view = new BlueprintNodeDetailsView(() => null, MakeRegistry());
        Assert.DoesNotContain(Addr("host1"), PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        view.SimulateDraw(DetailsContext.Empty("Blueprint"), "host1");

        Assert.Contains(Addr("host1"), PanelSnapshot.RegisteredPanels);
    }

    [Fact]
    public void AfterASelectedNodeWithADrawer_TheDumpCarriesTheResolvedDrawerKind()
    {
        PanelSnapshot.CaptureEnabled = true;
        var asset    = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "TestBP" };
        var graphId  = Guid.NewGuid();
        var nodeId   = Guid.NewGuid();
        var graph    = new Graph { Id = graphId, Name = "EventGraph" };
        graph.Nodes.Add(new WhenNode { Id = nodeId });
        asset.Graphs.Add(graph);

        var view = new BlueprintNodeDetailsView(() => asset, MakeRegistry());
        var ctx  = NodeSelected(graphId, nodeId, asset.AssetId);

        var vm = view.SimulateDraw(ctx, "host1");

        Assert.Equal(Addr("host1"), vm.PanelId);
        Assert.Equal(BlueprintNodeDetailsViewDescriptor.ViewId, vm.PanelKind);
        Assert.True(vm.HasSession);
        Assert.Equal("StubDrawer`1", vm.ResolvedDrawerKindName);

        var dump = PanelSnapshot.TryGet(Addr("host1"))!.Dump();
        Assert.True(dump["hasSession"]!.GetValue<bool>());
        Assert.False(dump["hasNodeWithNoDrawer"]!.GetValue<bool>());
    }

    [Fact]
    public void WithNothingSelected_NoSessionAndNoNode()
    {
        PanelSnapshot.CaptureEnabled = true;
        var view = new BlueprintNodeDetailsView(() => null, MakeRegistry());

        var vm = view.SimulateDraw(DetailsContext.Empty("Blueprint"), "host1");

        Assert.False(vm.HasSession);
        Assert.False(vm.HasNodeWithNoDrawer);
        Assert.Null(vm.ResolvedDrawerKindName);
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing_ButStaysRegistered()
    {
        var view = new BlueprintNodeDetailsView(() => null, MakeRegistry());   // CaptureEnabled stays false

        var vm = view.SimulateDraw(DetailsContext.Empty("Blueprint"), "host1");

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(Addr("host1"), PanelSnapshot.RegisteredPanels);
        Assert.False(vm.HasSession);
    }
}
