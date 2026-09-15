using System;
using System.Linq;
using Fdp.Core;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Hrot.IG.Components;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L0.4</c>'s rail — the Details context reads <c>SelectionState</c> from the WORLD.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L0.4</c> *(<c>R-122</c>)* ·
/// 📄 <c>docs/UX/UX_Feature_Selection.md</c> §0/§2.1, which names the editor-side copies 🔴 the defect.
///
/// <para>⭐⭐ <b>Over a REAL <c>EntityRepository</c></b> — ⛔ not a fake source. ⚠ A stub implementing
/// <c>IEntitySelectionSource</c> would assert my own arithmetic, not that the component the ring gizmo
/// paints is the one the panel reads.</para>
/// </summary>
public sealed class TheEntityContextReadsTheWorldTests
{
    /// <summary>
    /// ⚠ <b><c>SelectionState</c> must be REGISTERED on the repo</b> — the map subsystems do this in
    /// production. ⭐ Measured while writing this rail: <c>QueryBuilder.With&lt;T&gt;</c> only sets a
    /// bit *(<c>QueryBuilder.cs:36</c>)*, so an unregistered component matches NOTHING rather than
    /// throwing — ⇒ the source is safe in a world that never registered it, which
    /// <see cref="AWorldThatNeverRegisteredTheComponent_YieldsEmpty"/> asserts.
    /// </summary>
    private static EntityRepository World()
    {
        var w = new EntityRepository();
        w.RegisterComponent<SelectionState>();
        return w;
    }

    private static Entity Selected(EntityRepository w, bool primary)
    {
        var e = w.CreateEntity();
        w.AddComponent(e, new SelectionState { IsSelected = true, IsPrimarySelection = primary });
        return e;
    }

    // ══ it reads the component ═══════════════════════════════════════════════

