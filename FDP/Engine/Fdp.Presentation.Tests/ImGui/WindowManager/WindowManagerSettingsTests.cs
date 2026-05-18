using System;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Xunit;

using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// Tests for WM-S401: settings persistence — serialize/deserialize round-trips.
/// All tests run without an ImGui context (pure state logic).
/// </summary>
public class WindowManagerSettingsTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);

    public void Dispose() => _atlas.Dispose();

    // ── Helper window ───────────────────────────────────────────────────────────

    private sealed class SettingsWindow(string id)
        : ManagedWindow(id, id, "Default", WindowScope.Global)
    {
        protected override void DrawClientArea() { }
    }

    private WM CreateManager() => new(_atlas);

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S401.2: Write output format
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SerializeToIniSection_ContainsLinePerWindowInCorrectFormat()
    {
        var wm = CreateManager();
        var w = new SettingsWindow("win-a");
        w.IsOpen   = true;
        w.IsPinned = false;
        wm.RegisterWindow(w);

        var result = wm.SerializeToIniSection();

        Assert.Contains("win-a=True,False", result);
    }

    [Fact]
    public void SerializeToIniSection_MultipleWindows_ContainsAllLines()
    {
        var wm = CreateManager();
        var w1 = new SettingsWindow("win-a"); w1.IsOpen = true;  w1.IsPinned = false; wm.RegisterWindow(w1);
        var w2 = new SettingsWindow("win-b"); w2.IsOpen = false; w2.IsPinned = true;  wm.RegisterWindow(w2);

        var result = wm.SerializeToIniSection();

        Assert.Contains("win-a=True,False",  result);
        Assert.Contains("win-b=False,True",  result);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S401.3: Round-trip — IsOpen = true, IsPinned = true
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RoundTrip_IsOpenTrue_IsPinnedTrue_RestoresBothValues()
    {
        var wm = CreateManager();
        var w = new SettingsWindow("w1");
        w.IsOpen   = true;
        w.IsPinned = true;
        wm.RegisterWindow(w);

        var data = wm.SerializeToIniSection();

        // Reset to opposite values before restoring
        w.IsOpen   = false;
        w.IsPinned = false;

        wm.DeserializeFromIniSection(data);

        Assert.True(w.IsOpen);
        Assert.True(w.IsPinned);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S401.4: Round-trip — IsOpen = false, IsPinned = false
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RoundTrip_IsOpenFalse_IsPinnedFalse_RestoresBothValues()
    {
        var wm = CreateManager();
        var w = new SettingsWindow("w2");
        w.IsOpen   = false;
        w.IsPinned = false;
        wm.RegisterWindow(w);

        var data = wm.SerializeToIniSection();

        // Set to opposite values before restoring
        w.IsOpen   = true;
        w.IsPinned = true;

        wm.DeserializeFromIniSection(data);

        Assert.False(w.IsOpen);
        Assert.False(w.IsPinned);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S401.5: Unknown id in read is silently skipped
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeserializeFromIniSection_UnknownId_DoesNotThrow()
    {
        var wm = CreateManager(); // no windows registered

        var ex = Record.Exception(() => wm.DeserializeFromIniSection("unknown-id=True,False"));

        Assert.Null(ex);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S401.6: Malformed line skipped — no comma in value
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeserializeFromIniSection_MalformedLine_NoComma_DoesNotThrow()
    {
        var wm = CreateManager();

        var ex = Record.Exception(() => wm.DeserializeFromIniSection("badline-no-equals"));

        Assert.Null(ex);
    }

    [Fact]
    public void DeserializeFromIniSection_MalformedValue_NoComma_DoesNotThrow()
    {
        var wm = CreateManager();
        var w = new SettingsWindow("w3");
        wm.RegisterWindow(w);

        // value has no comma — should be silently skipped, not throw
        var ex = Record.Exception(() => wm.DeserializeFromIniSection("w3=TrueFalse"));

        Assert.Null(ex);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // WM-S401.7: Late-registered window not affected by early read
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void LateRegisteredWindow_NotAffectedByEarlyDeserialize()
    {
        var wm = CreateManager();

        // Deserialize with data that references "late" window — but it is not yet registered
        wm.DeserializeFromIniSection("late=True,True");

        // Now register the "late" window — its default state must be preserved
        var late = new SettingsWindow("late");
        wm.RegisterWindow(late);

        // Default IsOpen/IsPinned are both false (per ManagedWindow constructor)
        Assert.False(late.IsOpen);
        Assert.False(late.IsPinned);
    }
}
