using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Patching;

namespace Fdp.Toolkit.Replication.Edits
{
    /// <summary>⭐ Edit framework (📄 docs/UX/DESIGN_Edit_Framework.md §2.1, §3) — the kinds of user edit, each answered through the ONE outcome stream.</summary>
    public enum EditKind : byte
    {
        /// <summary>An attribute-shaped write (name, side, clock, the ORBAT commander …) — F1.</summary>
        Write = 0,
        Create = 1,
        Delete = 2,
        AddComponent = 3,
        RemoveComponent = 4,
        Interact = 5,
        Command = 6,
        Commit = 7,
        /// <summary>⭐ CE-11061 (Injury States §9) — put a unit back in the fight (<c>CombatLife.Revive</c>) on the node that applies its Health.</summary>
        Revive = 8,
    }

    /// <summary>
    /// ⭐ Edit framework §2.1 — ONE user edit: what kind, on what, with what payload, and the words the user would use for it (<see cref="Label"/>: the journal,
    /// the notification). Immutable. Build it with the factories (<see cref="Write"/>, <see cref="WritePatch"/>, <see cref="SetCommander"/>).
    /// </summary>
    public sealed record EditOp
    {
        public required EditKind Kind { get; init; }
        /// <summary>The entity the edit is on.</summary>
        public Entity Target { get; init; }
        /// <summary>A binary attribute write (the write router's changes) — <see cref="EditKind.Write"/>.</summary>
        public IReadOnlyList<EntityAttributeChange>? Changes { get; init; }
        /// <summary>A JSON attribute patch (the owner applies it; ⚠ a networkless host has no system that does — prefer <see cref="Changes"/>).</summary>
        public string? PatchJson { get; init; }
        /// <summary>The ORBAT commander for <see cref="SetCommander"/> (<see cref="Entity.Null"/> = detach).</summary>
        public Entity Commander { get; init; }
        /// <summary>What the user did, in their words ("Rename Alpha 1-1").</summary>
        public string Label { get; init; } = string.Empty;

        /// <summary><see cref="EditKind.Create"/>: the host's creation affordance, called once with the request id <see cref="Edits.Submit"/> minted
        /// (Hrot: <c>EntityCreation.RequestEntityCreation(…, requestId: id)</c>). ⭐ The toolkit knows no creation request; the host's affordance stays the ONE author path (R-145).</summary>
        public Action<Guid>? Send { get; init; }

        /// <summary>F3: the component to add / remove (a vocabulary component's name, <see cref="ComponentEdits"/>).</summary>
        public string? ComponentName { get; init; }

        /// <summary>True for <see cref="SetCommander"/>'s op.</summary>
        public bool IsCommanderWrite { get; init; }

        public static EditOp Write(Entity target, IReadOnlyList<EntityAttributeChange> changes, string label)
            => new() { Kind = EditKind.Write, Target = target, Changes = changes ?? throw new ArgumentNullException(nameof(changes)), Label = label ?? string.Empty };

        public static EditOp Write(Entity target, EntityAttributeChange change, string label) => Write(target, new[] { change }, label);

        /// <summary>⭐ F2 — a creation: <paramref name="send"/> publishes the request under the id it is given; the processing node answers with <c>EntityEditOutcome</c>.</summary>
        public static EditOp Create(Action<Guid> send, string label)
            => new() { Kind = EditKind.Create, Send = send ?? throw new ArgumentNullException(nameof(send)), Label = label ?? string.Empty };

        /// <summary>⭐ F4 — an interaction (embark, disembark, possess, release): <paramref name="send"/> publishes the interaction's command carrying the id it
        /// is given; the node that applies it answers (accepted, or refused + why — <see cref="InteractionOutcomes"/>).</summary>
        public static EditOp Interact(Entity target, Action<Guid> send, string label)
            => new() { Kind = EditKind.Interact, Target = target, Send = send ?? throw new ArgumentNullException(nameof(send)), Label = label ?? string.Empty };

        /// <summary>⭐ F5 (CE-4306) — an order to a unit: <paramref name="send"/> publishes the order's intent carrying the id it is given; the brain's owner answers
        /// ACCEPTED (<see cref="EditStatus.Applied"/>) or refused + why (<see cref="InteractionOutcomes"/>). Execution is not tracked.</summary>
        public static EditOp Command(Entity target, Action<Guid> send, string label)
            => new() { Kind = EditKind.Command, Target = target, Send = send ?? throw new ArgumentNullException(nameof(send)), Label = label ?? string.Empty };

