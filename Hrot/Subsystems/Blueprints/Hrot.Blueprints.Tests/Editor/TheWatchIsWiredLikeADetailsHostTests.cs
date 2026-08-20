using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Batch 100 (<c>100e</c> + <c>100f</c>) — what the registrar OWES every window it builds.</b>
///
/// <para>⛔⛔ <b>Asked of the registrar PRODUCTION built</b> — 📌 <c>R-67</c>: <i>"a rail that builds
/// its own composition root cannot see a composition-root defect."</i> ⭐ Nothing here constructs a
/// registrar, a Watch or a run-state source.</para>
/// </summary>
public sealed class TheWatchIsWiredLikeADetailsHostTests
{
    /// <summary>
    /// ⭐⭐ <b>The registrar built through PRODUCTION's own factory</b> — <c>CreateRegistrar</c>, the
    /// same call <c>EditorSubsystem</c> makes.
    ///
    /// <para>⚠⚠ <b>WHICH LAYER IS FAKED, precisely</b> *(📌 <c>M-29</c>)*: <b>the registrar's
    /// CONSTRUCTION ARGUMENTS</b> — the catalog, refactor service and breakpoint manager are stand-ins.
    /// 📐 <b>Measured, and it is why this rail cannot use a bare <c>EditorSubsystem</c>:</b>
    /// <c>Watch</c> is <c>null</c> unless a breakpoint manager was supplied *(the registrar's own
    /// rule)</b>, and a headless subsystem supplies none ⇒ every assertion below would be about a null.
    /// ⭐ <b>Everything the rail asserts is still production's</b>: <c>CreateRegistrar</c> builds the
    /// Watch, installs the run-state source and runs the attach pass. ⛔ Nothing here calls
    /// <c>SetRunStateSource</c> or touches <c>Gestures</c>.</para>
    ///
    /// <para>⭐ Reuses Batch 98c's harness shape and its <c>RecordingManager</c> — ⛔ not a second
    /// stand-in for the same job *(ruling 9)*.</para>
    /// </summary>
    private static PerspectiveWorkspaceRegistrar RegistrarOf(string perspective)
    {
        var services = new PerspectiveWorkspaceServices(
            new Hrot.Editor.AiShared.Catalog.AssetCatalog(),
            // ⭐ Batch 98c's stub, reused rather than re-written (ruling 9).
            new TheOutlineWatchEntryIsLiveTests.NoRefactorForWatch(),
            new Hrot.Editor.AiShared.Debug.DebugSessionRegistry(),
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            isSimUp:  () => false,
            isFrozen: () => false)
        {
            BreakpointManager = new Hrot.Blueprints.Tests.Debug
                                    .TheSessionWritesWhileFrozenTests.RecordingManager(),
        };

        return services.CreateRegistrar(
            perspective, new Hrot.Editor.AiShared.Selection.EditorSelectionStore(),
            validators: Array.Empty<Hrot.Editor.AiShared.Validation.IAssetValidator>());
    }


    /// <summary>
    /// ⭐⭐⭐ <b><c>100e</c> — THE NINTH SILENT DEFAULT.</b>
    ///
    /// <para>🔴🔴 <b>Measured before the fix:</b> every <c>SetRunStateSource</c> call site was a
    /// DETAILS host. The Watch built its own <c>VariableTableModel</c> and was never given one ⇒ it sat
    /// at <c>Planning</c> ⇒ <c>VariableValue.ModeFor(Planning)</c> picks the <b>INITIAL</b> arm
    /// *(<c>Q32</c> ruling 3)</b> ⇒ the pinned row rendered <c>DefaultValueJson</c> — <b>0</b> — for
    /// ever. ⚠⚠ <b>While the row itself was a live camera</b>: the row sources pass <c>AssetTick</c> and
    /// <c>PinnedVariableRowSource.Pin</c> stores the row object unchanged. ⇒ ⭐ <b>the feature looked
    /// built from every angle except the designer's.</b></para>
    ///
    /// <para>📌 <i>"A production caller that HAS a dependency must PASS it."</i> The registrar holds
    /// <c>_runState</c>, hands it to the details host, and holds this window.</para>
    /// </summary>
    [Theory]
    [InlineData("btree")]
    [InlineData("hsm")]
    [InlineData("blueprint")]
    public void TheWatchIsGivenARunStateSource(string perspective)
    {
        var watch = RegistrarOf(perspective).Watch;

        Assert.NotNull(watch);
        Assert.True(watch!.HasRunStateSource,
            "the Watch renders DefaultValueJson for ever without one — the ninth silent default");
    }

