using System;
using System.Linq;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L1</c>'s rail, verbatim from the design:</b> 📄
/// <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L1</c> —
/// <i>"the offer set for a measured context, on the <b>production-built</b> registry."</i>
///
/// <para>⛔⛔ <b>"PRODUCTION-BUILT" IS THE WHOLE POINT</b> — 📌 <c>R-67</c>: <i>"a rail that builds its
/// own composition root cannot see a composition-root defect."</i> ⇒ ⭐ this goes through
/// <c>PerspectiveWorkspaceServices.CreateRegistrar</c>, the same call <c>EditorSubsystem</c> makes, so
/// a descriptor that is never registered fails HERE. ⚠ A hand-built <c>DetailsViewRegistry</c> would
/// pass while the editor showed nothing — 📌 exactly the shape that cost Batches 79/80/81/96d.</para>
/// </summary>
public sealed class TheProductionRegistryOffersTheVariablesViewTests
{
    /// <summary>⚠ BTree and HSM only: 📌 <c>AiDetails</c> is deliberately null on Blueprint, which has
    /// its own <c>BlueprintDetailsWindow</c> — a second panel there would be two for one concept.</summary>
    private static readonly string[] DetailsPerspectives = { "BTree", "HSM" };

    private static PerspectiveWorkspaceRegistrar Production(string perspective)
    {
        var services = new PerspectiveWorkspaceServices(
            // ⭐ Reuses the layout rail's fakes — ⛔ not a second set (ruling 9).
            new AssetCatalog(), new Windows.TheDefaultLayoutIsNotStaleTests.NoRefactor(),
            new DebugSessionRegistry(),
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            isSimUp: () => false, isFrozen: () => false);

        return services.CreateRegistrar(
            perspective, new EditorSelectionStore(),
            validators: Array.Empty<IAssetValidator>());
    }

    // ══ the descriptor really is registered by the production wiring ═════════

    /// <summary>
    /// ⭐⭐⭐ <b>The variables view reaches the catalogue through the PRODUCTION path</b> —
    /// 📄 §6 <c>L1.2</c>/<c>L1.3</c>. ⛔ Nothing in this rail adds a descriptor by hand.
    /// </summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    public void TheProductionRegistrar_RegistersTheVariablesView(string perspective)
    {
        var registrar = Production(perspective);

        Assert.Contains(
            registrar.DetailsViews.All,
            d => d.Id == VariablesDetailsViewDescriptor.ViewId);
    }

    /// <summary>
    /// ⭐⭐ <b>The OFFER SET for a MEASURED context</b> — §6 <c>L1</c>'s clause. ⚠ The context is built
    /// by the production builder from a real store, ⛔ not hand-assembled.
    /// </summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    public void WithTheOutlineFocused_TheVariablesViewIsOffered(string perspective)
    {
        var store     = new EditorSelectionStore();
        var registrar = Production(perspective);
        store.NotifySurfaceFocused(SelectionOrigin.VariableOutline);

        var ctx     = DetailsContextBuilder.Build(store, perspective, VariableRunState.Planning);
        var offered = registrar.DetailsViews.OfferSet(ctx);

        Assert.Contains(offered, d => d.Id == VariablesDetailsViewDescriptor.ViewId);
        Assert.Equal(VariablesDetailsViewDescriptor.ViewId, registrar.DetailsViews.Default(ctx)!.Id);
    }

    // ══ L1.4 — the "exactly one" rule, in ONE predicate ══════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b><c>L1.4</c> — the rule <c>L0.2</c> deleted from three bridges lives HERE, once.</b>
    /// 📄 §3, <c>R-118</c>'s row: <i>"the <c>Count != 1</c> rule reappears in ONE predicate."</i>
    ///
    /// <para>⭐ One node ⇒ a node-properties view applies. ⛔ Two nodes ⇒ it does not, and 📌
    /// <c>R-117</c>'s grey line is the answer — ⚠ <b>not</b> "the first of the two", which is the
    /// collapse <c>R-118</c> exists to prevent.</para>
    /// </summary>
    [Fact]
    public void TheNodePropertiesPredicate_AppliesToExactlyOne_AndNotToTwo()
    {
        var store = new EditorSelectionStore { ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset() };

        store.ActiveSubSelections = new IAssetSubSelection[]
            { new BlueprintNodeSelection(Guid.NewGuid(), Guid.NewGuid()) };
        Assert.True(DetailsViewPredicates.ExactlyOne<BlueprintNodeSelection>(
            DetailsContextBuilder.Build(store, "Blueprint", VariableRunState.Planning)));

        store.ActiveSubSelections = new IAssetSubSelection[]
        {
            new BlueprintNodeSelection(Guid.NewGuid(), Guid.NewGuid()),
            new BlueprintNodeSelection(Guid.NewGuid(), Guid.NewGuid()),
        };
        Assert.False(DetailsViewPredicates.ExactlyOne<BlueprintNodeSelection>(
            DetailsContextBuilder.Build(store, "Blueprint", VariableRunState.Planning)));
    }

    /// <summary>
    /// ⭐⭐ <b>…and a view that CAN present a set still sees both.</b> ⚠ Stated because making every
    /// predicate single-selection would have wasted <c>L0.2</c>'s whole point — 📌 §6 <c>L3</c> lists
    /// views whose predicate is <i>"asset context"</i>, not <i>"one node"</i>.
    /// </summary>
    [Fact]
    public void ASetAwarePredicate_SeesBothSelections()
    {
        var store = new EditorSelectionStore { ActiveAsset = new Tests.Selection.EditorSelectionStoreTests.FakeAsset() };
        store.ActiveSubSelections = new IAssetSubSelection[]
        {
            new BlueprintNodeSelection(Guid.NewGuid(), Guid.NewGuid()),
            new BlueprintNodeSelection(Guid.NewGuid(), Guid.NewGuid()),
        };

        var ctx = DetailsContextBuilder.Build(store, "Blueprint", VariableRunState.Planning);

        Assert.True(DetailsViewPredicates.Any<BlueprintNodeSelection>(ctx));
        Assert.Equal(2, ctx.Selection.Count);
    }

    // ══ registering twice must not throw ═════════════════════════════════════

    /// <summary>
    /// ⚠ <b><c>RegisterExtraWindow</c> may be called twice for the same window</b> — 📌 the file's own
    /// note beside the properties-form arm. ⛔ Without the source guard, a second pass would hit the
    /// duplicate-id check and turn a harmless re-registration into a crash.
    /// </summary>
    [Fact]
    public void RegisteringTheSameSourceTwice_DoesNotThrow()
    {
        var registrar = Production("BTree");
        int before    = registrar.DetailsViews.All.Count;

        var wm = new Fdp.Presentation.WindowManager.WindowManager(
            new Fdp.Presentation.Icons.IconAtlas(nint.Zero, 1, 1, 16f));
        registrar.RegisterExtraWindow(wm, registrar.AiDetails!);
        registrar.RegisterExtraWindow(wm, registrar.AiDetails!);

        Assert.Equal(before, registrar.DetailsViews.All.Count);
    }
}
