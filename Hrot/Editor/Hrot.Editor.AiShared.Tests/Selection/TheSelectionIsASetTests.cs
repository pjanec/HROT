using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.Editor.AiShared.Selection;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Selection;

/// <summary>
/// ⭐⭐⭐ <b><c>L0</c>'s rail — 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L0</c>, verbatim:</b>
/// <i>"a marquee of two yields a 2-item context; <b>a pan yields the same context object as the frame
/// before</b>."</i> 📌 <c>M-22</c>.
///
/// <para>⭐⭐ <b>Asserted on the STORE, not on a draw</b> — 📌 §6: <i>"every task's rail asserts on a
/// store or a returned model; the draw is unrailed by construction"</i> *(<c>R-21</c>/<c>R-62</c>)*.</para>
///
/// <para>⚠ <b>Why the identity half matters as much as the count half.</b> §6 <c>L0.4</c>: <i>"return
/// the same list instance when unchanged, or every view rebuilds per frame."</i> ⇒ ⛔ a store that
/// re-stored an equal-but-new list every frame would satisfy a count assertion and still repaint the
/// whole panel 60× a second.</para>
/// </summary>
public sealed class TheSelectionIsASetTests
{
    /// <summary>⭐ A sub-selection that is nothing but an identity — which is all the store compares.
    /// ⛔ Deliberately NOT one of the nine production records: this rail is about the SET, not about
    /// any host's mapping.</summary>
    private sealed record NodeSel(int Id) : IAssetSubSelection;

    /// <summary>⭐ Reuses <c>EditorSelectionStoreTests.FakeAsset</c> — ⛔ not a second fake (ruling 9).</summary>
    private static (EditorSelectionStore Store, IEditableAsset Asset) Store()
    {
        IEditableAsset asset = new EditorSelectionStoreTests.FakeAsset();
        var store = new EditorSelectionStore { ActiveAsset = asset };
        return (store, asset);
    }

    // ══ the marquee ══════════════════════════════════════════════════════════

    /// <summary>⭐⭐ <b>A marquee of two is CARRIED, not collapsed.</b> 📌 <c>R-118</c> — before <c>L0</c>
    /// the store could only hold one, so a two-node marquee arrived as <c>null</c>.</summary>
    [Fact]
    public void AMarqueeOfTwo_IsCarriedAsTwo()
    {
        var (store, _) = Store();

        store.ActiveSubSelections = new IAssetSubSelection[] { new NodeSel(1), new NodeSel(2) };

        Assert.Equal(2, store.ActiveSubSelections.Count);

        // ⛔ …and the DERIVED single is null, because it is not exactly one. ⚠ That keeps every
        //    pre-L0 reader answering exactly what it answered before.
        Assert.Null(store.ActiveSubSelection);
    }

    /// <summary>⭐ One selected element still derives to that element — the old path is untouched.</summary>
    [Fact]
    public void ASelectionOfOne_StillDerivesToThatOne()
    {
        var (store, _) = Store();
        var only = new NodeSel(7);

        store.ActiveSubSelections = new IAssetSubSelection[] { only };

        Assert.Equal(only, store.ActiveSubSelection);
        Assert.Same(only, store.ActiveSubSelection);
    }

    // ══ THE PAN — the defect L0.2 exists to fix ══════════════════════════════

