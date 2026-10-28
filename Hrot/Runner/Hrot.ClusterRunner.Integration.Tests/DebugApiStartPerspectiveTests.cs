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
}