    /// <summary>⭐⭐ <b>Selected entities come back; unselected ones do not</b> — the whole claim of
    /// <c>R-122</c>, over the real component.</summary>
    [Fact]
    public void OnlyEntitiesWithSelectionState_AreReported()
    {
        var w = World();
        var a = Selected(w, primary: true);
        var unselected = w.CreateEntity();
        w.AddComponent(unselected, new SelectionState { IsSelected = false });
        w.CreateEntity();   // ⭐ no component at all

        var got = new WorldEntitySelectionSource(w).Selected();

        Assert.Equal(new[] { a }, got.ToArray());
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE PRIMARY COMES FIRST.</b> 📄 <c>UX_Feature_Selection.md</c> §0 — <c>SelectionState</c>
    /// carries <i>"one primary, many selected"</i>. ⇒ ⭐ a view that takes <c>[0]</c> gets the entity
    /// the ring paints GREEN, ⛔ not whichever the archetype walk reached first.
    /// </summary>
    [Fact]
    public void ThePrimaryIsFirst_WhateverOrderTheWorldWalksIn()
    {
        var w = World();
        var secondary = Selected(w, primary: false);   // ⭐ created FIRST, deliberately
        var primary   = Selected(w, primary: true);

        var got = new WorldEntitySelectionSource(w).Selected();

        Assert.Equal(2, got.Count);
        Assert.Equal(primary, got[0]);
        Assert.Equal(secondary, got[1]);
    }

    // ══ §6 L0.4's same-instance clause ═══════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>§6 <c>L0.4</c>, verbatim: <i>"return the same list instance when unchanged, or every
    /// view rebuilds per frame."</i></b>
    /// ⚠ This is what makes §2b's pan guarantee hold through the ENTITY field — 📌 the same defect
    /// <c>L0.1</c> fixed on the selection field, arriving by a different door.
    /// </summary>
    [Fact]
    public void AnUnchangedSelection_ReturnsTheSameInstance()
    {
        var w = World();
        Selected(w, primary: true);
        var src = new WorldEntitySelectionSource(w);

        Assert.Same(src.Selected(), src.Selected());
    }

    /// <summary>
    /// ⛔⛔ <b>A SWAP AT THE SAME COUNT MUST NOT be mistaken for "unchanged".</b>
    /// ⚠ The obvious cache — key on <c>Count</c> — passes every other rail in this file and fails
    /// exactly here: clicking entity B after entity A leaves the count at <b>1</b>. ⭐ That is why the
    /// comparison is ELEMENTWISE.
    /// </summary>
    [Fact]
    public void SwappingOneEntityForAnother_IsANewInstance()
    {
        var w = World();
        var a = Selected(w, primary: true);
        var src = new WorldEntitySelectionSource(w);
        var first = src.Selected();

        w.SetComponent(a, new SelectionState { IsSelected = false });
        var b = Selected(w, primary: true);

        var second = src.Selected();

        Assert.NotSame(first, second);
        Assert.Equal(new[] { b }, second.ToArray());
    }

    /// <summary>
    /// ⚠ <b>…and so must a PRIMARY that moved</b>, which also leaves the count alone. ⭐ Order carries
    /// the primary, so the comparison is order-sensitive on purpose — ⛔ an order-blind one would hold
    /// the panel on a stale primary.
    /// </summary>
    [Fact]
    public void MovingThePrimary_IsANewInstance()
    {
        var w = World();
        var a = Selected(w, primary: true);
        var b = Selected(w, primary: false);
        var src = new WorldEntitySelectionSource(w);
        var first = src.Selected().ToArray();

        w.SetComponent(a, new SelectionState { IsSelected = true, IsPrimarySelection = false });
        w.SetComponent(b, new SelectionState { IsSelected = true, IsPrimarySelection = true });

        var second = src.Selected();

        Assert.NotEqual(first, second.ToArray());
        Assert.Equal(b, second[0]);
    }

    /// <summary>
    /// ⭐⭐ <b>A world that never registered <c>SelectionState</c> yields EMPTY, ⛔ not a throw.</b>
    /// ⚠ Worth a rail because editor worlds differ by which subsystems booted — 📐 and because the
    /// safety is not obvious from the call site: it comes from <c>With&lt;T&gt;</c> being a bit-set.
    /// </summary>
    [Fact]
    public void AWorldThatNeverRegisteredTheComponent_YieldsEmpty()
        => Assert.Empty(new WorldEntitySelectionSource(new EntityRepository()).Selected());

    // ══ the context carries it ═══════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>The builder passes the source's OWN instance through</b> — ⛔ no copy, or §6 <c>L0.4</c>'s
    /// clause would be defeated one layer up.
    /// </summary>
    [Fact]
    public void TheContextCarriesTheSourcesOwnInstance()
    {
        var w = World();
        Selected(w, primary: true);
        var src   = new WorldEntitySelectionSource(w);
        var store = new EditorSelectionStore();

        var one = DetailsContextBuilder.Build(store, "BTree", VariableRunState.Planning, src);
        var two = DetailsContextBuilder.Build(store, "BTree", VariableRunState.Planning, src);

        Assert.Same(one.Entities, two.Entities);
        Assert.Equal(one, two);        // ⭐ §2b's pan guarantee, through the entity field
    }

    /// <summary>⚠ With no source the context is EMPTY, never null — ⛔ the honest headless answer.</summary>
    [Fact]
    public void WithNoSource_TheContextHasNoEntities()
        => Assert.Empty(DetailsContextBuilder
            .Build(new EditorSelectionStore(), "BTree", VariableRunState.Planning).Entities);

    /// <summary>
    /// ⭐⭐ <b>A source with no world yet answers EMPTY rather than throwing.</b>
    /// ⚠⚠ 📐 Measured at the composition root: the world field is nullable and read lazily
    /// *(<c>ClockIsHalted</c>)*, so the source must resolve it at CALL time — ⛔ capturing it eagerly
    /// would bind null for the editor's lifetime, silently.
    /// </summary>
    [Fact]
    public void ASourceWhoseWorldIsNotReadyYet_AnswersEmptyThenRecovers()
    {
        EntityRepository? world = null;
        var src = new WorldEntitySelectionSource(() => world);

        Assert.Empty(src.Selected());

        world = World();
        var e = Selected(world, primary: true);

        Assert.Equal(new[] { e }, src.Selected().ToArray());
    }

    // ══ R-67 — the PRODUCTION registrar was actually given one ═══════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The control the <c>2026-08-16</c> rule prescribes: a forwarding rail asserted on the
    /// CONSTRUCTED object.</b> 📌 <i>"a production caller that HAS a dependency must PASS it"</i>, and
    /// ⛔ <c>EntitySelection</c> is optional, so only this can tell a real source from the default.
    /// </summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    [InlineData("Blueprint")]
    public void TheProductionServices_ForwardTheEntitySourceToTheRegistrar(string perspective)
    {
        var services = new PerspectiveWorkspaceServices(
            new AssetCatalog(), new Windows.TheDefaultLayoutIsNotStaleTests.NoRefactor(),
            new DebugSessionRegistry(),
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            isSimUp: () => false, isFrozen: () => false)
        {
            EntitySelection = new WorldEntitySelectionSource(World()),
        };

        var registrar = services.CreateRegistrar(
            perspective, new EditorSelectionStore(), Array.Empty<IAssetValidator>());

        Assert.IsType<WorldEntitySelectionSource>(registrar.EntitySelection);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>…and it reaches the CONTEXT, not just the field.</b> 📌 <c>M-22</c>: <i>"'is it
    /// connected?' is not 'does anything flow?'"</i> — ⚠ this is the half that catches a source held
    /// and never read.
    /// </summary>
    [Fact]
    public void TheProductionShell_SeesTheWorldsSelection()
    {
        var w = World();
        var e = Selected(w, primary: true);

        var services = new PerspectiveWorkspaceServices(
            new AssetCatalog(), new Windows.TheDefaultLayoutIsNotStaleTests.NoRefactor(),
            new DebugSessionRegistry(),
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            isSimUp: () => false, isFrozen: () => false)
        {
            EntitySelection = new WorldEntitySelectionSource(w),
        };

        var registrar = services.CreateRegistrar(
            "BTree", new EditorSelectionStore(), Array.Empty<IAssetValidator>());

        Assert.Equal(new[] { e }, registrar.Details!.Frame().Context.Entities.ToArray());
    }
}
