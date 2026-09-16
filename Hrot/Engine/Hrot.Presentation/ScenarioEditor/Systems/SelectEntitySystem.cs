using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Common.Events;

namespace Hrot.ScenarioEditor.Systems;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-051</c> (Axis-C <b>E3</b>) — <see cref="SelectEntityCommand"/> writes the viewport
/// selection. SHARED.</b>
/// 📄 <b><c>docs/DESIGN_Cgf_Tool_Selection_Camera_Slice.md</c></b> §3 ②, §7.
///
/// <para>🔴🔴 <b>MEASURED <c>2026-08-26</c> — THIS IS NEW CAPABILITY, NOT AN EXTRACTION, and that is a
/// correction to the design's premise.</b> §3 ② lists this system beside two whose bodies come out of
/// <c>EditorSubsystem</c>'s drain. 📐 But a full-repo sweep found <c>SelectEntityCommand</c> is
/// <b>published and never read</b>: the only references are
/// <c>EditorApplication.SelectEntity</c> *(publish)*, <c>PresentationComponentRegistry</c>
/// *(<c>RegisterEvent</c>)* and the struct itself. ⇒ ⛔ <b><c>IEditorLogic.SelectEntity(long)</c> has been
/// a SILENT NO-OP on every host</b> — the panel calls it, the command is published, nothing consumes it,
/// and nothing ever reported that. ⭐ This system is the consumer that makes it real.</para>
///
/// <para>⭐⭐ <b>Why the absence was invisible.</b> The publisher exists, the event is registered, and the
/// facade method is documented as *"programmatically selects the entity … switching to the Select tool if
/// required"*. ⚠ Nothing in that chain fails; the write simply never happens. ⛔ A reference COUNT on
/// <c>SelectEntityCommand</c> is non-zero, which is why the seam law's *"never read a reference count as
/// adoption"* applies exactly here.</para>
///
/// <para>⚠ <b>Scope:</b> this is the E3 <b>viewport</b> selection *(<c>ISelectionState</c> —
/// <c>PrimarySelected</c>)*. ⛔ It is NOT <c>IMapPickService</c>'s transient click-to-resolve, which is
/// Axis-B and untouched *(design §2/§8)*.</para>
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
public sealed class SelectEntitySystem : IEcsModuleSystem
{
    private readonly Func<ISelectionState?> _selection;
    private readonly Action<Entity>? _alsoSelect;

    /// <param name="alsoSelect">
    /// ⭐ Optional host hook run with the same entity after the selection state is written.
    ///
    /// <para>📐 It exists because CGF's hand-rolled context-menu *"Select entity"* set **two** things:
    /// <c>ISelectionState.PrimarySelected</c> **and** its inspector-panel state. ⭐ The first is the shared
    /// concept; the second is a host's own panel wiring. ⇒ keeping the second as a hook is what lets CGF's
    /// parallel be deleted without losing the inspector follow-through — ⛔ rather than pushing a panel
    /// type into this assembly.</para>
    /// </param>
    public SelectEntitySystem(Func<ISelectionState?> selection, Action<Entity>? alsoSelect = null)
    {
        _selection  = selection ?? throw new ArgumentNullException(nameof(selection));
        _alsoSelect = alsoSelect;
    }

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world) return;

        // ⚠ Resolved per execute — see InteractionDeps' remarks on the editor's build/teardown order.
        var selection = _selection();
        if (selection == null) return;

        foreach (ref readonly var cmd in world.Bus.Read<SelectEntityCommand>())
        {
            // ⭐ BP-508 — the ONE resolver (R-77).
            var target = NetworkIdResolver.FindEntityByNetworkId(world, cmd.NetworkId);
            if (target.IsNull) continue;

            selection.PrimarySelected = target;
            _alsoSelect?.Invoke(target);
        }
    }
}
