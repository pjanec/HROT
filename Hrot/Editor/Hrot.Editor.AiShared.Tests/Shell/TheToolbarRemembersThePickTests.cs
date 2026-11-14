using System;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L2.2</c>'s rail — §2b's <c>stateDiagram</c>, transition by transition.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §2b *(<i>"The toolbar's remembered pick — state"</i>)*
/// · §2 *(<i>"The context key"</i>)* · §3's <c>R-98</c> row.
///
/// <para>⭐⭐ <b>The MODEL is railed, the ImGui row is not</b> — 📌 §6/<c>R-21</c>: the draw is unrailed
/// by construction, so everything that DECIDES lives in <see cref="DetailsViewSelector"/> and every
/// state below is a value.</para>
///
/// <para>⚠ The diagram has <b>five</b> edges. All five are here, one <c>[Fact]</c> each, named after
/// the edge.</para>
/// </summary>
public sealed class TheToolbarRemembersThePickTests
{
    private const string A = "view.a";
    private const string B = "view.b";

    private sealed class NullInstance : IDetailsViewInstance
    {
        public void Draw(DetailsContext context, string idScope) { }
        public void Dispose() { }
    }

    private static DetailsViewDescriptor View(string id, int rank, Func<DetailsContext, bool>? applies = null)
        => new(id, id, rank, applies ?? (_ => true), () => new NullInstance());

    /// <summary>⭐ <c>A</c> outranks <c>B</c>, so the <c>Rank</c> default is unambiguous.</summary>
    private static DetailsViewRegistry Both(Func<DetailsContext, bool>? aApplies = null)
    {
        var r = new DetailsViewRegistry();
        r.Add(View(A, 20, aApplies));
        r.Add(View(B, 10));
        return r;
    }

    /// <summary>
    /// ⚠⚠ <b>ONE asset for the whole class, and that is load-bearing.</b> 📌 §2's key is
    /// <c>(Perspective, AssetId, shape)</c> — ⭐ <i>"node A → node B"</i> is a move WITHIN one document.
    ///
    /// <para>⛔ <b>The first version of this helper minted a fresh <c>FakeAsset</c> per call</b>, so two
    /// contexts that differ only by which node is selected also differed by <c>AssetId</c> ⇒ two of
    /// these rails failed against CORRECT code. 📌 The same lesson as <c>BP-394</c>, mirrored: a rail
    /// can be wrong in either direction, and <b>a red is a claim about the rail until the rail is
    /// checked</b>.</para>
    /// </summary>
    private static readonly Tests.Selection.EditorSelectionStoreTests.FakeAsset TheDocument = new();

    private static DetailsContext Ctx(params IAssetSubSelection[] selection)
    {
        var store = new EditorSelectionStore { ActiveAsset = TheDocument };
        if (selection.Length > 0) store.ActiveSubSelections = selection;
        return DetailsContextBuilder.Build(store, "BTree", VariableRunState.Planning);
    }

    private sealed class NodeLike : IAssetSubSelection { }
    private sealed class RowLike  : IAssetSubSelection { }

    // ══ [*] → RankDefault ════════════════════════════════════════════════════

    /// <summary>⭐ <b>With no pick, the highest <c>Rank</c> that applies wins</b> — 📌 <c>R-98</c>.</summary>
    [Fact]
    public void Start_IsTheRankDefault()
    {
        var choice = new DetailsViewSelector().Resolve(Both(), Ctx());

        Assert.Equal(DetailsViewSelector.Mode.RankDefault, choice.State);
        Assert.Equal(A, choice.View!.Id);
        Assert.Equal(new[] { A, B }, Ids(choice));
    }

    // ══ RankDefault → UserPick ═══════════════════════════════════════════════

    /// <summary>⭐⭐ <b>The designer clicks a toggle and the panel obeys</b> — ⛔ rank stops deciding.</summary>
    [Fact]
    public void APick_BeatsTheRank()
    {
        var s = new DetailsViewSelector();
        var c = Ctx();

        s.Pick(c, B);
        var choice = s.Resolve(Both(), c);

        Assert.Equal(DetailsViewSelector.Mode.UserPick, choice.State);
        Assert.Equal(B, choice.View!.Id);
    }

    // ══ UserPick → UserPick : "context changes, pick still applies" ══════════

