using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replication.Edits
{
    /// <summary>What <see cref="EditRequests"/> hands back: the id to read the outcome by, and how the write was routed.</summary>
    public readonly record struct EditTicket(Guid RequestId, EntityWriteRoute Route);

    /// <summary>
    /// ⭐ CE-2380 part 1 (📄 docs/DESIGN_Remote_Edit_Outcome.md §2, R-2082) — the UI's entry for a remote edit that wants an ANSWER. A thin wrapper over the EXISTING write path
    /// (<see cref="AttributeEntityComponentWriter"/> / <see cref="EntityAttributeChangeRequests"/>): it mints the request id, routes, and reports at once what this node can know —
    /// Direct ⇒ <see cref="EditStatus.Applied"/> (lean F: one API whether or not this node owns the entity), Refused ⇒ <see cref="EditStatus.Refused"/> with the reason;
    /// Requested ⇒ the command carries the id and <c>WantOutcome</c>, and the OWNER's answer arrives later (<see cref="EditOutcomeLog.TryGet"/>).
    /// </summary>
    public static class EditRequests
    {
        /// <summary>A binary edit (the write router's changes) with an outcome.</summary>
        public static EditTicket Write(EntityRepository repo, Entity entity, IReadOnlyList<EntityAttributeChange> changes)
        {
            ArgumentNullException.ThrowIfNull(repo);
            ArgumentNullException.ThrowIfNull(changes);
            var id = Guid.NewGuid();
            var writer = new AttributeEntityComponentWriter(repo, AttributeInterpreterProvider.GetOrCreate(repo), EntityAttributeChangeRequests.PublishOnto(repo, id, wantOutcome: true)) { RequestId = id };
            var route = writer.Write(entity, changes);
            int netId = repo.IsAlive(entity) ? NetworkIdResolver.RuntimeNetworkIdOf(repo, entity) : 0;
            // an unreplicated entity: the router "requested" but PublishOnto had nobody to address (it only warns) ⇒ say so
            if (route == EntityWriteRoute.Requested && netId <= 0) route = EntityWriteRoute.Refused;
            ReportRoute(repo, id, netId, route, writer.LastWriteDeferred, EditOutcomes.LocalNodeOf(repo, entity), changes.Count == 0 ? "no change was given" : repo.IsAlive(entity) ? null : "the entity is gone");
            return new EditTicket(id, route);
        }

        /// <summary>A JSON edit (<paramref name="patchJson"/>, the debug API's attribute patch) with an outcome. There is no local apply: the owner — this node included — gets the request.</summary>
        public static EditTicket WriteJson(EntityRepository repo, Entity entity, string patchJson)
        {
            ArgumentNullException.ThrowIfNull(repo);
            var id = Guid.NewGuid();
            int netId = repo.IsAlive(entity) ? NetworkIdResolver.RuntimeNetworkIdOf(repo, entity) : 0;
            bool sent = EntityAttributeChangeRequests.TryPublishJsonPatch(repo, entity, patchJson, id, wantOutcome: true);
            var route = sent ? EntityWriteRoute.Requested : EntityWriteRoute.Refused;
            ReportRoute(repo, id, netId, route, false, EditOutcomes.LocalNodeOf(repo, entity), null);
            return new EditTicket(id, route);
        }

        private static void ReportRoute(EntityRepository repo, Guid id, int netId, EntityWriteRoute route, bool deferred, int node, string? why)
        {
            switch (route)
            {
                case EntityWriteRoute.Direct:
                    if (deferred) EditOutcomes.Report(repo, id, netId, EditStatus.Pending, "taken by this node; its verdict follows", node);   // a rule's live edit / control: the judge answers later (lean C)
                    else EditOutcomes.Report(repo, id, netId, EditStatus.Applied, "applied on this node", node);
                    break;
                case EntityWriteRoute.Refused:
                    EditOutcomes.Report(repo, id, netId, EditStatus.Refused,
                        why ?? (netId <= 0 ? "the entity is not replicated — nobody to ask" : "nothing was sent — this node has no way to ask the owner"), node);
                    break;
            }   // Requested: the owner answers
        }
    }
}
