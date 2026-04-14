using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Services;
using Fdp.ModuleHost.Core.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator that tracks dirty <see cref="Health"/> components and publishes
    /// <see cref="EntityDamage"/> DDS messages so the IG and ExCon can update health bars.
    ///
    /// <para>
    /// Change detection is performed by comparing <see cref="Health.Current"/> against a
    /// per-entity cache keyed by network ID.  A DDS sample is only written when the value
    /// has changed from the last published value, preventing unnecessary traffic on every tick.
    /// </para>
    ///
    /// <para>
    /// The <see cref="EntityDamage.Damage"/> wire field is a 0–100 damage level where
    /// 0 = fully healthy and 100 = fully destroyed/dead, derived from
    /// <c>(1 − Current / Max) × 100</c>.
    /// </para>
    /// </summary>
    public class EntityDamageEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityDamage";
        private const long OrdinalValue   = 30;

        private readonly DdsWriter<EntityDamage> _writer;
        private readonly NetworkEntityMap        _entityMap;

        /// <summary>
        /// Cache of last-published <see cref="Health.Current"/> per network entity ID.
        ///
        /// <para>
        /// <b>TD-9 — Memory leak risk:</b> entries are only removed in <see cref="Dispose(long)"/>,
        /// which is called by <c>CycloneNetworkCleanupSystem</c> during network entity teardown.
        /// In topologies where <c>CycloneNetworkCleanupSystem</c> is disabled or fails to run
        /// (e.g., abnormal process exit or certain test configurations), stale health entries for
        /// destroyed entities will accumulate in this dictionary for the lifetime of the process.
        /// A dedicated lifecycle cleanup pass should be added in a future debt-burndown batch.
        /// </para>
        /// </summary>
        private readonly Dictionary<long, float> _lastPublished = new();

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => OrdinalValue;

        public EntityDamageEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
        {
            _writer    = new DdsWriter<EntityDamage>(participant, DdsTopicName);
            _entityMap = entityMap;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Scans all authority-owned entities that have both a <see cref="Health"/> and a
        /// <see cref="NetworkIdentity"/> component, and publishes an <see cref="EntityDamage"/>
        /// DDS sample whenever <see cref="Health.Current"/> has changed since the last publish.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<Health>()
                .With<NetworkIdentity>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            long packedKey = Fdp.ModuleHost.Core.Network.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                // Only publish for locally-owned (authority) entities.
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                ref readonly var netId  = ref view.GetComponentRO<NetworkIdentity>(entity);
                ref readonly var health = ref view.GetComponentRO<Health>(entity);

                float current = health.Current;

                // Only send when health has actually changed.
                if (_lastPublished.TryGetValue(netId.Value, out float prev) && prev == current)
                    continue;

                _lastPublished[netId.Value] = current;

                float max    = health.Max > 0f ? health.Max : 1f;
                float damage = (1f - current / max) * 100f;
                if (damage < 0f)   damage = 0f;
                if (damage > 100f) damage = 100f;

                _writer.Write(new EntityDamage
                {
                    EntityId = (int)netId.Value,
                    Damage   = damage,
                });
            }
        }

        /// <summary>Removes the entity from the published-value cache.</summary>
        public void Dispose(long networkEntityId)
        {
            // TD-9: Trace disposal events so we can detect if cleanup is being skipped.
            // If this log never fires for a destroyed entity, the cache entry is leaking.
            FdpLog<EntityDamageEgressTranslator>.Warn(
                "[TD-9] EntityDamageEgressTranslator.Dispose called for networkEntityId={0}",
                networkEntityId);
            _lastPublished.Remove(networkEntityId);
        }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}
