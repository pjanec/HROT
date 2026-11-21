using System;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>DetailsWindow</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class DetailsWindowDumpsItsChoiceTests : IDisposable
{
    private sealed class Nothing : IDetailsViewInstance
    {
        public void Draw(DetailsContext context, string idScope) { }
        public void Dispose() { }
    }

    private static DetailsViewDescriptor View(string id, Func<DetailsContext, bool> applies)
        => new(id, id, 0, applies, () => new Nothing());

    public DetailsWindowDumpsItsChoiceTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static DetailsWindow MakeShell(string id, int viewCount)
    {
        var views = new DetailsViewRegistry();
        for (int i = 0; i < viewCount; i++) views.Add(View($"v{i}", _ => true));

        var store = new EditorSelectionStore();
        return new DetailsWindow(
            id:                id,
            owningPerspective: "Scenario",
            formatter:         new VariableValueFormatter(RawValueDecoder.Instance),
            views:             views,
            context:           new LiveContextSource(() => DetailsContextBuilder.Build(
                                   store, "Scenario", VariableRunState.Planning)));
    }

    [Fact]
    public void ConstructingTheShell_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        const string id = "details_rail1";
        Assert.DoesNotContain(id, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var shell = MakeShell(id, viewCount: 1);

        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(id, PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet(id));
        Assert.NotNull(shell);
    }

    [Fact]
    public void AfterABuild_TheDumpCarriesTheChosenView()
    {
        const string id = "details_rail2";
        PanelSnapshot.CaptureEnabled = true;
        var shell = MakeShell(id, viewCount: 2);   // 2 views ⇒ ShowsViewSwitch true

        shell.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet(id);
        Assert.NotNull(vm);
        Assert.Equal(id,        vm!.PanelId);
        Assert.Equal(DetailsWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        Assert.Equal("v0", dump["chosenViewId"]!.GetValue<string>());
        Assert.Equal(2, dump["offeredViewIds"]!.AsArray().Count);
        Assert.Null(dump["emptyState"]);
        Assert.True(dump["showsViewSwitch"]!.GetValue<bool>());
        Assert.Equal("Scenario", dump["perspective"]!.GetValue<string>());
    }

    [Fact]
    public void WithNoOfferedView_TheDumpCarriesTheEmptyState()
    {
        const string id = "details_rail4";
        PanelSnapshot.CaptureEnabled = true;
        var shell = MakeShell(id, viewCount: 0);

        shell.SimulateDrawClientArea();

        var dump = PanelSnapshot.TryGet(id)!.Dump();
        Assert.Null(dump["chosenViewId"]);
        Assert.NotNull(dump["emptyState"]);
        Assert.False(dump["showsViewSwitch"]!.GetValue<bool>());
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        const string id = "details_rail3";
        var shell = MakeShell(id, viewCount: 1);   // CaptureEnabled stays false

        var vm = shell.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);
        Assert.Equal("v0", vm.ChosenViewId);
    }
}
