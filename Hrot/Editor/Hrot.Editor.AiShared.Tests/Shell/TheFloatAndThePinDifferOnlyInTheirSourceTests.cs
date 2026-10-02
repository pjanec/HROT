using System;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L4</c>'s rail — float and pin are ONE class, and the source is the whole difference.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §2 *(the hosting table · <i>"the two window classes
/// differ ONLY in <c>IDetailsContextSource</c>"</i>)* · §2b's float and pin sequences ·
/// §6 <c>L4.1</c>–<c>L4.4</c> · 📌 <c>R-119</c> · <c>R-100</c> · <c>R-117</c> · <c>R-120</c>.
/// </summary>
public sealed class TheFloatAndThePinDifferOnlyInTheirSourceTests
{
    private static WindowManager Wm()
        => new(new Fdp.Presentation.Icons.IconAtlas(nint.Zero, 1, 1, 16f));

    private static PerspectiveWorkspaceRegistrar Production(string perspective, EditorSelectionStore store)
    {
        var services = new PerspectiveWorkspaceServices(
            new AssetCatalog(), new Windows.TheDefaultLayoutIsNotStaleTests.NoRefactor(),
            new DebugSessionRegistry(),
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            isSimUp: () => false, isFrozen: () => false);

        return services.CreateRegistrar(
            perspective, store, validators: Array.Empty<IAssetValidator>());
    }

    /// <summary>⭐ A shell with something to show — the Blackboard view claims any open document.</summary>
    private static (DetailsWindow details, EditorSelectionStore store) ShellWithAView()
    {
        var store = new EditorSelectionStore
            { ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset() };
        return (Production("BTree", store).Details!, store);
    }

    // ══ L4.1 — the window itself ═════════════════════════════════════════════

    private sealed class CountingInstance : IDetailsViewInstance
    {
        public int Draws, Disposes;
        public void Draw(DetailsContext context, string idScope) => Draws++;
        public void Dispose() => Disposes++;
    }

    private static DetailsViewDescriptor View(
        string id, Func<DetailsContext, bool> applies, Func<IDetailsViewInstance> create)
        => new(id, id, 0, applies, create);