    /// <summary>
    /// ⛔⛔⛔ <b>THE ONE THAT MATTERS: a PAN changes NOTHING.</b> 📄 §2b's second sequence diagram —
    /// <i>"AFTER <c>L0.2</c> the same set is written ⇒ <c>Equals(current)</c> ⇒ no event ⇒ unchanged,
    /// no repaint."</i>
    ///
    /// <para>⭐⭐ <b>Three assertions, and each catches a different wrong implementation:</b>
    /// ① no event ⇒ nothing downstream recomputes; ② the SAME LIST INSTANCE ⇒ a reference-caching
    /// reader does not rebuild *(§6 <c>L0.4</c>)*; ③ the contents still right ⇒ the guard did not
    /// silently drop the write.</para>
    ///
    /// <para>⚠ The bridges rebuild their list every frame, so the incoming list is always a NEW object.
    /// ⇒ ⛔ a reference-equality guard would fire every frame and this rail would catch it.</para>
    /// </summary>
    [Fact]
    public void APan_WritesTheSameSet_AndNothingChanges()
    {
        var (store, _) = Store();
        store.ActiveSubSelections = new IAssetSubSelection[] { new NodeSel(1), new NodeSel(2) };

        var before = store.ActiveSubSelections;
        int events = 0;
        store.OnSelectionChanged += () => events++;

        // ⭐ The pan: the bridge reports the same selection again, in a freshly allocated list.
        store.ActiveSubSelections = new IAssetSubSelection[] { new NodeSel(1), new NodeSel(2) };

        Assert.Equal(0, events);                                  // ①
        Assert.Same(before, store.ActiveSubSelections);           // ②
        Assert.Equal(2, store.ActiveSubSelections.Count);         // ③
    }

    /// <summary>⭐ …and a REAL change still fires exactly once.</summary>
    [Fact]
    public void AGenuineChange_FiresOnce()
    {
        var (store, _) = Store();
        store.ActiveSubSelections = new IAssetSubSelection[] { new NodeSel(1) };

        int events = 0;
        store.OnSelectionChanged += () => events++;

        store.ActiveSubSelections = new IAssetSubSelection[] { new NodeSel(1), new NodeSel(2) };

        Assert.Equal(1, events);
        Assert.Equal(2, store.ActiveSubSelections.Count);
    }

    /// <summary>
    /// ⚠ <b>ORDER is part of the identity</b>, and that is deliberate — the bridges enumerate in a
    /// stable order, so a differing order IS a differing selection. ⛔ Stated as a rail because the
    /// alternative *(an order-insensitive compare)* costs a sort or a set per frame.
    /// </summary>
    [Fact]
    public void ADifferentOrder_IsADifferentSelection()
    {
        var (store, _) = Store();
        store.ActiveSubSelections = new IAssetSubSelection[] { new NodeSel(1), new NodeSel(2) };

        int events = 0;
        store.OnSelectionChanged += () => events++;

        store.ActiveSubSelections = new IAssetSubSelection[] { new NodeSel(2), new NodeSel(1) };

        Assert.Equal(1, events);
    }

    // ══ empty is a LIST, never a null ════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>Empty is an empty LIST, and it is the SAME empty list every time.</b> 📌 <c>R-118</c>:
    /// <c>null</c> used to mean <i>nothing</i> AND <i>more than one</i> AND <i>unresolvable</i>.
    /// ⇒ ⛔ a caller can now tell "nothing is selected" from "two things are".
    /// </summary>
    [Fact]
    public void NothingSelected_IsAnEmptyList_NotNull()
    {
        var (store, _) = Store();

        Assert.NotNull(store.ActiveSubSelections);
        Assert.Empty(store.ActiveSubSelections);

        store.ActiveSubSelections = new IAssetSubSelection[] { new NodeSel(1) };
        var before = store.ActiveSubSelections;
        store.ActiveSubSelections = Array.Empty<IAssetSubSelection>();

        Assert.NotSame(before, store.ActiveSubSelections);
        Assert.Empty(store.ActiveSubSelections);

        // ⭐ …and clearing an already-empty selection is free.
        int events = 0;
        store.OnSelectionChanged += () => events++;
        store.ActiveSubSelections = new List<IAssetSubSelection>();
        Assert.Equal(0, events);
    }

    /// <summary>⭐ The single-value setter still round-trips, so every pre-<c>L0</c> writer is
    /// unchanged — and writing <c>null</c> means EMPTY, not "a list containing null".</summary>
    [Fact]
    public void TheSingleValueSetter_StillRoundTrips()
    {
        var (store, _) = Store();
        var one = new NodeSel(3);

        store.ActiveSubSelection = one;
        Assert.Equal(one, store.ActiveSubSelection);
        Assert.Single(store.ActiveSubSelections);

        store.ActiveSubSelection = null;
        Assert.Null(store.ActiveSubSelection);
        Assert.Empty(store.ActiveSubSelections);
    }
}
