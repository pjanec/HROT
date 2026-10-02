using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Messages;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replication.Systems
{
    /// <summary>
    /// ⭐⭐ <b>R-167 — what a departed node owned returns to each entity's primary owner, by a DIRECT call.</b>
    /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.3, §5.6 S7; <c>Architect_Question_79</c> §0.11.
    ///
    /// <para>On a <see cref="NodeDeparted"/>, on EVERY node, for every entity whose record names the departed node:
    /// each such key (descriptor or part instance) is applied to the entity's primary owner through the shared
    /// <see cref="OwnershipApplier"/> — the record moves, and the primary owner (only) gains the claim. No message is
    /// sent: every node sees the same departure and computes the same result (R-167, user: <i>"calling the handler
    /// internally, not really sending a network message to itself"</i>).</para>
    ///
    /// <para>⭐ <b>Keyed on the node LEAVING, not on a disposed descriptor sample</b> (the §0.11 wording). The creator
    /// writes a granted descriptor until the grantee confirms (F7), so its writer has the instance registered too; DDS
    /// reports an instance not-alive only when NO writer is left, so a grantee's crash never shows on that descriptor.
    /// A node's heartbeat instance has exactly one writer — it goes not-alive on a crash (lease expiry) and on a clean
    /// exit alike. ⭐ This also IS the P10 guard: a former owner's exit cannot take back what it already handed on,
    /// because only keys whose record STILL names the departed node move.</para>
    ///
    /// <list type="bullet">
    ///   <item>An entity whose PRIMARY owner departed is left alone — the master's departure deletes the entity (wire
    ///     spec; <c>EntityMasterIngressTranslator</c>).</item>
    ///   <item>⭐ A grant whose target left before taking over (<see cref="OutgoingGrantsPending"/>) is taken back by
    ///     the creator: it had yielded the claim, so nobody would ever publish that descriptor again (Q79 P2).</item>
    /// </list>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    [UpdateAfter(typeof(OwnershipIngressSystem))]
    public sealed class PartialOwnerReclaimSystem : IEcsModuleSystem
    {
        private readonly int              _localNodeId;
        private readonly OwnershipApplier _applier;

        private readonly List<(Entity Entity, long Key, int NewOwner)> _moves = new();
        private readonly List<Entity> _emptied = new();
        private EntityQuery? _recorded;
        private EntityQuery? _pending;

        public PartialOwnerReclaimSystem(int localNodeId, DescriptorOwnershipMap descriptorMap)
        {
            if (descriptorMap == null) throw new ArgumentNullException(nameof(descriptorMap));
            _localNodeId = localNodeId;
            _applier     = new OwnershipApplier(localNodeId, descriptorMap);
        }

        /// <summary>Diagnostics: keys moved back to a primary owner (including grants taken back).</summary>
        public int KeysReclaimed { get; private set; }

        public void Execute(ISimulationView view, float dt)
        {
            if (view is not EntityRepository repo) return;

            var departures = view.ReadEvents<NodeDeparted>();
            if (departures.IsEmpty) return;

            _recorded ??= repo.Query().WithManaged<DescriptorOwnership>().Build();
            _pending  ??= repo.Query().WithManaged<OutgoingGrantsPending>().Build();

            foreach (var departure in departures)
            {
                int gone = departure.NodeId;
                if (gone == _localNodeId) continue;

                // Collect first: applying may add a record to an entity mid-iteration.
                _moves.Clear();
                _emptied.Clear();

                foreach (var entity in _recorded)
                {
                    if (!repo.HasComponent<NetworkAuthority>(entity)) continue;
                    int primary = repo.GetComponentRO<NetworkAuthority>(entity).PrimaryOwnerId;
                    if (primary == gone) continue;            // the master left: the entity is deleted, not reclaimed

                    foreach (var kv in repo.GetComponent<DescriptorOwnership>(entity).Map)
                        if (kv.Value == gone) _moves.Add((entity, kv.Key, primary));
                }

                foreach (var entity in _pending)
                {
                    var pending = repo.GetComponent<OutgoingGrantsPending>(entity);
                    List<long>? lost = null;
                    foreach (var kv in pending.Descriptors)
                        if (kv.Value == gone) (lost ??= new List<long>()).Add(kv.Key);
                    if (lost == null) continue;

                    foreach (long descriptor in lost)
                    {
                        pending.Descriptors.Remove(descriptor);
                        _moves.Add((entity, OwnershipExtensions.PackKey(descriptor, 0), _localNodeId));
                    }
                    if (pending.Descriptors.Count == 0) _emptied.Add(entity);
                }

                foreach (var (entity, key, newOwner) in _moves)
                {
                    _applier.Apply(repo, entity, key, newOwner);
                    KeysReclaimed++;
                }
                foreach (var entity in _emptied)
                    repo.RemoveManagedComponent<OutgoingGrantsPending>(entity);

                Fdp.Core.Logging.FdpLog<PartialOwnerReclaimSystem>.Info(
                    "[Node-{0}] node {1} left: {2} ownership key(s) returned to the primary owner.", _localNodeId, gone, _moves.Count);
            }
        }
    }
}
