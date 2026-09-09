using System;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Common;
using Hrot.Common.Events;
using Hrot.ScenarioEditor.Tools;

namespace Hrot.ScenarioEditor.Systems;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-051</c> (Axis-C <b>E3</b>) — the tool-activation drain, SHARED. Finishes
/// <c>PACK2-E002</c>.</b>
/// 📄 <b><c>docs/DESIGN_Cgf_Tool_Selection_Camera_Slice.md</c></b> §1, §3 ②, §4, §5 ·
/// <b><c>docs/UX/UX_Feature_Tool_Model.md</c></b> §4.7c.
///
/// <para>🔴 <b>What it replaces: a switch welded into a 5 000-line host.</b> 📐 Measured
/// <c>2026-08-26</c>: the whole "tool system" was `EditorSubsystem.DrainToolActivationEvents` — the
/// `EditorTool` enum, a `switch`, and the (already shared) gizmos. ⭐ CGF had the same primitives and
/// reached them through **hand-rolled context-menu callbacks instead**, which is how the two drifted.</para>
///
/// <para>⭐⭐⭐ <b>WHAT IT IS NOW, after <c>UXI-07</c> step 3b: an EVENT ADAPTER and nothing else.</b>
/// It turns a target-less <see cref="ActivateEditorToolEvent"/> into
/// <c>IToolController.Activate(id, target)</c> by supplying the primary selection as the target.
/// ⛔ <b>It no longer owns the tool set.</b> 📐 The six descriptors and their arm bodies moved to
/// <see cref="ScenarioToolRegistrations"/>, which <c>MapInteractionPack.Build</c> calls — because
/// <c>Build</c> is called by <b>FIVE</b> hosts and only <b>TWO</b> compose this drain, so a tool set
/// owned here reached two hosts out of five and the other three hand-rolled the same gizmos.</para>
///
/// <para>🔒 <b>That split is <c>Q26</c> constraint 3</b> — <i>"a tool descriptor is shared; its
/// activation is host-bound"</i>. ⭐ This class is one of the ways a host reaches the shared set; a host
/// with no event path can call <c>MapInteraction.Tools.Activate</c> directly, and both routes land on the
/// same registration. ⛔ There is exactly ONE implementation of each tool (ruling 9).</para>
/// </summary>
/// <remarks>
/// ⚠⚠ <b><c>[UpdateInPhase(PostSimulation)]</c> — and its ABSENCE was a BOOT CRASH, caught by T3.</b>
/// 📐 <c>SystemScheduler.RegisterSystem</c> throws <c>"System X must have [UpdateInPhase] attribute"</c>, so
/// <c>kernel.Initialize()</c> — and therefore the whole editor — failed to start. ⛔ Every unit rail passed:
/// the test's recording registry accepted any system, so it never asked the question the real scheduler asks.
/// ⇒ ⭐ the rail now asserts the attribute is present *(see <c>TheViewportInteractionIsSharedTests</c>)*.
///
/// <para>⭐ <b>Why <c>PostSimulation</c>:</b> 📐 the editor's old drain ran from <c>EditorSubsystem.Update()</c>
/// at <c>:2239</c>, AFTER <c>_kernel.Update()</c> at <c>:2232</c> — so a gizmo it activated first executed on
/// the following frame either way. ⭐ <c>PostSimulation</c> is the main-thread phase the gizmo group and the
/// sibling <c>CanvasMenuUpdateSystem</c> already use, which keeps this within one frame of the old ordering.
/// ⛔ Not <c>Simulation</c>: that runs on background threads and this touches ImGui-adjacent host state.</para>
/// </remarks>
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class ToolActivationDrainSystem : IEcsModuleSystem
{
    private readonly Func<ISelectionState?>       _selection;
    private readonly Func<DataDrivenGizmoSystem?> _gizmos;
    private readonly Func<ToolController?>        _tools;
    private readonly Action<string>?              _reportUnserviceable;

    /// <param name="selection">
    /// ⚠⚠ A RESOLVER, not an instance — the editor creates its selection state AFTER
    /// <c>kernel.Initialize()</c> and nulls it on teardown, so a captured instance would be permanently
    /// null. Same reason as every other member of <c>ScenarioEditorModule.InteractionDeps</c>.
    /// </summary>
    /// <param name="gizmos">
    /// The entity-scoped arbiter. ⭐ Used ONLY as the host-is-ready guard — the tools themselves resolve it
    /// through their own registration. ⛔ <see langword="null"/> ⇒ the drain idles rather than throwing.
    /// </param>
    /// <param name="tools">
    /// 🔒 <b>The host's arbiter — pass <c>MapInteraction.Tools</c>.</b> ⛔ REQUIRED, and deliberately not
    /// defaulted to a freshly-constructed controller: a drain with a private arbiter would arbitrate
    /// nothing the rest of the host knows about, which is the silent-default failure this programme keeps
    /// finding (*"a production caller that HAS the dependency must PASS it"*).
    /// </param>
    /// <param name="reportUnserviceable">
    /// Where *"this host cannot service tool X"* goes for the DRAIN's own refusals. ⚠ The TOOLS report
    /// through the pack's <c>ReportUnserviceableTool</c>; this one covers the drain's own cases.
    /// ⭐ Defaults to the FDP log; a rail injects a recorder.
    /// </param>
    public ToolActivationDrainSystem(
        Func<ISelectionState?>       selection,
        Func<DataDrivenGizmoSystem?> gizmos,
        Func<ToolController?>        tools,
        Action<string>?              reportUnserviceable = null)
    {
        _selection           = selection ?? throw new ArgumentNullException(nameof(selection));
        _gizmos              = gizmos    ?? throw new ArgumentNullException(nameof(gizmos));
        _tools               = tools     ?? throw new ArgumentNullException(nameof(tools));
        _reportUnserviceable = reportUnserviceable;
    }

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world) return;

        // ⚠⚠ Resolved PER EXECUTE, not captured — see the constructor's remarks.
        var selection = _selection();
        var gizmos    = _gizmos();
        if (selection == null || gizmos == null) return;

        var tools = _tools();
        if (tools == null)
        {
            // ⛔ Never silently: a host that scheduled the drain but wired no arbiter would otherwise
            //    swallow every tool press. ⭐ Drain the events anyway so they do not pile up.
            foreach (ref readonly var _ in world.Bus.Read<ActivateEditorToolEvent>()) { }
            Report("tool activation was dropped — this host scheduled the drain but wired no ToolController "
                 + "(pass MapInteraction.Tools).");
            return;
        }

        foreach (ref readonly var evt in world.Bus.Read<ActivateEditorToolEvent>())
        {
            // ⭐⭐⭐ The WHOLE drain, because "which tool is active" is no longer this system's business.
            //    The controller cancels the OTHER arbiter's modal before arming, which is UXI-07's
            //    correctness change (ToolController.CancelOtherArbiter).
            // ⚠ The TARGET is the primary selection, exactly as the old switch did. 📐 A context-menu
            //   caller SELECTS first and then activates — a CALLER concern, which is why the editor's
            //   action path can publish this event instead of duplicating an arm body.
            var target = selection.PrimarySelected is { } p ? p : Entity.Null;
            tools.Activate(ScenarioToolIds.ForEditorTool(evt.Tool), target);
        }
    }

    private void Report(string message)
    {
        if (_reportUnserviceable != null) _reportUnserviceable(message);
        else FdpLog<ToolActivationDrainSystem>.Info("[Tools] {0}", message);
    }
}
