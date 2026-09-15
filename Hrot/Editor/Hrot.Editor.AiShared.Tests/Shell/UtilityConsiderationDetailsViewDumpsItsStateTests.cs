using System;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 2) — <c>UtilityConsiderationDetailsView</c> converted to the
/// <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example;
/// <c>BP-462</c>.
///
/// <para>⛔⛔ <b>CORRECTED ORDER, like the design's own AS-BUILT ①</b> — the original body opened with
/// the ImGui-context guard, so a headless call never reached <c>Describe</c> at all. This rail is what
/// would have reddened that.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class UtilityConsiderationDetailsViewDumpsItsStateTests : IDisposable
{
    public UtilityConsiderationDetailsViewDumpsItsStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private static DetailsContext WithSelection(UtilityConsiderationSelection sel) =>
        DetailsContext.Empty("BTree") with { Selection = new[] { sel } };

    private static string Addr(string idScope) =>
        $"{idScope}/{UtilityConsiderationDetailsViewDescriptor.ViewId}";

    [Fact]
    public void FirstDraw_DeclaresItInstrumented_AtTheComposedAddress_EvenHeadless()
    {
        var view = new UtilityConsiderationDetailsView();
        var ctx  = WithSelection(new UtilityConsiderationSelection(2, 5));
        var addr = Addr("host1");
        Assert.DoesNotContain(addr, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        view.SimulateDraw(ctx, "host1");

        Assert.Contains(addr, PanelSnapshot.RegisteredPanels);
    }

    [Fact]
    public void AfterABuild_TheDumpCarriesTheDescription()
    {
        PanelSnapshot.CaptureEnabled = true;
        var view = new UtilityConsiderationDetailsView();
        var ctx  = WithSelection(new UtilityConsiderationSelection(2, 5));

        view.SimulateDraw(ctx, "host1");

        var stored = PanelSnapshot.TryGet(Addr("host1"));
        Assert.NotNull(stored);
        Assert.Equal(Addr("host1"), stored!.PanelId);
        Assert.Equal(UtilityConsiderationDetailsViewDescriptor.ViewId, stored.PanelKind);

        var dump = stored.Dump();
        Assert.Equal("Option 2, Consideration 5", dump["description"]!.GetValue<string>());
    }

    [Fact]
    public void WithNoConsiderationSelected_TheDescriptionIsNull()
    {
        PanelSnapshot.CaptureEnabled = true;
        var view = new UtilityConsiderationDetailsView();

        var vm = view.SimulateDraw(DetailsContext.Empty("BTree"), "host1");

        Assert.Null(vm.Description);
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing_ButStaysRegistered()
    {
        var view = new UtilityConsiderationDetailsView();   // CaptureEnabled stays false
        var ctx  = WithSelection(new UtilityConsiderationSelection(1, 1));

        var vm = view.SimulateDraw(ctx, "host1");

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(Addr("host1"), PanelSnapshot.RegisteredPanels);
        Assert.Equal("Option 1, Consideration 1", vm.Description);
    }
}
