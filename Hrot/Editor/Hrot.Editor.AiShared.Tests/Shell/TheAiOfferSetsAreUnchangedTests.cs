using System;
using System.Linq;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L6</c>'s STAGE GATE — the three AI perspectives' offer sets, FROZEN.</b>
/// 📄 [`HANDOFF_L6_Entity_Views_On_Scenario.md`] §1 item 1 and §2:
/// <i>"STAGE GATE: BTree/HSM/Blueprint each still host their SAME offer set … ANY change ⇒ HALT the L6
/// line and REPORT — the extraction is wrong and everything downstream would inherit it."</i>
///
/// <para>⛔⛔ <b>THIS FILE WAS WRITTEN AND MADE GREEN <i>BEFORE</i> <c>L6.1a</c> TOUCHED ANYTHING.</b>
/// ⚠ That order is the whole value: a baseline captured AFTER a refactor records whatever the refactor
/// did, which is not a gate at all. 📌 The same discipline as <c>B48 §0</c>'s persistence-shape
/// baseline. ⭐ The expected sets below are <b>measured</b>, not designed.</para>
///
/// <para>⭐⭐ <b>Through the PRODUCTION composition</b> — <c>PerspectiveWorkspaceServices.CreateRegistrar</c>,
/// the same call <c>EditorSubsystem</c> makes *(<c>R-67</c>)*. ⛔ A hand-built registry would pass while
/// the editor registered nothing.</para>
///
/// <para>⚠ <b>It asserts the WHOLE SET, ordered, not "contains".</b> ⛔ A <c>Contains</c> gate would miss
/// exactly the two failures an extraction causes: a view <b>dropped</b>, and a view <b>gained</b> by a
/// perspective it does not belong to. ⭐ Registration order is asserted too, because
/// <c>DetailsViewRegistry.OfferSet</c> breaks rank ties by it — a reshuffle would flip which view a
/// designer sees by default.</para>
///
/// <para>⛔⛔ <b><c>S1</c> (<c>BP-399</c>, <c>2026-08-22</c>) — THE <b>BLUEPRINT</b> ROW MOVED, AND IT MOVED
/// <i>BY DESIGN</i>.</b> 📄 <b><c>DESIGN_Details_Panel_View_Switching.md</c> §7.3 ①</b>: <i>the shell is
/// built for EVERY perspective</i>, so Blueprint now HAS a <c>DetailsWindow</c> and therefore gains the
/// generic <c>details.variables</c> descriptor the shell contributes. ⭐ <b>Scenario · BTree · HSM are
/// ordered-EQUAL to the pre-<c>S1</c> measurement</b> — that half is still a frozen baseline and is the
/// stage gate (<c>TASKS_One_Shell_BP399.md</c> §2 ①). ⚠ <b>Only the row the approved design changes was
/// re-expressed</b>; ⛔ per <c>B101c</c> the DIRECTION was established first (the design says Blueprint
/// gets the shell) and only then were the expectations moved — this is not "updating the expected list"
/// to hide a drift.</para>
/// </summary>
public sealed class TheAiOfferSetsAreUnchangedTests
{
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

    /// <summary>⭐ The registered view ids, in registration order — the thing the gate compares.</summary>
    private static string[] RegisteredIds(string perspective)
        => Production(perspective).DetailsViews.All.Select(d => d.Id).ToArray();

    // ══ THE FROZEN SETS — measured 2026-08-22, BEFORE L6.1a ══════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The gate.</b> ⚠ If this reddens during <c>L6.1a</c>, the extraction moved a registration
    /// — ⛔ HALT, do not "update the expected list". 📌 That is what "self-certifying past a drift"
    /// means, and the handoff names it as the one place a false green is expensive.
    /// </summary>
    [Theory]
    [MemberData(nameof(FrozenOfferSets))]
    public void TheAiPerspectives_RegisterExactlyTheseViews_InThisOrder(
        string perspective, string[] expected)
        => Assert.Equal(expected, RegisteredIds(perspective));

