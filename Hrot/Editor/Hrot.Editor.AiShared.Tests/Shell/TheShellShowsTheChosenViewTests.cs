using System;
using System.Linq;
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
/// ⭐⭐⭐ <b><c>L2.1</c>'s rail — the shell shows what the REGISTRY chose, and says why when it shows
/// nothing.</b> 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §4 *(<i>"grow this into
/// <c>DetailsWindow</c>"</i>)* · §2's <c>classDiagram</c> · §2b's first sequence · §6 <c>L2</c>.
///
/// <para>⛔⛔ <b>PRODUCTION-BUILT</b>, like <c>L1</c>'s — 📌 <c>R-67</c>: <i>"a rail that builds its own
/// composition root cannot see a composition-root defect."</i> ⚠ A hand-built window with a hand-built
/// registry would pass while the editor showed a blank panel, which is precisely the class of miss that
/// cost Batches 79/80/81/96d.</para>
///
/// <para>⭐⭐ Every assertion is on <see cref="DetailsWindow.Frame"/> — a returned MODEL — so it holds
/// without an ImGui context *(§6, <c>R-21</c>/<c>R-62</c>)*.</para>
/// </summary>
public sealed class TheShellShowsTheChosenViewTests
{
    /// <summary>⚠ BTree and HSM only — 📌 <c>Details</c> is deliberately null on Blueprint.</summary>
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

    /// <summary>⭐ The section the outline routes into — giving it content is what makes the variables
    /// view APPLY *(<c>L2.3</c>'s predicate half)*.</summary>
    private static void ShowSomeVariables(DetailsWindow details)
        => details.ShowVariables(new VariableOutlineSelection("Inputs", OneRow()));

    /// <summary>⭐ Reuses the shipped <c>FixedVariableRowSource</c> — ⛔ not a second fake (ruling 9).</summary>
    private static IVariableRowSource OneRow()
        => new FixedVariableRowSource(new[]
        {
            new VariableRow(
                Origin:    new VariableRowOrigin(Guid.NewGuid(), new Fdp.Core.Entity(1, 0), "s", "Ammo", "Asset"),
                ShortName: "Ammo",
                TypeText:  "Int32",
                ClrType:   typeof(int),
                ReadValue: () => new byte[4]),
        });

    // ══ §2b's first sequence — nothing applies ⇒ the grey line ═══════════════

