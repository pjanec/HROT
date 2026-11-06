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

        /// <summary>True for <see cref="SetCommander"/>'s op.</summary>
        public bool IsCommanderWrite { get; init; }

        public static EditOp Write(Entity target, IReadOnlyList<EntityAttributeChange> changes, string label)
            => new() { Kind = EditKind.Write, Target = target, Changes = changes ?? throw new ArgumentNullException(nameof(changes)), Label = label ?? string.Empty };

        public static EditOp Write(Entity target, EntityAttributeChange change, string label) => Write(target, new[] { change }, label);

        /// <summary>⭐ F2 — a creation: <paramref name="send"/> publishes the request under the id it is given; the processing node answers with <c>EntityEditOutcome</c>.</summary>
        public static EditOp Create(Action<Guid> send, string label)
            => new() { Kind = EditKind.Create, Send = send ?? throw new ArgumentNullException(nameof(send)), Label = label ?? string.Empty };

        /// <summary>⭐ F2 (CE-4276) — a deletion, carrying its request id end to end; the owner tears it down and answers.</summary>
        public static EditOp Delete(Entity target, string label)
            => new() { Kind = EditKind.Delete, Target = target, Label = label ?? string.Empty };

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
}