    /// <remarks>
    /// 📐 <b>MEASURED <c>2026-08-22</c>, before <c>L6.1a</c>.</b> ⚠⚠ <b>My first guess was WRONG on all
    /// three rows</b>, which is exactly why the baseline is measured and not written from the design:
    /// I expected <c>details.variables</c> first and a <c>details.runtime.*</c> per AI host.
    ///
    /// <para>⚠ <b>Two things the measurement says, stated so nobody reads this list as the whole
    /// story:</b>
    /// <list type="number">
    ///   <item><b>No <c>details.runtime.*</c> appears.</b> 📐 <c>RuntimeInspectorWindow.RegisterPane</c>
    ///   is what adds them, and this composition registers no panes — the BTree/HSM editor registrars
    ///   supply those, and they are not in this assembly. ⇒ ⛔ the gate does NOT cover the runtime
    ///   descriptors; it covers what THIS composition builds, before and after, which is what an
    ///   extraction can break.</item>
    ///   <item><b>Blueprint has no <c>details.variables</c>.</b> ⭐ Consistent, not a defect: the
    ///   variables descriptor is contributed by the generic <c>DetailsWindow</c>, and <c>Details</c> is
    ///   deliberately <c>null</c> on Blueprint *(it has <c>BlueprintDetailsWindow</c>)* — the last rail
    ///   in this file pins that. ⛔⛔ <b>SUPERSEDED BY <c>S1</c></b> — see the note below.</item>
    /// </list></para>
    ///
    /// <para>⛔⛔ <b><c>S1</c>, <c>2026-08-22</c> — the Blueprint row is now
    /// <c>{ details.blackboard, details.variables }</c>.</b> ⭐ <b>Cause, named:</b>
    /// <c>PerspectiveWorkspaceRegistrar</c> used to build the shell only inside the
    /// <c>effectiveHost != null</c> gate, so Blueprint got no <c>DetailsWindow</c> and therefore none of
    /// the descriptors the shell contributes. §7.3 ① moves that construction OUT of the gate ⇒ Blueprint
    /// gets the same shell as its neighbours, and the same generic view with it. ⚠ <b>The BTree and HSM
    /// rows are byte-for-byte the pre-<c>S1</c> measurement</b> — ⛔ if either of THOSE ever reddens, the
    /// old rule applies unchanged: HALT, do not edit the list.</para>
    /// </remarks>
    public static TheoryData<string, string[]> FrozenOfferSets => new()
    {
        // ⭐ FROZEN — measured before L6.1a, unchanged by S1.
        { "BTree",     new[] { "details.blackboard", "details.variables" } },
        { "HSM",       new[] { "details.blackboard", "details.variables" } },
        // ⛔ RE-EXPRESSED by S1, per DESIGN_Details_Panel_View_Switching.md §7.3 ①. Was
        //   { "details.blackboard" } while Blueprint had no shell.
        { "Blueprint", new[] { "details.blackboard", "details.variables" } },
    };

    /// <summary>
    /// ⛔ <b>The negative half — the SHARED registrar must stay host-AGNOSTIC.</b>
    ///
    /// <para>⭐ <b>Why this rail changed shape at <c>S1</c>, stated plainly.</b> Before <c>S1</c> the
    /// reachable cross-contamination was <i>"Blueprint must not get the generic variables panel"</i> —
    /// ⛔ but §7.3 ① makes exactly that the DESIGNED outcome, so that assertion no longer says anything
    /// true. ⚠ The remaining two clauses (<c>details.graphsignature</c> on BTree/HSM) were already
    /// <b>vacuous</b>: 📐 measured — <c>graphsignature</c> is not registered by this composition on ANY
    /// perspective, so they asserted an absence nothing could produce. ⛔ Keeping them would be a rail
    /// that cannot redden, which is worse than no rail (<c>BP-402</c> ①).</para>
    ///
    /// <para>⭐⭐ <b>The claim that IS reachable and IS independent of <c>FrozenOfferSets</c>:</b> the
    /// three AI perspectives register the <b>SAME</b> set from the shared composition. 📌 That is the
    /// property <c>S1</c> creates and the one a future edit would break — a perspective-specific view
    /// registered into <c>PerspectiveWorkspaceRegistrar</c> instead of into its own host editor's
    /// contribution reddens here, whatever the lists above say. ⚠ <b>Host-contributed views are
    /// deliberately out of scope</b> — <c>details.nodeproperties</c> and <c>details.runtime.*</c> come
    /// from the BTree/HSM/Blueprint editor registrars, which are not in this assembly.</para>
    /// </summary>
    [Fact]
    public void TheSharedRegistrar_OffersTheSameViewsToEveryAiPerspective()
    {
        var btree     = RegisteredIds("BTree");
        var hsm       = RegisteredIds("HSM");
        var blueprint = RegisteredIds("Blueprint");

        Assert.Equal(btree, hsm);
        Assert.Equal(btree, blueprint);
    }

    /// <summary>
    /// ⭐⭐ <b>And the shell's presence per perspective — now UNIVERSAL, which is the whole of <c>S1</c>.</b>
    ///
    /// <para>⛔⛔ <b>RE-EXPRESSED, <c>2026-08-22</c>.</b> This rail used to pin <c>Blueprint ⇒ false</c>
    /// with the reason <i>"it has its own <c>BlueprintDetailsWindow</c>, and a second panel there would be
    /// two for one concept."</i> ⭐ <b>The reason was right and the conclusion is now reached the other
    /// way round:</b> §7.3 ①③ keeps ONE panel per perspective by retiring
    /// <c>BlueprintDetailsWindow</c> and giving Blueprint the SAME shell, not by leaving Blueprint
    /// without one. ⚠ <b>So this is the same invariant, inverted by the design, not a weakened gate</b>
    /// — and it is now an <c>Assert.NotNull</c> on every row, which is strictly harder to satisfy by
    /// accident than the old mixed table.</para>
    /// </summary>
    [Theory]
    [InlineData("BTree")]
    [InlineData("HSM")]
    [InlineData("Blueprint")]
    public void EveryAiPerspective_HasTheShell(string perspective)
        => Assert.NotNull(Production(perspective).Details);
}