    /// <summary>
    /// ⭐⭐⭐ <b>The pick SURVIVES a context change of the same SHAPE</b> — 📄 §2, verbatim:
    /// <i>"node A → node B keeps the view."</i>
    ///
    /// <para>⭐ Two different <see cref="NodeLike"/> selections are the same shape, so the key matches
    /// and the designer keeps the panel they chose. ⛔ Keying on the selection's IDENTITY would reset
    /// the panel on every click, which is the behaviour this edge exists to forbid.</para>
    /// </summary>
    [Fact]
    public void NodeAToNodeB_KeepsTheView()
    {
        var s = new DetailsViewSelector();
        s.Pick(Ctx(new NodeLike()), B);

        var choice = s.Resolve(Both(), Ctx(new NodeLike()));      // ⭐ a DIFFERENT selection instance

        Assert.Equal(DetailsViewSelector.Mode.UserPick, choice.State);
        Assert.Equal(B, choice.View!.Id);
    }

    /// <summary>
    /// ⭐⭐ <b>…and a different SHAPE remembers its own</b> — §2: <i>"a variable pick remembers its
    /// own."</i> ⇒ the two memories do not bleed into one another.
    /// </summary>
    [Fact]
    public void ADifferentShape_HasItsOwnMemory()
    {
        var s = new DetailsViewSelector();
        s.Pick(Ctx(new NodeLike()), B);

        var other = s.Resolve(Both(), Ctx(new RowLike()));

        Assert.Equal(DetailsViewSelector.Mode.RankDefault, other.State);
        Assert.Equal(A, other.View!.Id);

        // ⭐ and the node shape still has its pick — the two are independent, not overwritten.
        Assert.Equal(B, s.Resolve(Both(), Ctx(new NodeLike())).View!.Id);
    }

    /// <summary>
    /// ⚠ <b>ORDER is part of the shape, deliberately.</b> ⭐ It is part of the SET's identity too
    /// *(<c>L0.1</c>'s elementwise guard)* — ⛔ a key that ignored order would disagree with the store
    /// about whether the selection changed.
    /// </summary>
    [Fact]
    public void SwappingTheOrder_IsADifferentShape()
    {
        var s = new DetailsViewSelector();
        s.Pick(Ctx(new NodeLike(), new RowLike()), B);

        Assert.Equal(
            DetailsViewSelector.Mode.RankDefault,
            s.Resolve(Both(), Ctx(new RowLike(), new NodeLike())).State);
    }

    /// <summary>
    /// ⭐⭐ <b>A different DOCUMENT has its own memory</b> — §2's second key component.
    /// ⚠ Written because getting this WRONG in the helper above is what reddened two rails against
    /// correct code; ⭐ stating it positively is cheaper than remembering the trap.
    /// </summary>
    [Fact]
    public void ADifferentAsset_HasItsOwnMemory()
    {
        var s = new DetailsViewSelector();
        s.Pick(Ctx(), B);

        var otherDoc = new EditorSelectionStore
            { ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset() };
        var choice = s.Resolve(
            Both(), DetailsContextBuilder.Build(otherDoc, "BTree", VariableRunState.Planning));

        Assert.Equal(DetailsViewSelector.Mode.RankDefault, choice.State);
    }

    /// <summary>⭐ A different PERSPECTIVE is a different key too — §2's first key component.</summary>
    [Fact]
    public void ADifferentPerspective_HasItsOwnMemory()
    {
        var s     = new DetailsViewSelector();
        var store = new EditorSelectionStore
            { ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset() };

        s.Pick(DetailsContextBuilder.Build(store, "BTree", VariableRunState.Planning), B);

        var hsm = s.Resolve(Both(), DetailsContextBuilder.Build(store, "HSM", VariableRunState.Planning));

        Assert.Equal(DetailsViewSelector.Mode.RankDefault, hsm.State);
    }