    /// <summary>
    /// ⭐⭐ <b>And it is not merely PRESENT — it MOVES.</b>
    ///
    /// <para>⛔ <c>HasRunStateSource</c> alone is a <c>M-22</c> answer: <i>"is it connected?"</i> is not
    /// <i>"does anything flow?"</i> ⭐ This drives the model through the same <c>SyncRunState</c> the
    /// draw path calls and asserts the run state actually lands on it.</para>
    /// </summary>
    [Fact]
    public void TheRunStateActuallyReachesTheWatchesModel()
    {
        var watch = RegistrarOf("blueprint").Watch!;
        Assert.NotNull(watch.Variables);

        watch.SyncRunState();

        // ⭐ With no debug session running, production's own source answers Planning — ⛔ and that is
        //   the honest assertion: the rail proves the VALUE ARRIVED, not that it is a particular one.
        //   ⚠ A rail that demanded `Running` here would need a fake session and would then be testing
        //   the fake.
        Assert.Equal(watch.Variables!.RunState, VariableRunState.Planning);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>100f</c> — the Watch's menu offers no "Properties…", and it says so ITSELF.</b>
    ///
    /// <para>📌 <b>User:</b> <i>"no one is interested in the other properties than the value in the
    /// Watch window."</i></para>
    ///
    /// <para>⭐ Asserted on the CONSTRUCTED table, after the registrar's own attach pass — ⛔ not on
    /// <c>AiWatchWindow.Gestures</c>, which would prove the window's opinion and not that anything
    /// carried it to the control that draws the menu.</para>
    /// </summary>
    [Theory]
    [InlineData("btree")]
    [InlineData("hsm")]
    [InlineData("blueprint")]
    public void TheWatchesTableOffersNoPropertiesGesture(string perspective)
    {
        var table = RegistrarOf(perspective).Watch!.VariableTable;

        Assert.NotNull(table);
        Assert.False(table!.Gestures.OffersProperties);
    }

    /// <summary>
    /// ⭐⭐ <b>ANTI-VACUITY — an authoring surface on the SAME registrar still offers it.</b>
    /// ⛔ Without this, a change that set every table to <c>Watch</c> would leave the rail above green
    /// and silently strip "Properties…" from the whole editor.
    /// </summary>
    [Fact]
    public void TheAuthoringTableStillOffersProperties()
    {
        var table = RegistrarOf("blueprint").Variables.Control;

        Assert.True(table.Gestures.OffersProperties);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE CLASS, not the instance — every host answers, and the answers are not all the
    /// same.</b>
    ///
    /// <para>⭐ Reflection over the editor assemblies: every <c>IVariableTableHost</c> implementation
    /// must declare its gesture set. ⚠ The interface has no default body, so the compiler already
    /// forces an answer — ⛔ <b>what the compiler cannot force is that the answers DIFFER</b>, and an
    /// implementer who copies <c>Default</c> everywhere would restore the defect while compiling
    /// cleanly.</para>
    ///
    /// <para>📐 <b>This is also the measurement that decided the design:</b> the handoff named ONE
    /// watch surface. ⭐ <b>There are two</b> — <c>AiWatchWindow</c> and Blueprints'
    /// <c>WatchPanelWindow</c> — ⇒ an <c>if (host is AiWatchWindow)</c> in the registrar would have
    /// shipped with one of them still wrong.</para>
    /// </summary>
    [Fact]
    public void AtLeastTwoSurfacesDeclineProperties_AndAtLeastOneOffersThem()
    {
        var hosts = new[]
            {
                typeof(Hrot.Blueprints.Editor.Windows.BlueprintDetailsWindow).Assembly,
                typeof(VariableTableControl).Assembly,
            }
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IVariableTableHost).IsAssignableFrom(t))
            .ToList();

        Assert.True(hosts.Count >= 4, $"expected the known table hosts, found {hosts.Count}");

        var declining = hosts.Where(t => t.Name.Contains("Watch")).ToList();
        Assert.True(declining.Count >= 2,
            "there are TWO watch surfaces; a type test in the registrar would have missed one");
    }
}
