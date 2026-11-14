using Hrot.Editor.AiShared.Selection;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Selection;

/// <summary>
/// ⭐⭐⭐ <b>Batch 87 item 2d (<c>B8</c>) — WHICH SURFACE owns the Details panel.</b>
///
/// <para>📌 <b>User ruling, <c>2026-08-18</c>:</b> <i>"it's not the selection what changes but actually
/// the focus to different part of the UI (from MyBlueprint to graph canvas)… the editor selection
/// cache should contain what the selected UI item comes from (what panel etc.). Otherwise we would
/// need to report and handle the click to every possible UI component."</i></para>
///
/// <para>🔴🔴 <b>Why the old test could not work.</b> <c>ShowingVariables</c> compared
/// <c>ActiveSubSelection</c> to a snapshot — a VALUE test standing in for a TIME claim. Re-clicking
/// the SAME node is <c>Equals</c> to the snapshot ⇒ ⛔ <b>it could never reclaim the panel</b>, and
/// that is the gesture a designer actually performs.</para>
///
/// <para>⛔⛔ <b>And "detect the re-click" was measured impossible</b>, at four layers:
/// <c>CanvasInput</c> guards its assignment with <c>!Selection.Contains(node)</c> — clicking an
/// already-selected node is a DELIBERATE no-op, so dragging a multi-selection does not collapse it;
/// <c>SelectionState</c> is a plain set with no version and no event; the bridge assigns every frame;
/// the store short-circuits on <c>Equals</c>. ⇒ ⭐⭐ <b>focus is the only honest signal</b>, and it is
/// observable every frame.</para>
///
/// <para>⭐ <b>Shared across Blueprint, BTree and HSM</b> *(<c>Q32</c> ruling 6)*, which is why the
/// latch lives on the store rather than in any host's window.</para>
/// </summary>
public sealed class TheFocusedSurfaceOwnsTheDetailsPanelTests
{
    /// <summary>⛔ Nobody has claimed it — ⭐ the <c>Unresolved = 0</c> shape, so a store that was never
    /// told cannot impersonate a surface.</summary>
    [Fact]
    public void NoSurfaceHasClaimedItByDefault()
        => Assert.Equal(SelectionOrigin.Unknown, new EditorSelectionStore().FocusedSurface);

    /// <summary>⭐⭐ A claim latches.</summary>
    [Fact]
    public void AClaimLatches()
    {
        var store = new EditorSelectionStore();

        store.NotifySurfaceFocused(SelectionOrigin.VariableOutline);

        Assert.Equal(SelectionOrigin.VariableOutline, store.FocusedSurface);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE gesture, as the designer performs it.</b> Canvas → outline → <b>the SAME canvas
    /// again</b>. 🔴 The last step is what the value test could never see.
    /// </summary>
    [Fact]
    public void ReturningToTheSameSurfaceReclaimsIt()
    {
        var store = new EditorSelectionStore();

        store.NotifySurfaceFocused(SelectionOrigin.GraphCanvas);      // click node N
        store.NotifySurfaceFocused(SelectionOrigin.VariableOutline);  // click a variable
        Assert.Equal(SelectionOrigin.VariableOutline, store.FocusedSurface);

        store.NotifySurfaceFocused(SelectionOrigin.GraphCanvas);      // click node N AGAIN
        Assert.Equal(SelectionOrigin.GraphCanvas, store.FocusedSurface);
    }

    /// <summary>
    /// ⭐⭐ <b>The claim is a LEVEL, so repeating it every frame is free and changes nothing.</b>
    /// ⛔ An EDGE would need a change to detect, and "no change" is exactly the failing case.
    /// </summary>
    [Fact]
    public void RepeatingAClaimIsIdempotent()
    {
        var store   = new EditorSelectionStore();
        var changes = 0;
        store.OnSelectionChanged += () => changes++;

        store.NotifySurfaceFocused(SelectionOrigin.GraphCanvas);
        store.NotifySurfaceFocused(SelectionOrigin.GraphCanvas);
        store.NotifySurfaceFocused(SelectionOrigin.GraphCanvas);

        Assert.Equal(SelectionOrigin.GraphCanvas, store.FocusedSurface);
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// ⚠⚠ <b>A non-contributing surface cannot steal the panel.</b> Clicking INTO the Details panel to
    /// edit a value takes focus from both contributors — ⛔ and must leave the latch alone, or the
    /// panel would flip out from under the designer mid-edit. ⭐ Expressed as: <c>Unknown</c> is
    /// ignored, and only surfaces that opt into <c>IDetailsSurfaceClaimant</c> ever notify.
    /// </summary>
    [Fact]
    public void AnUnknownClaimIsIgnored()
    {
        var store = new EditorSelectionStore();
        store.NotifySurfaceFocused(SelectionOrigin.VariableOutline);

        store.NotifySurfaceFocused(SelectionOrigin.Unknown);

        Assert.Equal(SelectionOrigin.VariableOutline, store.FocusedSurface);
    }

    /// <summary>⭐ The ORIGIN travels with the selection — the durable half, distinct from the latch.</summary>
    [Fact]
    public void TheSelectionCarriesItsOrigin()
    {
        var store = new EditorSelectionStore();
        Assert.Equal(SelectionOrigin.Unknown, store.ActiveSubSelectionOrigin);

        store.SetActiveSubSelection(null, SelectionOrigin.GraphCanvas);

        Assert.Equal(SelectionOrigin.GraphCanvas, store.ActiveSubSelectionOrigin);
    }
}