    /// <summary>
    /// ⭐⭐⭐ <b>§6 <c>L2</c>'s rail, verbatim: <i>"an empty offer set returns the grey string."</i></b>
    ///
    /// <para>📐 With no document open, nothing claims the panel ⇒ 📌 <c>R-117</c>'s sentence, ⛔ never a
    /// blank. ⚠ The <c>View</c> is null and the state says <c>EmptyOffer</c> — the three agree.</para>
    /// </summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    public void WithNothingToShow_TheFrameCarriesTheGreyLine(string perspective)
    {
        var store   = new EditorSelectionStore();
        var details = Production(perspective, store).Details!;

        var frame = details.Frame();

        Assert.Equal(DetailsViewSelector.Mode.EmptyOffer, frame.Choice.State);
        Assert.Null(frame.Choice.View);
        Assert.Equal(DetailsEmptyState.NoDocument, frame.EmptyState);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE DEFECT <c>L2.3</c> CLOSES.</b> 📐 Measured:
    /// <c>VariableDetailsSection.Draw</c> is <c>if (!HasContent) return;</c> ⇒ a variables view that
    /// claimed the panel with an EMPTY section would render a <b>blank</b>.
    ///
    /// <para>⭐ 📌 <c>R-116</c> — the predicate ships with the view, so the view declines and the shell's
    /// ordinary empty-offer path answers. ⛔ The alternative *(the shell special-casing variables)* would
    /// put knowledge of what a variable is inside a type that must not have it.</para>
    ///
    /// <para>⚠⚠ <b>RE-EXPRESSED BY <c>L3.3</c>, and the reason matters.</b> ⭐ <c>L2</c> asserted
    /// <c>EmptyOffer</c> here — which was true, but only because <b>no other view existed yet</b>.
    /// 📐 <c>L3.3</c>'s Blackboard view claims any open document, so the panel is now filled.
    /// ⇒ ⛔ the <c>EmptyOffer</c> assertion was encoding <i>"nothing else is built"</i>, not the claim
    /// this rail is FOR. ⭐ <b>The two real claims are unchanged and are asserted directly:</b>
    /// the variables view <b>declines</b> with an empty section, and the panel is <b>never blank</b>.
    /// 📌 The mirror of <c>BP-396</c>: a rail written before its neighbours exist can encode their
    /// absence by accident.</para>
    /// </summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    public void ADocumentWithNoVariableSelected_DoesNotOfferTheVariablesView(string perspective)
    {
        var store = new EditorSelectionStore
            { ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset() };
        var details = Production(perspective, store).Details!;
        store.NotifySurfaceFocused(SelectionOrigin.VariableOutline);

        // ⭐ The outline is focused — the CONTEXT half of the predicate is true — but the section is
        //   empty, so the view must NOT claim the panel.
        var frame = details.Frame();

        Assert.DoesNotContain(
            frame.Choice.Offered, d => d.Id == VariablesDetailsViewDescriptor.ViewId);

        // ⛔ R-117: never a blank. Either a view is showing, or the grey line says why.
        Assert.True(frame.Choice.View != null || frame.EmptyState != null);
    }

    /// <summary>
    /// ⭐⭐ <b>…and with NO view at all registered, the same context still answers with the grey
    /// line.</b> ⚠ Kept as its own rail so <c>L2.3</c>'s empty-offer path stays covered now that
    /// <c>L3.3</c> fills the production panel — ⛔ otherwise the branch would go unrailed and nobody
    /// would notice until a perspective had no views.
    /// </summary>
    [Fact]
    public void WithNoViewsRegistered_AnOpenDocumentStillGetsTheGreyLine()
    {
        var store = new EditorSelectionStore
            { ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset() };
        var ctx = DetailsContextBuilder.Build(store, "BTree", VariableRunState.Planning);

        var empty = new DetailsViewRegistry();
        var choice = new DetailsViewSelector().Resolve(empty, ctx);

        Assert.Equal(DetailsViewSelector.Mode.EmptyOffer, choice.State);
        Assert.Equal(DetailsEmptyState.NothingForThisSelection, DetailsEmptyState.For(ctx));
    }

    // ══ a view that applies IS what the shell shows ══════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>With content, the production registry's variables view is the chosen one</b> — and the
    /// grey line is gone. ⚠ Nothing here adds a descriptor by hand: the view reaches the catalogue
    /// through <c>L1.2</c>'s claim chain inside the registrar.
    /// </summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    public void WithVariablesToShow_TheShellChoosesTheVariablesView(string perspective)
    {
        var store = new EditorSelectionStore
            { ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset() };
        var details = Production(perspective, store).Details!;
        store.NotifySurfaceFocused(SelectionOrigin.VariableOutline);
        ShowSomeVariables(details);

        var frame = details.Frame();

        Assert.Null(frame.EmptyState);
        Assert.Equal(DetailsViewSelector.Mode.RankDefault, frame.Choice.State);
        Assert.Equal(VariablesDetailsViewDescriptor.ViewId, frame.Choice.View!.Id);
    }

    // ══ R-112 — the shell never keys on asset kind ═══════════════════════════

    /// <summary>
    /// ⭐⭐ <b>The chosen view does not change when only the ASSET KIND changes</b> — 📌 <c>R-112</c>:
    /// <i>"`AssetKind` is never a view key."</i> ⛔ That is the mistake §4 dissolves
    /// <c>RuntimeInspectorWindow</c> for *(<c>_panes.Find(p =&gt; p.TargetKind == asset.Kind)</c>)*, and
    /// this rail is what stops the shell inheriting it.
    /// </summary>
    [Fact]
    public void TheShellDoesNotKeyOnAssetKind()
    {
        var store   = new EditorSelectionStore();
        var details = Production("BTree", store).Details!;
        store.NotifySurfaceFocused(SelectionOrigin.VariableOutline);
        ShowSomeVariables(details);

        foreach (var kind in new[] { AssetKind.Blueprint, AssetKind.BTree, AssetKind.Hsm })
        {
            store.ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset { Kind = kind };
            Assert.Equal(
                VariablesDetailsViewDescriptor.ViewId,
                details.Frame().Choice.View!.Id);
        }
    }

    // ══ §2's "only the workspace builds a context" ═══════════════════════════

    /// <summary>
    /// ⭐⭐ <b>The context is LIVE</b> — the shell re-reads it every frame rather than caching one at
    /// construction. 📌 §2b's contextual-float loop *(<c>F-&gt;&gt;W: BuildContext()</c> inside
    /// <c>loop every frame</c>)*, and §2's <i>"only the workspace builds a context."</i>
    ///
    /// <para>⚠ Railed because a captured-at-open context is the exact defect §6 <c>L4.2</c> warns about:
    /// <i>"it may hold NO reference captured at open time."</i></para>
    /// </summary>
    [Fact]
    public void TheContextIsReReadEveryFrame()
    {
        var store   = new EditorSelectionStore();
        var details = Production("BTree", store).Details!;

        Assert.Null(details.Frame().Context.Asset);

        store.ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset();

        Assert.NotNull(details.Frame().Context.Asset);
    }

    /// <summary>⭐ …and the perspective the registrar was built for reaches the context unchanged.</summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    public void TheContextCarriesTheOwningPerspective(string perspective)
    {
        var details = Production(perspective, new EditorSelectionStore()).Details!;
        Assert.Equal(perspective, details.Frame().Context.Perspective);
    }

    // ══ §2's DetailsWindow *-- "0..1" IDetailsViewInstance ═══════════════════

    /// <summary>
    /// ⭐⭐ <b>No instance exists before one is needed</b> — §2's multiplicity is <c>"0..1"</c>, and a
    /// window that eagerly created every view would defeat <c>R-120</c>'s per-window instances.
    /// </summary>
    [Fact]
    public void BeforeAnythingIsDrawn_NoViewIsInstantiated()
        => Assert.Null(Production("BTree", new EditorSelectionStore()).Details!.InstantiatedViewId);

    // ══ the rename kept the layout key ═══════════════════════════════════════

    /// <summary>
    /// ⛔⛔ <b>The TYPE renamed; the persisted ImGui ID did NOT.</b> 📌 §5: a bare key rename
    /// <i>"silently resets layouts"</i>, because <c>CurrentPerspective</c> and every
    /// <c>OwningPerspective</c> are saved. ⚠ This rail is cheap and the failure it guards is invisible
    /// until a designer opens the editor and finds their dock layout gone.
    /// </summary>
    [Theory]
    [InlineData("BTree", "ai_details_btree")]
    [InlineData("HSM",   "ai_details_hsm")]
    public void TheRenameKeptThePersistedWindowId(string perspective, string expectedId)
    {
        var details = Production(perspective, new EditorSelectionStore()).Details!;

        Assert.Equal(expectedId, details.Id);
        Assert.Equal("Details", details.Title);
        Assert.Equal(perspective, details.OwningPerspective);
    }
}
