using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replication.Edits
{
    /// <summary>
    /// ⭐⭐ Edit framework F1 (📄 docs/UX/DESIGN_Edit_Framework.md §2.1–§2.3, R-4126…R-4128) — the ONE entry point for a user's edit, on every station:
    /// the gate (<see cref="EditGate"/>) may refuse it here, else it goes to the kind's existing route with a request id and the outcome arrives in the ONE
    /// stream (<see cref="EditOutcomeLog"/>), and the station's <see cref="EditJournal"/> records it. ⭐ The caller never knows whether the owner is local.
    /// <para>F1 builds <see cref="EditKind.Write"/> (through <see cref="EditRequests"/>), F2 <see cref="EditKind.Create"/> / <see cref="EditKind.Delete"/>; the
    /// other kinds arrive with F3–F6 — ⛔ never a second path.</para>
    /// </summary>
    public static class Edits
    {
        /// <summary>What a replaying world says to every edit.</summary>
        public const string ReplayRefusal = "a recording is read-only — branch to live to change it";

        /// <summary>Submits <paramref name="op"/>; the ticket's id reads the outcome (<see cref="EditOutcomeLog.TryGet"/> / <see cref="EditJournal.Find"/>).</summary>
        public static EditTicket Submit(EntityRepository repo, EditOp op)
        {
            ArgumentNullException.ThrowIfNull(repo);
            ArgumentNullException.ThrowIfNull(op);
            var gate = EditGate.Of(repo);
            string mode = gate is IEditGateMode m ? m.ModeText : string.Empty;
            int netId = repo.IsAlive(op.Target) ? NetworkIdResolver.RuntimeNetworkIdOf(repo, op.Target) : 0;

            // ⭐ Review mode on EVERY host by construction: a world that is replaying a recording is read-only (ReplayState — the R-3064 flag), whatever gate a host has.
            string? refusal = Fdp.Toolkit.Replay.ReplayState.IsActive(repo) ? ReplayRefusal : gate?.Refuse(repo, op);
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(repo)) mode = "Review";
            if (refusal != null)
            {
                var refusedId = Guid.NewGuid();
                var entry = EditJournal.Of(repo).Append(refusedId, op, netId, mode);
                EditOutcomes.Report(repo, refusedId, netId, EditStatus.Refused, refusal, EditOutcomes.LocalNodeOf(repo, op.Target));
                entry.Status = EditStatus.Refused; entry.Reason = refusal;
                return new EditTicket(refusedId, EntityWriteRoute.Refused);
            }

            // ⭐⭐ CE-11060 — a THREADED node's presentation world (A, a SimLink to the simulation world S): an edit that writes the world (a write, a
            //   deletion, a component added / removed) is ROUTED ON THE SIM THREAD, against S, by this same code — posted to the ONE sim job queue
            //   (D-5, the threaded work inventory's U-2). 📐 Before it, the form's write landed on A, the UI's copy, and the next staging pull of a
            //   changed chunk put S's value back (the user: "every change was reverted, if not immediately then when unpaused").
            //   The kinds that only SEND a host's event (create, interact, order, commit) stay here: their delegate publishes on A's bus, and the
            //   staging uplink already carries it to S.
            if (RoutesOnTheSimWorld(op.Kind) && Fdp.Core.Threading.SimJobs.LinkOf(repo) is { } link && !link.Jobs.OnSimThread)
                return SubmitOnTheSimWorld(repo, link, op, netId, mode);

            var ticket = Route(repo, op, netId, Guid.NewGuid());

            var e = EditJournal.Of(repo).Append(ticket.RequestId, op, netId, mode);
            if (EditOutcomeLog.Of(repo).TryGet(ticket.RequestId, out var known))   // what this node already knows (Applied here, Refused, a deferred Pending)
            {
                e.Status = known.Status; e.Reason = known.Reason ?? string.Empty;
            }
            return ticket;
        }

        /// <summary>The kinds that write the world, and so must run where the world is written (the sim thread, on a threaded node).</summary>
        private static bool RoutesOnTheSimWorld(EditKind kind)
            => kind is EditKind.Write or EditKind.Delete or EditKind.AddComponent or EditKind.RemoveComponent;

        /// <summary>
        /// ⭐ CE-11060 — the threaded form of <see cref="Submit"/>: the ticket is minted and journaled HERE (the UI's station journal, Pending); the route runs on
        /// the sim thread against the simulation world under the SAME request id, and the outcome it knows at once (Applied here, Refused, a deferred
        /// Pending) comes back to this world's <see cref="EditOutcomeLog"/> by the job's reply. A later answer (an owner elsewhere, a judge) is reported on S and
        /// reaches this log through <see cref="EditOutcomes.MirrorFromBus"/>. The caller reads <see cref="EntityWriteRoute.Requested"/>: the outcome is not known yet.
        /// </summary>
        private static EditTicket SubmitOnTheSimWorld(EntityRepository ui, Fdp.Core.Threading.SimLink link, EditOp op, int netId, string mode)
        {
            var id = Guid.NewGuid();
            var entry = EditJournal.Of(ui).Append(id, op, netId, mode);
            entry.Status = EditStatus.Pending;
            var sent = op.Changes is { } changes ? op with { Changes = Copy(changes) } : op;   // the caller may reuse its list: the job keeps its own
            link.Jobs.Post(sim =>
            {
                int simNetId = sim.IsAlive(sent.Target) ? NetworkIdResolver.RuntimeNetworkIdOf(sim, sent.Target) : 0;
                Route(sim, sent, simNetId, id);
                if (!EditOutcomeLog.Of(sim).TryGet(id, out var known)) return null;   // Requested: the owner answers later
                return presentation => EditOutcomes.Mirror(presentation, known);
            }, "edit: " + op.Label);
            return new EditTicket(id, EntityWriteRoute.Requested);
        }

        private static EntityAttributeChange[] Copy(IReadOnlyList<EntityAttributeChange> changes)
        {
            var copy = new EntityAttributeChange[changes.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = changes[i];
            return copy;
        }

        /// <summary>The kind's route on <paramref name="repo"/> under request id <paramref name="id"/>, inside one undo step (U2).</summary>
        private static EditTicket Route(EntityRepository repo, EditOp op, int netId, Guid id)
        {
            // ⭐ Edit-mode undo U2 — an accepted authoring edit is one undo step (nested inside a bracket a gizmo opened, it joins that step).
            //    Orders and interactions are not world authoring (and are refused in Edit mode anyway).
            bool step = op.Kind is not (EditKind.Command or EditKind.Interact);
            if (step) EditSteps.Begin(repo, op.Label);
            try
            {
                return op.Kind switch
                {
                    EditKind.Write when op.IsCommanderWrite => WriteCommander(repo, op, netId, id),
                    EditKind.Write when op.Changes != null => EditRequests.Write(repo, op.Target, op.Changes, id),
                    EditKind.Write when op.PatchJson != null => EditRequests.WriteJson(repo, op.Target, op.PatchJson, id),
                    EditKind.Write => throw new ArgumentException("A Write op carries Changes, a PatchJson or a commander.", nameof(op)),
                    EditKind.Create => Create(op, id),
                    EditKind.Interact => Create(op, id),
                    EditKind.Command => Create(op, id),    // ⭐ F5 (CE-4306): an order — the host's intent event sent under a fresh id; the brain's owner answers
                    EditKind.Commit => Create(op, id),     // ⭐ F6: likewise; the version precondition rides in the host's command   // ⭐ F4: the same shape — the host's command sent under a fresh id; its executor answers
                    EditKind.Delete => Delete(repo, op, netId, id),
                    EditKind.AddComponent => ComponentEdit(repo, op, netId, add: true, id),
                    EditKind.RemoveComponent => ComponentEdit(repo, op, netId, add: false, id),
                    _ => throw new NotSupportedException($"Edits.Submit: the {op.Kind} kind is not built yet (edit framework F2–F6)."),
                };
            }
            finally
            {
                if (step) EditSteps.End(repo);
            }
        }

        /// <summary>⭐ F2 — the host's creation affordance sends the request under a fresh id; the processing node's request system answers (Pending, then
        /// Applied with the new entity's id, or Refused with the reason).</summary>
        private static EditTicket Create(EditOp op, Guid id)
        {
            op.Send!(id);
            return new EditTicket(id, EntityWriteRoute.Requested);
        }

        /// <summary>
        /// ⭐ F2 (CE-4276) — a deletion with the request id end to end: a <c>DestroyEntityCommand</c> carrying it. On the owner it is torn down here
        /// (Applied at once); elsewhere the egress asks the owner and the replica stays until the owner's dispose removes it.
        /// </summary>
        private static EditTicket Delete(EntityRepository repo, EditOp op, int netId, Guid id)
        {
            int node = EditOutcomes.LocalNodeOf(repo, op.Target);
            if (!repo.IsAlive(op.Target))
            {
                EditOutcomes.Report(repo, id, netId, EditStatus.Refused, "the entity is gone", node);
                return new EditTicket(id, EntityWriteRoute.Refused);
            }
            if (netId <= 0)
            {
                EditOutcomes.Report(repo, id, netId, EditStatus.Refused, "the entity is not replicated — delete it locally", node);
                return new EditTicket(id, EntityWriteRoute.Refused);
            }
            if (!repo.Bus.IsRegisteredManaged<Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand>())
                repo.Bus.RegisterManaged<Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand>();
            repo.Bus.PublishManaged(new Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand { NetworkId = netId, RequestId = id, Reason = op.Label });
            if (Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, op.Target))
            {
                EditOutcomes.Report(repo, id, netId, EditStatus.Applied, "deleted on this node", node);
                return new EditTicket(id, EntityWriteRoute.Direct);
            }
            return new EditTicket(id, EntityWriteRoute.Requested);
        }

        /// <summary>⭐ F3 — the owner adds / removes the component here; a non-owner asks it with the <see cref="ComponentEdits.PatchKey"/> patch (the CommanderId shape).</summary>
        private static EditTicket ComponentEdit(EntityRepository repo, EditOp op, int netId, bool add, Guid id)
        {
            int node = EditOutcomes.LocalNodeOf(repo, op.Target);
            if (Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, op.Target))
            {
                string? why = ComponentEdits.Apply(repo, op.Target, op.ComponentName!, add);
                EditOutcomes.Report(repo, id, netId, why is null ? EditStatus.Applied : EditStatus.Refused, why ?? "applied on this node", node);
                return new EditTicket(id, why is null ? EntityWriteRoute.Direct : EntityWriteRoute.Refused);
            }
            if (netId <= 0 || !EntityAttributeChangeRequests.TryPublishJsonPatch(repo, op.Target, ComponentEdits.Patch(op.ComponentName!, add), id, wantOutcome: true))
            {
                EditOutcomes.Report(repo, id, netId, EditStatus.Refused, "the entity is not replicated — nobody to ask", node);
                return new EditTicket(id, EntityWriteRoute.Refused);
            }
            return new EditTicket(id, EntityWriteRoute.Requested);
        }

        /// <summary>
        /// ⭐ CE-4275 — the ORBAT re-parent through the existing <c>"CommanderId"</c> attribute. The owner of the subordinate's HIERARCHY (the same authority
        /// key <c>UpdateEntityAttributeRequestSystem</c>'s intercept checks) applies it here; any other node asks the owner. ⚠ Not <see cref="EditRequests.WriteJson"/>
        /// alone: a networkless host has no system that applies a JSON patch, so the owner-local case is applied directly, as the binary router does.
        /// </summary>
        private static EditTicket WriteCommander(EntityRepository repo, EditOp op, int netId, Guid id)
        {
            var sub = op.Target;
            int node = EditOutcomes.LocalNodeOf(repo, sub);
            if (!repo.IsAlive(sub))
            {
                EditOutcomes.Report(repo, id, netId, EditStatus.Refused, "the entity is gone", node);
                return new EditTicket(id, EntityWriteRoute.Refused);
            }

            if (OwnsHierarchy(repo, sub))
            {
                if (!op.Commander.IsNull)
                {
                    if (!repo.IsAlive(op.Commander))
                    {
                        EditOutcomes.Report(repo, id, netId, EditStatus.Refused, "the commander is gone", node);
                        return new EditTicket(id, EntityWriteRoute.Refused);
                    }
                    repo.Bus.Publish(new CmdAssignSubordinate { Subordinate = sub, Commander = op.Commander, Designation = TacticalDesignation.Undefined });
                }
                else if (repo.IsComponentTypeRegistered<UnitSubordinate>() && repo.HasComponent<UnitSubordinate>(sub))
                {
                    repo.Bus.Publish(new CmdRemoveSubordinate { Subordinate = sub });
                }
                EditOutcomes.Report(repo, id, netId, EditStatus.Applied, "applied on this node", node);
                return new EditTicket(id, EntityWriteRoute.Direct);
            }

            int commanderNetId = op.Commander.IsNull ? 0 : NetworkIdResolver.RuntimeNetworkIdOf(repo, op.Commander);
            if (netId <= 0 || (!op.Commander.IsNull && commanderNetId <= 0))
            {
                EditOutcomes.Report(repo, id, netId, EditStatus.Refused, "the entity is not replicated — nobody to ask", node);
                return new EditTicket(id, EntityWriteRoute.Refused);
            }
            bool sent = EntityAttributeChangeRequests.TryPublishJsonPatch(repo, sub,
                "{\"CommanderId\":" + commanderNetId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", id, wantOutcome: true);
            if (!sent)
            {
                EditOutcomes.Report(repo, id, netId, EditStatus.Refused, "nothing was sent — this node has no way to ask the owner", node);
                return new EditTicket(id, EntityWriteRoute.Refused);
            }
            return new EditTicket(id, EntityWriteRoute.Requested);
        }

        /// <summary>Does this node own <paramref name="e"/>'s hierarchy? An entity with no authority component (a networkless world) is owned here.</summary>
        private static bool OwnsHierarchy(EntityRepository repo, Entity e)
        {
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkAuthority>()
                || !repo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkAuthority>(e)) return true;
            long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(1L, 0);
            return Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, e, packedKey);
        }
    }

    /// <summary>A gate that also names the station's editing mode (the journal records it).</summary>
    public interface IEditGateMode
    {
        string ModeText { get; }
    }
}
