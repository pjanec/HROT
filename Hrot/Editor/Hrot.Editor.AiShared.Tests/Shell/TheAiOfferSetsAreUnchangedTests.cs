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
    ///   in this file pins that.</item>
    /// </list></para>
    /// </remarks>
    public static TheoryData<string, string[]> FrozenOfferSets => new()
    {
        { "BTree",     new[] { "details.blackboard", "details.variables" } },
        { "HSM",       new[] { "details.blackboard", "details.variables" } },
        { "Blueprint", new[] { "details.blackboard" } },
    };

    /// <summary>
    /// ⛔ <b>The negative half — a perspective must not GAIN a neighbour's view.</b>
    /// ⚠ Without this, an extraction that registered every runtime pane on every perspective would
    /// still satisfy an ordered-equality check per row only by luck; ⭐ stating the exclusion makes the
    /// claim independent of the lists above.
    /// </summary>
    [Fact]
    public void NoAiPerspective_HostsAnotherPerspectivesView()
    {
        // ⚠ 📐 Measured: no `details.runtime.*` is registered in THIS composition at all (see the
        //   FrozenOfferSets remarks), so naming those ids here would assert an absence that is already
        //   guaranteed — a vacuous half. ⭐ The reachable cross-contamination is the GENERIC views, and
        //   the one perspective that must NOT have the generic variables panel is Blueprint.
        Assert.DoesNotContain("details.variables",    RegisteredIds("Blueprint"));
        Assert.DoesNotContain("details.graphsignature", RegisteredIds("BTree"));
        Assert.DoesNotContain("details.graphsignature", RegisteredIds("HSM"));
    }

    /// <summary>
    /// ⭐⭐ <b>And the DetailsWindow's presence per perspective is frozen too.</b>
    /// ⚠ 📌 <c>Details</c> is deliberately <c>null</c> on Blueprint — it has its own
    /// <c>BlueprintDetailsWindow</c>, and a second panel there would be two for one concept. ⛔ An
    /// extraction that "helpfully" gave Blueprint a generic panel would be a behaviour change wearing a
    /// refactor's name, and the id lists alone would not see it.
    /// </summary>
    [Theory]
    [InlineData("BTree",     true)]
    [InlineData("HSM",       true)]
    [InlineData("Blueprint", false)]
    public void TheDetailsWindowPresence_IsUnchanged(string perspective, bool hasWindow)
        => Assert.Equal(hasWindow, Production(perspective).Details is not null);
}