    // ══ UserPick → RankDefault : "pick no longer applies" ════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A pick that stops applying falls back by <c>Rank</c></b> — 📄 §2: <i>"⛔ never to a blank
    /// panel."</i>
    /// </summary>
    [Fact]
    public void WhenThePickStopsApplying_ItFallsBackByRank()
    {
        var s = new DetailsViewSelector();
        var c = Ctx();
        s.Pick(c, B);

        // ⭐ B drops out of the offer set entirely.
        var onlyA = new DetailsViewRegistry();
        onlyA.Add(View(A, 20));

        var choice = s.Resolve(onlyA, c);

        Assert.Equal(DetailsViewSelector.Mode.RankDefault, choice.State);
        Assert.Equal(A, choice.View!.Id);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>…and it is FORGOTTEN, not merely ignored.</b>
    ///
    /// <para>⛔ Keeping it would make the panel jump back to a view the designer last saw three
    /// selections ago, the moment it happened to apply again — ⚠ surprising, and indistinguishable
    /// from a bug. ⭐ This is the rail for that choice; without it the behaviour is a coin flip.</para>
    /// </summary>
    [Fact]
    public void APickThatStoppedApplying_IsForgottenNotParked()
    {
        var s = new DetailsViewSelector();
        var c = Ctx();
        s.Pick(c, B);

        var onlyA = new DetailsViewRegistry();
        onlyA.Add(View(A, 20));
        s.Resolve(onlyA, c);              // ⭐ B no longer applies here

        // ⛔ B is offered again — and must NOT silently come back.
        var choice = s.Resolve(Both(), c);

        Assert.Equal(DetailsViewSelector.Mode.RankDefault, choice.State);
        Assert.Equal(A, choice.View!.Id);
    }

    /// <summary>⭐ The toolbar's <i>"back to default"</i> — clicking the active toggle clears the pick.</summary>
    [Fact]
    public void ClearPick_ReturnsToTheRankDefault()
    {
        var s = new DetailsViewSelector();
        var c = Ctx();
        s.Pick(c, B);
        Assert.Equal(B, s.Resolve(Both(), c).View!.Id);

        s.ClearPick(c);

        Assert.Equal(DetailsViewSelector.Mode.RankDefault, s.Resolve(Both(), c).State);
    }

    // ══ → EmptyOffer, and back ═══════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>Nothing applies ⇒ <c>EmptyOffer</c>, a null view and an empty offer list.</b>
    /// ⚠ The three agree by construction — ⛔ a non-null view in this state would put the toolbar and
    /// the panel at odds.
    /// </summary>
    [Fact]
    public void NothingApplies_IsTheEmptyOfferState()
    {
        var none = new DetailsViewRegistry();
        none.Add(View(A, 20, _ => false));

        var choice = new DetailsViewSelector().Resolve(none, Ctx());

        Assert.Equal(DetailsViewSelector.Mode.EmptyOffer, choice.State);
        Assert.Null(choice.View);
        Assert.Empty(choice.Offered);
    }

    /// <summary>
    /// ⭐⭐ <b><c>EmptyOffer → RankDefault</c> — <i>"a view applies again"</i>, and the PICK SURVIVES the
    /// gap.</b>
    ///
    /// <para>⚠ Stated as a deliberate difference from the edge above: a view that stops APPLYING is
    /// forgotten, but a moment where NOTHING applies is transient *(a marquee, a click on empty
    /// canvas)* — ⛔ dropping the pick there would punish the designer for deselecting.</para>
    /// </summary>
    [Fact]
    public void AfterAnEmptyOffer_ThePickIsStillRemembered()
    {
        var s = new DetailsViewSelector();
        var c = Ctx();
        s.Pick(c, B);

        var none = new DetailsViewRegistry();
        none.Add(View(A, 20, _ => false));
        Assert.Equal(DetailsViewSelector.Mode.EmptyOffer, s.Resolve(none, c).State);

        var back = s.Resolve(Both(), c);

        Assert.Equal(DetailsViewSelector.Mode.UserPick, back.State);
        Assert.Equal(B, back.View!.Id);
    }

    // ══ the offer set the toolbar draws ══════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>The choice carries the whole <c>Rank</c>-ordered offer set</b> — the toolbar's buttons ARE
    /// this list *(<c>R-98</c>: <i>"the toolbar is a panel switch"</i>)*.
    ///
    /// <para>⚠ Carried on the choice rather than re-queried, deliberately: two <c>OfferSet</c> calls in
    /// one frame could disagree if a predicate is impure, and the button row would then not match the
    /// view being drawn.</para>
    /// </summary>
    [Fact]
    public void TheChoiceCarriesTheOfferSet_InRankOrder()
    {
        var choice = new DetailsViewSelector().Resolve(Both(), Ctx());

        Assert.Equal(new[] { A, B }, Ids(choice));
        Assert.Contains(choice.Offered, d => ReferenceEquals(d, choice.View));
    }

    private static string[] Ids(DetailsViewSelector.Choice c)
    {
        var ids = new string[c.Offered.Count];
        for (int i = 0; i < ids.Length; i++) ids[i] = c.Offered[i].Id;
        return ids;
    }
}