        /// <summary>⭐ F6 (CE-4277) — a document commit with a version precondition (the mission plan): <paramref name="send"/> publishes it under the id;
        /// its executor answers Applied, or <see cref="EditStatus.Conflict"/> when the base version moved.</summary>
        public static EditOp Commit(Entity target, Action<Guid> send, string label)
            => new() { Kind = EditKind.Commit, Target = target, Send = send ?? throw new ArgumentNullException(nameof(send)), Label = label ?? string.Empty };

        /// <summary>⭐ F3 — add a form component (the Playable toggle) on its owner, with its default value.</summary>
        public static EditOp AddComponent(Entity target, Type component, string label)
            => new() { Kind = EditKind.AddComponent, Target = target, ComponentName = (component ?? throw new ArgumentNullException(nameof(component))).Name, Label = label ?? string.Empty };

        /// <summary>⭐ F3 — remove a form component on its owner.</summary>
        public static EditOp RemoveComponent(Entity target, Type component, string label)
            => new() { Kind = EditKind.RemoveComponent, Target = target, ComponentName = (component ?? throw new ArgumentNullException(nameof(component))).Name, Label = label ?? string.Empty };

        /// <summary>⭐ F2 (CE-4276) — a deletion, carrying its request id end to end; the owner tears it down and answers.</summary>
        public static EditOp Delete(Entity target, string label)
            => new() { Kind = EditKind.Delete, Target = target, Label = label ?? string.Empty };

        /// <summary>⭐ CE-11061 (📄 docs/DESIGN_Injury_States.md §9, RV-4) — REVIVE: the node that applies the unit's Health runs <c>CombatLife.Revive</c>; any other
        /// node asks it with the <see cref="ReviveEdits.PatchKey"/> patch (the F3 shape) and the answer comes back under the request id.</summary>
        public static EditOp Revive(Entity target, string label = "Revive")
            => new() { Kind = EditKind.Revive, Target = target, Label = label ?? string.Empty };

        public static EditOp WritePatch(Entity target, string patchJson, string label)
            => new() { Kind = EditKind.Write, Target = target, PatchJson = patchJson ?? throw new ArgumentNullException(nameof(patchJson)), Label = label ?? string.Empty };

        /// <summary>⭐ CE-4275 — the ORBAT re-parent as the existing <c>"CommanderId"</c> attribute (0 = detach): applied here when this node owns the subordinate's
        /// hierarchy, else asked of the owner.</summary>
        public static EditOp SetCommander(Entity subordinate, Entity commander, string label)
            => new() { Kind = EditKind.Write, Target = subordinate, Commander = commander, IsCommanderWrite = true, Label = label ?? string.Empty };
    }

    /// <summary>
    /// ⭐ Edit framework §2.1 — what may refuse an edit BEFORE it is sent: the station's editing mode and level. A host installs one per world
    /// (<see cref="EditGate.Install"/>); ⛔ the toolkit knows no modes or seats — the host's gate does (Hrot: <c>HrotEditGate</c>).
    /// </summary>
    public interface IEditGate
    {
        /// <summary>The whole reason the edit is refused ("this station is an observer"), or null when it may go.</summary>
        string? Refuse(EntityRepository repo, EditOp op);
    }

    /// <summary>The gate a world has (none installed ⇒ every edit may go).</summary>
    public static class EditGate
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, IEditGate> PerWorld = new();

        /// <summary>Installs <paramref name="gate"/> for <paramref name="repo"/> (replacing any earlier one).</summary>
        public static void Install(EntityRepository repo, IEditGate gate)
        {
            ArgumentNullException.ThrowIfNull(repo);
            ArgumentNullException.ThrowIfNull(gate);
            PerWorld.AddOrUpdate(repo, gate);
        }

