using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Hrot.ScenarioEditor.Systems;

/// <summary>
/// ⭐⭐⭐ <b><c>UXI-11</c> slice <c>S-3</c> — points a host's inspector context at whatever the
/// selection became, from the ANNOUNCEMENT rather than from one of its causes.</b>
/// 📄 <c>docs/UX/UX_Feature_Selection.md</c> §2.7.2 / §2.7.3 rule 4.
///
/// <para>🔴 <b>What it replaces, and why the replacement is not cosmetic.</b> 📐 Measured
/// <c>2026-09-20</c>: the editor and IG each hand-synced <c>IInspectorContext.SelectedEntity</c> from
/// <c>SelectionInteractionSystem.OnSelectionChanged</c> — a callback that fires <b>only for a MAP
/// click</b>. ⇒ an inspector click, a context-menu <i>Select</i>, or a <c>CMD_SET_SELECTION</c> from
/// ExCon changed the selection and the inspector context <b>did not follow</b>. ⭐ One publisher, every
/// cause, one consumer.</para>
///
/// <para>⚠ <b>Why a SYSTEM and not a panel subscription.</b> A bus event is readable for exactly one
/// frame. A system runs every frame by construction; ⛔ an ImGui panel does not — collapsed, on a
/// hidden tab, or simply not drawn, it would miss the event and stay stale forever. ⇒ the panels
/// PROJECT from <see cref="ISelectionState"/> each draw (which cannot miss), and the notification
/// serves the consumers that genuinely need an EDGE. 📌 <c>S-5</c>'s "losing selection cancels that
/// entity's edit" is the next one.</para>
///
/// <para>⚠ <c>PostSimulation</c>, like its publisher — and registered AFTER
/// <c>SelectionRequestSystem</c>, so a request and its consequence land in the same frame.</para>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class SelectionNotificationSystem : IEcsModuleSystem
{
    private readonly Func<IInspectorContext?> _inspector;

    /// <param name="inspector">
    /// ⚠ Resolved per execute, not captured: a host rebuilds its inspector state on scenario reload,
    /// and a captured instance would point at the dead one.
    /// </param>
    public SelectionNotificationSystem(Func<IInspectorContext?> inspector)
        => _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world) return;

        var inspector = _inspector();
        if (inspector == null) return;

        foreach (var note in world.Bus.ReadManaged<SelectionChangedNotification>())
        {
            if (note == null) continue;
            // ⚠ The PRIMARY, not the set: IInspectorContext is single-entity by contract, and the
            //   panel reads the full set from ISelectionState. ⛔ Inventing a "first of many" rule
            //   here would be a second opinion about what "the" selected entity is.
            inspector.SelectedEntity = note.Primary;
        }
    }
}
