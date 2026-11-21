using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Debug;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 3) — <c>CallstackWindow</c>, <c>DebugPanelWindow</c> and
/// <c>PreferencesWindow</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
///
/// <para>⛔ All three are <c>BlueprintEditorWindowBase</c> — a family with NO window id
/// (📄 <c>PanelIds.cs</c>'s own remarks) — so each declares a literal address/kind, and each is a
/// singleton registered once (Callstack, Debug Panel) or, for <c>PreferencesWindow</c>, not currently
/// wired into production at all (a measured finding, reported rather than assumed away).</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class DebugFamilyPanelsDumpTheirStateTests : System.IDisposable
{
    public DebugFamilyPanelsDumpTheirStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    // ══ CallstackWindow ═══════════════════════════════════════════════════

    [Fact]
    public void Callstack_ConstructingDeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain(CallstackWindow.PanelId, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = new CallstackWindow(new MockDebugSession(), new EditorSelectionStore());

        Assert.Contains(CallstackWindow.PanelId, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(CallstackWindow.PanelId, PanelSnapshot.CapturedPanels);
        Assert.NotNull(window);
    }

    [Fact]
    public void Callstack_AfterABuild_TheDumpCarriesTheFramesArray()
    {
        PanelSnapshot.CaptureEnabled = true;
        var window = new CallstackWindow(new MockDebugSession(), new EditorSelectionStore());

        window.SimulateDrawUI();

        var stored = PanelSnapshot.TryGet(CallstackWindow.PanelId);
        Assert.NotNull(stored);
        Assert.Equal(CallstackWindow.PanelId, stored!.PanelId);
        Assert.Equal(CallstackWindow.PanelId, stored.PanelKind);

        var dump = stored.Dump();
        Assert.NotNull(dump["frames"]);
        Assert.Equal(0, dump["frames"]!.AsArray().Count);   // MockDebugSession's callstack is empty
    }

    [Fact]
    public void Callstack_WithCaptureOff_PublishesNothing_ButStaysRegistered()
    {
        var window = new CallstackWindow(new MockDebugSession(), new EditorSelectionStore());

        var vm = window.SimulateDrawUI();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(CallstackWindow.PanelId, PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm.Frames);
    }

    // ══ DebugPanelWindow ══════════════════════════════════════════════════

    [Fact]
    public void DebugPanel_ConstructingDeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain(DebugPanelWindow.PanelId, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = new DebugPanelWindow(new MockDebugSession());

        Assert.Contains(DebugPanelWindow.PanelId, PanelSnapshot.RegisteredPanels);
        Assert.NotNull(window);
    }

    [Fact]
    public void DebugPanel_AfterABuild_TheDumpCarriesThePausedState()
    {
        PanelSnapshot.CaptureEnabled = true;
        var session = new MockDebugSession { IsPaused = true };
        var window  = new DebugPanelWindow(session);

        window.SimulateDrawUI();

        var dump = PanelSnapshot.TryGet(DebugPanelWindow.PanelId)!.Dump();
        Assert.True(dump["isPaused"]!.GetValue<bool>());
        Assert.Equal(0, dump["breakpointCount"]!.GetValue<int>());
    }

    [Fact]
    public void DebugPanel_WithCaptureOff_PublishesNothing_ButStaysRegistered()
    {
        var window = new DebugPanelWindow(new MockDebugSession { IsPaused = true });

        var vm = window.SimulateDrawUI();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(DebugPanelWindow.PanelId, PanelSnapshot.RegisteredPanels);
        Assert.True(vm.IsPaused);
    }

    // ══ PreferencesWindow ═════════════════════════════════════════════════

    [Fact]
    public void Preferences_ConstructingDeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain(PreferencesWindow.PanelId, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = new PreferencesWindow(new BlueprintEditorPreferences(), "/tmp/prefs.json");

        Assert.Contains(PreferencesWindow.PanelId, PanelSnapshot.RegisteredPanels);
        Assert.NotNull(window);
    }

    [Fact]
    public void Preferences_AfterABuild_TheDumpCarriesTheRealFields()
    {
        PanelSnapshot.CaptureEnabled = true;
        var prefs = new BlueprintEditorPreferences { AutoReloadOnSave = true, HotReloadLogMaxEntries = 42 };
        var window = new PreferencesWindow(prefs, "/tmp/prefs.json");

        window.SimulateDrawUI();

        var dump = PanelSnapshot.TryGet(PreferencesWindow.PanelId)!.Dump();
        Assert.True(dump["autoReloadOnSave"]!.GetValue<bool>());
        Assert.Equal(42, dump["hotReloadLogMaxEntries"]!.GetValue<int>());
    }

    [Fact]
    public void Preferences_WithCaptureOff_PublishesNothing_ButStaysRegistered()
    {
        var prefs  = new BlueprintEditorPreferences { HotReloadLogMaxEntries = 7 };
        var window = new PreferencesWindow(prefs, "/tmp/prefs.json");

        var vm = window.SimulateDrawUI();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(PreferencesWindow.PanelId, PanelSnapshot.RegisteredPanels);
        Assert.Equal(7, vm.HotReloadLogMaxEntries);
    }
}