        /// <summary>The gate of <paramref name="repo"/>, or null.</summary>
        public static IEditGate? Of(EntityRepository repo) => PerWorld.TryGetValue(repo, out var g) ? g : null;
    }

    /// <summary>
    /// ⭐ Edit-mode undo U2 (📄 docs/UX/DESIGN_Edit_Mode_Undo.md §2.2, R-4133) — what brackets ONE user action as one undo step. A host installs one per
    /// world (<see cref="EditSteps.Install"/>); ⛔ the toolkit knows no history — the host's recorder does (Hrot: <c>WorldUndoRecorder</c>).
    /// <para>Steps NEST: only the outermost <see cref="Begin"/> / <see cref="End"/> pair is the step (a drag that commits through
    /// <see cref="Edits.Submit"/> is ONE step).</para>
    /// </summary>
    public interface IEditStepRecorder
    {
        /// <summary>A user action begins, BEFORE anything is written (the world as it is now is the step's "before").</summary>
        void Begin(EntityRepository repo, string label);

        /// <summary>The action is done: the step is recorded.</summary>
        void End(EntityRepository repo);

        /// <summary>The action was abandoned (a cancelled drag): nothing is recorded.</summary>
        void Cancel(EntityRepository repo);
    }

    /// <summary>The step recorder a world has (none installed ⇒ every bracket is a no-op: no host but the editor records steps).</summary>
    public static class EditSteps
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, IEditStepRecorder> PerWorld = new();

        /// <summary>Installs <paramref name="recorder"/> for <paramref name="repo"/> (replacing any earlier one).</summary>
        public static void Install(EntityRepository repo, IEditStepRecorder recorder)
        {
            ArgumentNullException.ThrowIfNull(repo);
            ArgumentNullException.ThrowIfNull(recorder);
            PerWorld.AddOrUpdate(repo, recorder);
        }

        /// <summary>The recorder of <paramref name="repo"/>, or null.</summary>
        public static IEditStepRecorder? Of(EntityRepository repo) => PerWorld.TryGetValue(repo, out var r) ? r : null;

        // ⭐⭐ CE-4342 (reopened 2026-11-15) — A THREADED NODE'S PRESENTATION WORLD BRACKETS ON THE SIMULATION WORLD. In mode 3 the recorder is
        //   installed on S (the sim world, where every write lands — EditorSubsystem.WireWorldUndo, N-7) while the map gizmos run on the UI thread
        //   over A and bracket THERE; A's writes reach S as sim jobs (EntityWriteRouter's SimThreadWriter, N-6). 🔴 So every A-side bracket was a
        //   no-op: a drag (one unit or a group), a rotate, a route / vertex edit recorded NO step and Undo said "nothing to undo" (measured live).
        //   ⭐ The same retargeting the writer does: a world with no recorder but a SimLink posts the bracket to S's queue, so it lands in order
        //   with the writes posted between Begin and End — ONE step, by construction, for every caller. No link (one world): unchanged.

        /// <summary><see cref="IEditStepRecorder.Begin"/> on <paramref name="repo"/>'s recorder, if any — ⭐ CE-4342: on a threaded presentation world, on the simulation world's, in order with the writes.</summary>
        public static void Begin(EntityRepository? repo, string label)
        {
            if (repo == null) return;
            if (Of(repo) is { } r) r.Begin(repo, label);
            else if (Fdp.Core.Threading.SimJobs.LinkOf(repo) is { } link) link.Jobs.Post(sim => Begin(sim, label), "edit step: begin");
        }

        /// <summary><see cref="IEditStepRecorder.End"/> on <paramref name="repo"/>'s recorder, if any (⭐ CE-4342: forwarded as <see cref="Begin"/>).</summary>
        public static void End(EntityRepository? repo)
        {
            if (repo == null) return;
            if (Of(repo) is { } r) r.End(repo);
            else if (Fdp.Core.Threading.SimJobs.LinkOf(repo) is { } link) link.Jobs.Post(sim => End(sim), "edit step: end");
        }

        /// <summary><see cref="IEditStepRecorder.Cancel"/> on <paramref name="repo"/>'s recorder, if any (⭐ CE-4342: forwarded as <see cref="Begin"/>).</summary>
        public static void Cancel(EntityRepository? repo)
        {
            if (repo == null) return;
            if (Of(repo) is { } r) r.Cancel(repo);
            else if (Fdp.Core.Threading.SimJobs.LinkOf(repo) is { } link) link.Jobs.Post(sim => Cancel(sim), "edit step: cancel");
        }
    }
}
