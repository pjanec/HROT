using System;
using System.Text.Json.Nodes;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.DebugApi;
using Xunit;

using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>CE-4094</c> (AQ108 M7 / D22) — the EDITOR-shaped <see cref="DebugApiService"/> (no dispatcher): <c>GET /status</c> reads the window manager
/// through the perspective seam — <c>perspective</c> was <c>null</c> here before — and <c>routedTo</c> is the perspective, or the REMEMBERED one while
/// <c>Start</c> shows; <c>POST /perspective</c> (service level) answers the window manager's refusal with the 409 marker while <c>Start</c> shows.
/// 🔴 Mutations: <c>["perspective"]</c> back to <c>_dispatcher?.CurrentPerspective</c> only ⇒ null ⇒ red; <c>RoutedToFromSwitcher</c> returning
/// <c>CurrentPerspective</c> unconditionally ⇒ "Start" ⇒ red; the service calling <c>SwitchPerspective</c> (void) instead of <c>TrySwitchPerspective</c> ⇒ no refusal ⇒ red.
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class DebugApiStartPerspectiveTests
{
    private sealed class Win : ManagedWindow
    {
        public Win(string id, string perspective) : base(id, id, perspective, WindowScope.PerspectiveBound) { }
        protected override void DrawClientArea() { }
    }

    [Fact]
    public void TheEditorsStatus_ReportsThePerspective_AndWhileStartShows_RoutedToIsTheRememberedOne_AndSwitchingIsRefused()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        var wm = new WM(new IconAtlas(IntPtr.Zero, 16f, 16f));
        wm.RegisterWindow(new Win("w-scenario", "Scenario"));
        wm.RegisterWindow(new Win("w-btree", "BTree"));
        wm.RegisterWindow(new Win("w-start", WM.StartPerspectiveName));
        wm.SwitchPerspective("BTree");
        svc.AttachPerspectives(new WindowManagerPerspectiveSwitcher(wm));

        var status = svc.GetStatus().AsObject();
        Assert.Equal("BTree", status["perspective"]!.GetValue<string>());
        Assert.Equal("BTree", status["routedTo"]!.GetValue<string>());

        wm.SwitchPerspective(WM.StartPerspectiveName);

        status = svc.GetStatus().AsObject();
        Assert.Equal("Start", status["perspective"]!.GetValue<string>());
        Assert.Equal("BTree", status["routedTo"]!.GetValue<string>());

        var (result, error, _) = svc.SwitchPerspective(JsonNode.Parse("""{"name":"Scenario"}"""));
        Assert.Null(result);
        Assert.StartsWith(DebugApiService.RefusedPrefix, error);
        Assert.Contains("Start", error);
        Assert.Equal("Start", wm.CurrentPerspective);
    }

    /// <summary>
    /// ⭐ <c>CE-4054</c> (second half) — <c>GET /status</c> through the REAL handler, over the editor's real preview controller: <c>inPreview</c> is
    /// true exactly while the cluster is in the dry run's state, and false in a live run, a replay, a review and the edit state.
    /// 🔴 Red-proof: the controller's <c>IsInPreviewMode</c> widened to also hold in <c>OperatingLive</c> ⇒ the Live row fails.
    /// </summary>
    [Theory]
    [InlineData(Fdp.Toolkit.Orchestration.ClusterState.OperatingPreview, true)]
    [InlineData(Fdp.Toolkit.Orchestration.ClusterState.OperatingLive,    false)]
    [InlineData(Fdp.Toolkit.Orchestration.ClusterState.OperatingReplay,  false)]
    [InlineData(Fdp.Toolkit.Orchestration.ClusterState.OperatingReview,  false)]
    [InlineData(Fdp.Toolkit.Orchestration.ClusterState.OperatingEdit,    false)]
    public void CE4054_GetStatus_InPreview_IsTrueOnlyInTheDryRun(Fdp.Toolkit.Orchestration.ClusterState state, bool expected)
    {
        using var h = new EditorHarness();
        var controller = new Hrot.Editor.EditorSubsystem.EditorPreviewController(
            new Fdp.Toolkit.Orchestration.Handlers.ReferencePreviewHandler(null), () => h.Bus, () => state,
            new Fdp.Toolkit.Time.IntentTimeCommands(h.Bus));
        var svc = h.BuildDebugApiService(preview: controller, clusterState: () => state);

        var status = svc.GetStatus().AsObject();
        Assert.Equal(state.ToString(), status["clusterState"]!.GetValue<string>());
        Assert.Equal(expected, status["inPreview"]!.GetValue<bool>());
    }
}
