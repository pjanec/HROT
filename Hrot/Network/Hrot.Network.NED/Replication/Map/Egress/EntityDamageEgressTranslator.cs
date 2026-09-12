using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator that tracks dirty <see cref="Health"/> components and publishes
    /// <see cref="EntityDamage"/> DDS messages so the IG and ExCon can show entity health.
    ///
    /// <para>
    /// Change detection compares BOTH <see cref="Health.Current"/> and <see cref="Health.Max"/>
    /// against a per-entity cache keyed by network ID. A DDS sample is only written when either
    /// has changed, preventing unnecessary traffic on every tick.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐⭐ <b>CE-196 — the sample carries Current and Max, NOT a precomputed percentage.</b>
    /// It used to publish a 0–100 damage level derived from <c>(1 − Current / Max) × 100</c>. That was
    /// computed against the SENDER's <c>Max</c>, while every receiver kept its own TKB-seeded one — so
    /// the nodes disagreed about the same entity (measured: 50/50 on the Brain, 3000/3000 on IG).
    /// Shipping the pair makes the receiver's <see cref="Health"/> identical to the authority's, and
    /// each consumer derives whatever fraction it needs to render.
    /// 🔒 User ruling, 2026-09-05: "no precalculated percentages".
    /// </para>
    /// </summary>
    public class EntityDamageEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityDamage";
        private const long OrdinalValue   = (long)EDescriptorType.dtEntityDamage;

        private readonly DdsWriter<EntityDamage> _writer;
        private readonly NetworkEntityMap        _entityMap;

        /// <summary>
        /// Cache of the last-published (<see cref="Health.Current"/>, <see cref="Health.Max"/>) pair
        /// per network entity ID.
        ///
        /// <para>
        /// <b>TD-9 â€” Memory leak risk:</b> entries are only removed in <see cref="Dispose(long)"/>,
        /// which is called by <c>CycloneNetworkCleanupSystem</c> during network entity teardown.
        /// In topologies where <c>CycloneNetworkCleanupSystem</c> is disabled or fails to run
        /// (e.g., abnormal process exit or certain test configurations), stale health entries for
        /// destroyed entities will accumulate in this dictionary for the lifetime of the process.
        /// A dedicated lifecycle cleanup pass should be added in a future debt-burndown batch.
        /// </para>
        /// </summary>
        private readonly Dictionary<long, (float Current, float Max)> _lastPublished = new();

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

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
        /// DDS sample whenever <see cref="Health.Current"/> or <see cref="Health.Max"/> has changed
        /// since the last publish.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<Health>()
                .With<NetworkIdentity>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                // Only publish for locally-owned (authority) entities.
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                ref readonly var netId  = ref view.GetComponentRO<NetworkIdentity>(entity);
                ref readonly var health = ref view.GetComponentRO<Health>(entity);

                float current = health.Current;
                float max     = health.Max;

                // Only send when health has actually changed.
                // ⚠ The comparison covers BOTH fields: Max is authored data (a scenario may set it, and
                //   an editor write may change it), so a Max-only change must still reach the receivers.
                //   Comparing Current alone would silently pin them to a stale maximum.
                if (_lastPublished.TryGetValue(netId.Value, out (float Current, float Max) prev)
                    && prev.Current == current && prev.Max == max)
                    continue;

                _lastPublished[netId.Value] = (current, max);

                // ⛔ NO PERCENTAGE IS COMPUTED HERE. The pair travels and every consumer derives what it
                //    needs — one representation of health on the wire, as on the components.
                //    📄 See the descriptor's own remarks in SimDescriptors.cs.
                _writer.Write(new EntityDamage
                {
                    EntityId = (int)netId.Value,
                    Current  = current,
                    Max      = max,
                });

                SentSampleCount++;
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
