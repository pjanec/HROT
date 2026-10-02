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
public sealed class SelectionRequestSystem : IEcsModuleSystem
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
    public SelectionRequestSystem(Func<ISelectionState?> selection, Action<Entity>? alsoSelect = null)
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

        // ── the NETWORK-ID-addressed form: the boundary (UXI-11 S-2) ─────────────
        // ⭐ SelectEntityCommand stays because panels and the orbat address entities by network id
        //   and must not learn about ECS handles. 🔒 §2.7.3 rule 7 — selection is host-local and the
        //   wire form is translated AT THE BOUNDARY. This loop is that boundary, and it is one line.
        foreach (ref readonly var cmd in world.Bus.Read<SelectEntityCommand>())
        {
            // ⭐ BP-508 — the ONE resolver (R-77).
            var target = NetworkIdResolver.FindEntityByNetworkId(world, cmd.NetworkId);
            if (target.IsNull) continue;

            selection.PrimarySelected = target;
            _alsoSelect?.Invoke(target);
            Announce(world, selection, "SelectEntityCommand");
        }

        // ── the ENTITY-addressed form with a set and a mode (UXI-11 S-2) ─────────
        foreach (var req in world.Bus.ReadManaged<SelectionChangeRequest>())
        {
            if (req == null) continue;

            Apply(world, selection, req);
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Announces what the selection BECAME.</b> 📄 §2.7.2 — <c>UXI-11</c> slice <c>S-3</c>.
    ///
    /// <para>⚠ <b>Published AFTER the store is written, in the same tick</b>, so a consumer that reads
    /// <c>ISelectionState</c> while handling the notification sees the new value. ⛔ Announcing first
    /// would hand every subscriber the state it is replacing.</para>
    ///
    /// <para>⭐ It carries the WHOLE selection, not a delta — a subscriber that missed a frame is
    /// correct again after the next one, and it never has to keep a running copy (which is the
    /// parallel-store disease <c>S-1</c> removed).</para>
    /// </summary>
    private static void Announce(EntityRepository world, ISelectionState selection, string? reason)
    {
        // ⚠ A COPY, not the live collection. EcsSelectionState.SelectedEntities hands back its own
        //   observation buffer, which it rewrites on the next read ⇒ handing that to subscribers
        //   would give them a list that silently changes underneath them.
        var snapshot = new System.Collections.Generic.List<Entity>(selection.SelectedEntities);

        world.Bus.PublishManaged(new SelectionChangedNotification
        {
            Selected = snapshot,
            Primary  = selection.PrimarySelected,
            Reason   = reason,
        });
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The ONE place the selection changes.</b> 📄 <c>UX_Feature_Selection.md</c> §2.7.3 rule 1.
    /// ⚠ Dead entities are dropped here rather than at the publisher: a request may sit on the bus for
    /// a frame, and whether its targets are still alive is a question only this point can answer.
    /// </summary>
    private void Apply(EntityRepository world, ISelectionState selection, SelectionChangeRequest req)
    {
        if (req.Mode == SelectionChangeMode.Clear)
        {
            selection.Clear();
            // ⚠⚠ A CLEAR IS A CHANGE AND MUST BE ANNOUNCED. 📌 It was not, for about ten minutes:
            //    this branch returned early and skipped the Announce at the foot of the method, so
            //    every subscriber kept painting the selection that had just been emptied — the exact
            //    "accepted and silently discarded" shape. ⭐ Caught by
            //    ClearingTheSelectionClearsTheInspectorContext, which is why that rail exists.
            Announce(world, selection, req.Reason);
            return;
        }

        var live = new System.Collections.Generic.List<Entity>(req.Entities.Count);
        foreach (var e in req.Entities)
            if (!e.IsNull && world.IsAlive(e)) live.Add(e);

        switch (req.Mode)
        {
            case SelectionChangeMode.Replace:
                // ⚠ An empty REPLACE clears -- which is what an empty-space click means. ⛔ It is NOT
                //   the same as "the request named entities and they all died"; that also clears, and
                //   deliberately: selecting nothing is the honest outcome either way.
                selection.SetMultiple(live);
                break;

            case SelectionChangeMode.Add:
                foreach (var e in live) selection.Add(e);
                break;

            case SelectionChangeMode.Remove:
                foreach (var e in live) selection.Remove(e);
                break;
        }

        // ⭐ The host hook runs for the primary, as it does on the network-id path, so a host that
        //   follows selection with its own panel wiring behaves the same whichever form was used.
        if (_alsoSelect != null && selection.PrimarySelected is { } primary && !primary.IsNull)
            _alsoSelect(primary);

        Announce(world, selection, req.Reason);
    }
}