    /// <summary>
    /// ⭐⭐⭐ <b>Multiplicity <c>"1"</c>: the window composes its OWN instance</b> — 📌 <c>R-120</c>:
    /// two windows showing one view get two instances, so there is nothing to arbitrate.
    /// </summary>
    [Fact]
    public void EachWindow_ComposesItsOwnInstance()
    {
        var made = 0;
        var d = View("v", _ => true, () => { made++; return new CountingInstance(); });

        _ = new DetailsViewWindow("a", "V", "BTree", d, new FrozenContextSource(DetailsContext.Empty("BTree")), false);
        _ = new DetailsViewWindow("b", "V", "BTree", d, new FrozenContextSource(DetailsContext.Empty("BTree")), false);

        Assert.Equal(2, made);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>R-117</c>'s SECOND site: a float whose predicate is false STAYS OPEN and says
    /// why.</b> 📄 §2's hosting table — <i>"stays open, grey line"</i>; §6 <c>L4</c> — <i>"a float is
    /// restored into contexts that reject it; that is ordinary."</i>
    /// <para>⚠ The sentence NAMES the view — ⛔ a bare <i>"nothing to show"</i> reads as stuck.</para>
    /// </summary>
    [Fact]
    public void AFloatWhosePredicateIsFalse_SaysWhyAndNamesItself()
    {
        var w = new DetailsViewWindow(
            "f", "Variables", "BTree",
            View("v", _ => false, () => new CountingInstance()),
            new FrozenContextSource(DetailsContext.Empty("BTree")),
            isVolatile: false);

        var frame = w.Frame();

        Assert.False(frame.Applies);
        Assert.NotNull(frame.EmptyState);
        Assert.Contains("Variables", frame.EmptyState!, StringComparison.Ordinal);
        Assert.True(w.IsOpen);          // ⛔ NOT closed
    }

    /// <summary>⭐ …and when it applies, there is no grey line at all.</summary>
    [Fact]
    public void AFloatWhosePredicateHolds_HasNoEmptyState()
    {
        var w = new DetailsViewWindow(
            "f", "V", "BTree", View("v", _ => true, () => new CountingInstance()),
            new FrozenContextSource(DetailsContext.Empty("BTree")), isVolatile: false);

        Assert.True(w.Frame().Applies);
        Assert.Null(w.Frame().EmptyState);
    }

    /// <summary>
    /// ⛔⛔ <b>IT HOLDS NO REFERENCE CAPTURED AT OPEN TIME</b> — 📄 §6 <c>L4</c>, verbatim.
    /// ⭐ A LIVE float re-asks its source and its predicate every frame, so a context that starts
    /// rejected becomes accepted without reopening the window.
    /// </summary>
    [Fact]
    public void ALiveFloat_ReAsksEveryFrame()
    {
        var store = new EditorSelectionStore();
        var src   = new LiveContextSource(
            () => DetailsContextBuilder.Build(store, "BTree", VariableRunState.Planning));

        var w = new DetailsViewWindow(
            "f", "V", "BTree",
            View("v", DetailsViewPredicates.HasAsset, () => new CountingInstance()),
            src, isVolatile: false);

        Assert.False(w.Frame().Applies);                 // ⛔ nothing open yet

        store.ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset();

        Assert.True(w.Frame().Applies);                  // ⭐ …and it noticed
    }

    // ══ L4.3 — a pin is FROZEN, and that is the only difference ══════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A PIN does NOT follow the context</b> — 📌 <c>R-100</c>. ⚠ Same window class, same
    /// predicate, same everything: only the SOURCE differs from
    /// <see cref="ALiveFloat_ReAsksEveryFrame"/>, which is §2's claim made checkable.
    /// </summary>
    [Fact]
    public void APin_KeepsTheContextItWasPinnedAt()
    {
        var store    = new EditorSelectionStore();
        var snapshot = DetailsContextBuilder.Build(store, "BTree", VariableRunState.Planning);

        var w = new DetailsViewWindow(
            "p", "V", "BTree",
            View("v", DetailsViewPredicates.HasAsset, () => new CountingInstance()),
            new FrozenContextSource(snapshot), isVolatile: true);

        Assert.False(w.Frame().Applies);

        store.ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset();

        // ⭐ The world moved; the pin did not.
        Assert.False(w.Frame().Applies);
        Assert.Same(snapshot, w.Frame().Context);
    }

    /// <summary>
    /// ⭐⭐ <b>§2's hosting table, as flags:</b> a contextual float PERSISTS in the layout
    /// *(<c>IsVolatile = false</c>, listed in the Windows menu)*; a pin is excluded from the save
    /// *(<c>IsVolatile = true</c>, not menu-listed)*.
    /// </summary>
    [Fact]
    public void TheLayoutFlags_MatchTheHostingTable()
    {
        var d = View("v", _ => true, () => new CountingInstance());
        var ctx = new FrozenContextSource(DetailsContext.Empty("BTree"));

        var float_ = new DetailsViewWindow("f", "V", "BTree", d, ctx, isVolatile: false);
        var pin    = new DetailsViewWindow("p", "V", "BTree", d, ctx, isVolatile: true);

        Assert.False(float_.IsVolatile);
        Assert.True(float_.ShowInMenu);
        Assert.True(pin.IsVolatile);
        Assert.False(pin.ShowInMenu);
    }

    // ══ L4.2 / L4.4 — the entry points, on the production shell ══════════════

    /// <summary>
    /// ⭐⭐⭐ <b>§2b's float sequence, end to end on the PRODUCTION shell</b> — 📌 <c>R-67</c>.
    /// ⭐ The float shows the view the shell was showing, and is registered with the window manager.
    /// </summary>
    [Fact]
    public void OpenFloat_RegistersAWindowForTheShownView()
    {
        var (details, _) = ShellWithAView();
        var wm = Wm();

        var shown = details.Frame().Choice.View!.Id;
        var window = details.OpenFloat(wm);

        Assert.NotNull(window);
        Assert.Equal(shown, window!.ViewId);
        Assert.True(wm.TryGetWindow(window.Id, out _));
        Assert.False(window.IsVolatile);
    }

    /// <summary>
    /// ⭐⭐ <b>A second float of the same view FOCUSES the first</b> — ⛔ never a second identical
    /// window. ⚠ The same shape §2b gives the pin, applied here because a duplicate float is never
    /// what the designer meant.
    /// </summary>
    [Fact]
    public void OpeningTheSameFloatTwice_ReturnsTheSameWindow()
    {
        var (details, _) = ShellWithAView();
        var wm = Wm();

        var first  = details.OpenFloat(wm);
        var second = details.OpenFloat(wm);

        Assert.Same(first, second);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>§2b's pin sequence: a duplicate pin FOCUSES rather than stacking</b> — 📌 <c>R-100</c>,
    /// verbatim: <i>"<c>TryGetWindow</c> ⇒ focus a duplicate."</i>
    /// </summary>
    [Fact]
    public void PinningTwiceAtTheSameContext_FocusesTheExistingPin()
    {
        var (details, _) = ShellWithAView();
        var wm = Wm();

        var first  = details.Pin(wm);
        var second = details.Pin(wm);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.True(first!.IsVolatile);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>…but pinning after the CONTEXT changed makes a SECOND pin</b> — ⚠ which is the entire
    /// point of a pin: comparing two contexts side by side. 📄 §2b's id is
    /// <c>viewId + assetId + selectionKey</c>.
    /// </summary>
    [Fact]
    public void PinningAtADifferentContext_MakesASecondPin()
    {
        var (details, store) = ShellWithAView();
        var wm = Wm();

        var first = details.Pin(wm);

        store.ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset();   // ⭐ another document
        var second = details.Pin(wm);

        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.NotEqual(first!.Id, second!.Id);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A pin MADE BY THE SHELL is frozen; a float MADE BY THE SHELL is live.</b>
    ///
    /// <para>⚠⚠ <b>This rail exists because a revert probe found NOTHING.</b> 📐 Replacing
    /// <c>Pin</c>'s <c>new FrozenContextSource(frame.Context)</c> with the shell's own LIVE source —
    /// which collapses <c>R-119</c> entirely — reddened <b>zero</b> rails: the frozen-ness was only
    /// ever asserted on a <c>DetailsViewWindow</c> built by hand, ⛔ never on the one the ENTRY POINT
    /// makes. 📌 <c>BP-394</c>'s lesson, in the other direction: <b>a probe that does not redden is a
    /// finding about the rail.</b></para>
    /// </summary>
    [Fact]
    public void TheShellsPinIsFrozen_WhileTheShellsFloatIsLive()
    {
        var (details, store) = ShellWithAView();
        var wm = Wm();

        var pin      = details.Pin(wm)!;
        var float_   = details.OpenFloat(wm)!;
        var pinnedAt = pin.Frame().Context;

        // ⭐ The world moves.
        store.ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset();

        Assert.Same(pinnedAt, pin.Frame().Context);          // ⛔ the pin did NOT follow
        Assert.NotSame(pinnedAt, float_.Frame().Context);    // ⭐ the float DID
    }

    /// <summary>
    /// ⭐⭐ <b>The pin id reuses the TOOLBAR's context key</b> — ⛔ not a second key-builder
    /// *(ruling 9)*. ⚠ Railed because two keys that mean <i>"the same context"</i> and disagree is a
    /// defect nothing else would catch.
    /// </summary>
    [Fact]
    public void ThePinId_IsBuiltFromTheToolbarsContextKey()
    {
        var ctx = DetailsContextBuilder.Build(
            new EditorSelectionStore(), "BTree", VariableRunState.Planning);

        Assert.Contains(DetailsViewSelector.KeyOf(ctx),
                        DetailsWindow.PinIdFor(ctx, "details.x"), StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐ <b><c>L4.2</c>'s float id is STABLE — it does NOT move with the selection.</b>
    /// ⚠ An id that moved could never be restored from a saved layout, which is what
    /// <c>IsVolatile = false</c> is for.
    /// </summary>
    [Fact]
    public void TheFloatId_DoesNotDependOnTheSelection()
    {
        var store = new EditorSelectionStore();
        var a = DetailsContextBuilder.Build(store, "BTree", VariableRunState.Planning);
        store.ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset();
        var b = DetailsContextBuilder.Build(store, "BTree", VariableRunState.Planning);

        Assert.NotEqual(DetailsWindow.PinIdFor(a, "v"), DetailsWindow.PinIdFor(b, "v"));
        Assert.Equal(
            DetailsWindow.FloatIdFor("BTree", "v"),
            DetailsWindow.FloatIdFor("BTree", "v"));
    }

    /// <summary>
    /// ⭐ <b>Nothing showing ⇒ nothing to float or pin</b> — ⛔ never a window over a null descriptor.
    /// ⚠ 📌 <c>R-117</c>'s grey line is already the shell's answer in that state.
    /// </summary>
    [Fact]
    public void WithNothingShowing_NeitherGestureOpensAWindow()
    {
        var details = Production("BTree", new EditorSelectionStore()).Details!;   // ⛔ no document
        var wm = Wm();

        Assert.Equal(DetailsViewSelector.Mode.EmptyOffer, details.Frame().Choice.State);
        Assert.Null(details.OpenFloat(wm));
        Assert.Null(details.Pin(wm));
    }
}
