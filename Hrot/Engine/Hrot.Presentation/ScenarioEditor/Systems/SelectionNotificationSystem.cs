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
///
/// <para>⭐⭐⭐ <b><c>S-5</c> added the SECOND consequence</b> (<c>2026-09-20</c>): an entity that LOST the
/// selection has its edit cancelled. 🔒 User ruling ②. ⛔ Deliberately not a parallel system — this class
/// already is <i>"what a selection change causes"</i>, and a second consumer of one edge would be two
/// implementations of one concept (ruling 9). 📄 §2.7.15.</para>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class SelectionNotificationSystem : IEcsModuleSystem
{
    private readonly Func<IInspectorContext?> _inspector;
    private readonly Hrot.ScenarioEditor.Tools.IToolController? _tools;
    private readonly ISelectionState? _selection;
    private readonly Action<Entity?>? _aiEntity;

    /// <param name="inspector">
    /// ⚠ Resolved per execute, not captured: a host rebuilds its inspector state on scenario reload,
    /// and a captured instance would point at the dead one.
    /// </param>
    /// <param name="tools">
    /// ⭐ <c>S-5</c>'s arbiter. ⚠ Optional, and this one IS a legitimate default: a host with no tool
    /// controller has nothing that can be armed, so there is nothing to cancel. ⛔ In production
    /// <c>MapInteractionPack</c> always passes the one it built — the caller HAS it, so it passes it.
    /// </param>
    /// <param name="selection">
    /// ⭐ The store the arming used, read LIVE. 🔒 §4.14 ②: ruling ② is a <b>per-entity predicate</b> —
    /// <i>"is THIS entity still selected?"</i> — not a diff of the notification's set. ⭐ The notification
    /// is only the EDGE that says when to ask; the answer comes from the one store (<c>R-126</c>).
    /// </param>
    /// <param name="aiEntitySelection">
    /// ⭐⭐⭐ <c>CE-300</c> — the AI editors' entity cell, written from the ANNOUNCEMENT.
    /// 📄 <c>DESIGN_Editor_Entity_Selection_Source.md</c> §3.1.
    /// ⚠ A DELEGATE because the cell lives in <c>Hrot.Editor.AiShared</c>, which this assembly must not
    /// reference — same reason as <c>SelectionEgressSystem</c>'s publish (<c>R-134</c>).
    /// ⛔ Optional, and legitimately so: only the two AUTHORING hosts have AI editors. ⚠ But a host
    /// that HAS a cell must pass it — <c>R-67</c>, and the rail is on the constructed pack.
    /// </param>
    public SelectionNotificationSystem(
        Func<IInspectorContext?> inspector,
        Hrot.ScenarioEditor.Tools.IToolController? tools = null,
        ISelectionState? selection = null,
        Action<Entity?>? aiEntitySelection = null)
    {
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _tools     = tools;
        _selection = selection;
        _aiEntity  = aiEntitySelection;
    }

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world) return;

        // ⚠ Resolved once, but NOT an early return any more: a host without an inspector context still
        //   owes S-5's cancel. ⛔ The `if (inspector == null) return` that used to sit here would have
        //   skipped it silently on exactly those hosts.
        var inspector = _inspector();

        foreach (var note in world.Bus.ReadManaged<SelectionChangedNotification>())
        {
            if (note == null) continue;

            // ⚠ The PRIMARY, not the set: IInspectorContext is single-entity by contract, and the
            //   panel reads the full set from ISelectionState. ⛔ Inventing a "first of many" rule
            //   here would be a second opinion about what "the" selected entity is.
            if (inspector != null) inspector.SelectedEntity = note.Primary;

            // ⭐⭐⭐ CE-300 — the AI editors' entity, from the SAME announcement.
            // ⚠ THE SAME PRIMARY the inspector gets, deliberately: the AI cell is a single Entity? and
            //   "the selected entity" must mean one thing on a host, not two. ⛔ Picking a different
            //   member of the set here would be a second opinion about what "the" selection is.
            // ⚠ Entity.Null => null, because the cell's null is a REAL state its readers gate on
            //   ("I cannot project" => the row shows (pending)). ⛔ Handing them Entity.Null instead
            //   would make every provider ask the world about entity 0.
            _aiEntity?.Invoke(note.Primary == Entity.Null ? null : note.Primary);

            CancelEditsOnDeselectedEntities();
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>S-5</c> / ruling ② — an entity that lost the selection loses its edit.</b>
    ///
    /// <para>⭐ <b>Driven off the ARMED SET, not off a diff of the selection.</b> The modal stack is short
    /// by construction (🔒 <c>Q27-F</c>: nothing needs more than 2), whereas a selection can be large —
    /// and more importantly, asking <i>"is each armed entity still selected?"</i> is ruling ②'s own
    /// wording. ⛔ Diffing the notification's set would need the PREVIOUS set, i.e. a latch, which
    /// <c>R-126</c> exists to avoid.</para>
    ///
    /// <para>⚠ <b>The stack is mutated while it is read</b>, so it is snapshotted first — <c>CancelArmedOn</c>
    /// removes entries.</para>
    /// </summary>
    private void CancelEditsOnDeselectedEntities()
    {
        if (_tools == null || _selection == null) return;

        var armed = _tools.ModalStack;
        if (armed.Count == 0) return;

        Span<Entity> losing = stackalloc Entity[armed.Count];
        int n = 0;
        for (int i = 0; i < armed.Count; i++)
        {
            var target = armed[i].Target;
            // ⭐ Entity.Null is a target-less tool — exempt from ② by construction (§4.14).
            if (target.IsNull || _selection.IsSelected(target)) continue;
            losing[n++] = target;
        }

        for (int i = 0; i < n; i++) _tools.CancelArmedOn(losing[i]);
    }
}
