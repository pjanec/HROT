using System;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>DetailsViewWindow</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class DetailsViewWindowDumpsItsStateTests : IDisposable
{
    private sealed class StubViewInstance : IDetailsViewInstance
    {
        public void Draw(DetailsContext context, string idScope) { }
        public void Dispose() { }
    }

    public DetailsViewWindowDumpsItsStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static DetailsViewDescriptor MakeDescriptor(string viewId, bool applies)
        => new(viewId, "My View", Rank: 0, AppliesTo: _ => applies, Create: () => new StubViewInstance());

    private static DetailsViewWindow MakeWindow(string id, string viewId, DetailsContext ctx, bool applies)
        => new(id, "My View", "BTree", MakeDescriptor(viewId, applies), new FrozenContextSource(ctx), isVolatile: false);

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        const string id = "details_view_rail1";
        Assert.DoesNotContain(id, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = MakeWindow(id, "blackboard", DetailsContext.Empty("BTree"), applies: true);

        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(id, PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet(id));
        Assert.NotNull(window);
    }

    [Fact]
    public void AfterABuild_TheDumpCarriesTheViewIdAndApplies()
    {
        const string id = "details_view_rail2";
        PanelSnapshot.CaptureEnabled = true;
        var window = MakeWindow(id, "blackboard", DetailsContext.Empty("BTree"), applies: true);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet(id);
        Assert.NotNull(vm);
        Assert.Equal(id,           vm!.PanelId);
        Assert.Equal("blackboard", vm.PanelKind);

        var dump = vm.Dump();
        Assert.True(dump["applies"]!.GetValue<bool>());
        Assert.Null(dump["emptyState"]);
        Assert.Equal("BTree", dump["perspective"]!.GetValue<string>());
    }

    [Fact]
    public void WhenThePredicateRejects_TheDumpCarriesTheEmptyState()
    {
        const string id = "details_view_rail4";
        PanelSnapshot.CaptureEnabled = true;
        var window = MakeWindow(id, "blackboard", DetailsContext.Empty("BTree"), applies: false);

        window.SimulateDrawClientArea();

        var dump = PanelSnapshot.TryGet(id)!.Dump();
        Assert.False(dump["applies"]!.GetValue<bool>());
        Assert.NotNull(dump["emptyState"]);
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        const string id = "details_view_rail3";
        var window = MakeWindow(id, "blackboard", DetailsContext.Empty("BTree"), applies: true);   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);
        Assert.True(vm.Applies);
    }
}
