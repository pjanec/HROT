using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Xunit;

using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Fdp.Presentation.Tests.WindowManager;

/// <summary>
/// Tests for <see cref="WM.RegisterPerspectiveLabel"/> and
/// <see cref="WM.GetPerspectiveLabel"/> display-label overrides (MTB2-T5, BATCH-34).
/// The label is used in <see cref="WM.RenderPerspectiveMenu"/> for the item text;
/// <see cref="WM.SelectPerspective"/> continues to use the raw perspective id.
/// </summary>
[Collection("ImGui Sequential")]
public class PerspectiveLabelTests : IDisposable
{
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);

    public void Dispose() => _atlas.Dispose();

    private WM CreateManager() => new(_atlas);

    [Fact]
    public void PerspectiveLabel_OverridesDisplay_NotId()
    {
        var wm = CreateManager();

        wm.RegisterPerspectiveLabel("Editor", "Scenario");

        // Label overrides for the registered perspective.
        Assert.Equal("Scenario", wm.GetPerspectiveLabel("Editor"));

        // Unregistered perspectives fall back to the id.
        Assert.Equal("BTree", wm.GetPerspectiveLabel("BTree"));
    }

    /// <summary>
    /// ⭐⭐ Amended by <c>A0</c> (<c>2026-08-23</c>): a window now has to CLAIM the id, because
    /// <c>SwitchPerspective</c> refuses an unclaimed one. ⭐ That strengthens the point rather than
    /// weakening it — the LABEL is still not a perspective even though a window claims the id.
    /// </summary>
    [Fact]
    public void SelectPerspective_UsesId_NotLabel()
    {
        var wm = CreateManager();
        wm.RegisterWindow(new ClaimingWindow("w_editor", "Editor"));

        wm.RegisterPerspectiveLabel("Editor", "Scenario");

        // SelectPerspective uses the id, not the label.
        wm.SelectPerspective("Editor");

        Assert.True(wm.IsPerspectiveActive("Editor"));
        // The label "Scenario" is NOT a valid perspective.
        Assert.False(wm.IsPerspectiveActive("Scenario"));
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>A0</c> — AN UNKNOWN PERSPECTIVE IS REFUSED, and the label is not a back door.</b>
    /// 📄 <c>DESIGN_Perspective_Unification.md</c> §3 <c>A0</c>.
    ///
    /// <para>⛔ Without this, a stored or hand-edited name that no window claims is accepted, every
    /// <see cref="WindowScope.PerspectiveBound"/> window fails its visibility gate, and 🔴 the UI comes
    /// up blank with no error and no log line.</para>
    /// </summary>
    [Fact]
    public void SwitchPerspective_RefusesAnUnclaimedName()
    {
        var wm = CreateManager();
        var claimed = new ClaimingWindow("w_scenario", "Scenario");
        wm.RegisterWindow(claimed);
        wm.SelectPerspective("Scenario");
        wm.ShowWindow("w_scenario");

        wm.SwitchPerspective("NoSuchPerspective");

        // ⭐ Unchanged — the refusal is a no-op, not a partial switch.
        Assert.Equal("Scenario", wm.CurrentPerspective);
        // ⭐⭐ And the window that WAS visible still is. 📌 THIS is the defect being railed: had the switch
        //   been accepted, the visibility gate (Global || IsPinned || owning == current) would have gone
        //   false for every perspective-bound window and the UI would have come up blank.
        Assert.True(claimed.IsOpen);
        Assert.Equal(wm.CurrentPerspective, claimed.OwningPerspective);
        Assert.False(claimed.IsPinned);   // ⛔ not saved by an accidental pin
        Assert.Contains("Scenario", wm.GetPerspectives());
        Assert.DoesNotContain("NoSuchPerspective", wm.GetPerspectives());
    }

    /// <summary>
    /// ⚠ <c>A0</c> — a DISPLAY LABEL never makes a perspective real. 📌 The label mechanism exists so the
    /// id can stay stable while the menu reads well; ⛔ it is not a second registry.
    /// </summary>
    [Fact]
    public void ALabelDoesNotMakeAPerspectiveSwitchable()
    {
        var wm = CreateManager();
        wm.RegisterWindow(new ClaimingWindow("w_editor", "Editor"));
        wm.RegisterPerspectiveLabel("Editor", "Scenario");
        wm.SelectPerspective("Editor");

        wm.SwitchPerspective("Scenario");   // the LABEL, not the id

        Assert.Equal("Editor", wm.CurrentPerspective);
    }

    private sealed class ClaimingWindow : ManagedWindow
    {
        public ClaimingWindow(string id, string perspective)
            : base(id, id, perspective, WindowScope.PerspectiveBound) { }

        protected override void DrawClientArea() { }
    }
}
