using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Replication.Edits
{
    /// <summary>
    /// ⭐ CE-2380 part 1 (📄 docs/DESIGN_Remote_Edit_Outcome.md §2) — reports the outcome of a remote edit on THIS node: adds it to the local <see cref="EditOutcomeLog"/> and
    /// publishes the <see cref="EditOutcome"/> event (<c>FromNetwork</c> false), which the wire egress sends to the other nodes. The owner's request system, a deferred judge and
    /// <see cref="EditRequests"/> (a local edit) are the reporters.
    /// </summary>
    public static class EditOutcomes
    {
        /// <summary>
        /// Reports <paramref name="status"/> for <paramref name="requestId"/>. A no-op when <paramref name="requestId"/> is empty (a caller that never asked).
        /// <paramref name="respondingNode"/> defaults to this node's id (<see cref="INetworkTopology"/>; 0 when the world has none).
        /// </summary>
        public static EditOutcome? Report(EntityRepository repo, Guid requestId, int networkId, EditStatus status, string? reason, int? respondingNode = null)
        {
            ArgumentNullException.ThrowIfNull(repo);
            if (requestId == Guid.Empty) return null;

            int node = respondingNode is >= 0 ? respondingNode.Value : (repo.HasSingletonManaged<INetworkTopology>() ? (int)(repo.GetSingletonManaged<INetworkTopology>()?.LocalNodeId ?? default) : 0);
            double at = repo.HasSingletonUnmanaged<GlobalTime>() ? repo.GetSingletonUnmanagedRO<GlobalTime>().TotalTime : 0d;
            var outcome = EditOutcome.Create(requestId, networkId, status, reason, node, at, fromNetwork: false);

            EditOutcomeLog.Of(repo).Add(outcome);
            Publish(repo, outcome);
            return outcome;
        }

        /// <summary>
        /// This node's id as <paramref name="entity"/> knows it (<c>NetworkAuthority.LocalNodeId</c>), else the world's <see cref="INetworkTopology"/>, else 0. 📐 The world carries no
        /// node-id singleton of its own in production (the topology is handed to the ownership systems), and a replicated entity always has the component.
        /// </summary>
        public static int LocalNodeOf(EntityRepository repo, Entity entity)
            => repo.IsAlive(entity) && repo.HasComponent<NetworkAuthority>(entity) ? (int)repo.GetComponentRO<NetworkAuthority>(entity).LocalNodeId : -1;

        /// <summary>
        /// ⭐ CE-11060 — takes an outcome reported on ANOTHER world of this node into <paramref name="presentation"/>'s log: a threaded node's UI world (A)
        /// learns what its simulation world (S) decided. Only the log — ⛔ never re-published (A's bus goes UP to S). A no-op when the log already holds it.
        /// </summary>
        public static void Mirror(EntityRepository presentation, EditOutcome outcome)
        {
            ArgumentNullException.ThrowIfNull(presentation);
            ArgumentNullException.ThrowIfNull(outcome);
            var log = EditOutcomeLog.Of(presentation);
            if (!log.Holds(outcome.RequestId, outcome.Status, (int)outcome.RespondingNode)) log.Add(outcome);
        }

        /// <summary>
        /// ⭐ CE-11060 — the outcomes the staging carried from the simulation world's bus onto <paramref name="presentation"/>'s (a later answer: an owner on
        /// another node, a deferred judge) into its log. Called once per pull by the threaded node (<c>StagedPresentation.Pull</c>). 0 B when none.
        /// </summary>
        public static void MirrorFromBus(EntityRepository presentation)
        {
            ArgumentNullException.ThrowIfNull(presentation);
            if (!presentation.Bus.IsRegisteredManaged<EditOutcome>()) return;
            var events = presentation.ReadManagedEvents<EditOutcome>();   // ⛔ no foreach over the interface: this runs every pull (0 B)
            for (int i = 0; i < events.Count; i++) Mirror(presentation, events[i]);
        }

        /// <summary>Publishes <paramref name="outcome"/> on the world's bus (registering the event type when a bare world has not).</summary>
        public static void Publish(EntityRepository repo, EditOutcome outcome)
        {
            if (!repo.Bus.IsRegisteredManaged<EditOutcome>()) repo.Bus.RegisterManaged<EditOutcome>();
            repo.Bus.PublishManaged(outcome);
        }
    }
}
