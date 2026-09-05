using System;
using Hrot.NED.Descriptors;
using Fdp.Toolkit.Combat.Components;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Translators;
using Fdp.Interfaces;

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// Ingress translator for the Hrot <c>EntityDamage</c> DDS topic.
    ///
    /// <para>Writes the authoritative node's <see cref="Health"/> onto this node's copy of the entity.
    /// This translator is ingress-only; <see cref="ScanAndPublish"/> is a no-op.</para>
    ///
    /// <para>⭐⭐⭐ <b>It writes <see cref="Health"/>, not a presentation cache.</b> This used to decode
    /// into an IG-only <c>IgHealthState</c> holding a precomputed damage percentage; that was a second
    /// representation of one concept, and because the percentage was computed against the SENDER's
    /// <c>Max</c> while the receiver kept its own TKB-seeded <c>Max</c>, the two nodes disagreed about
    /// the same entity. Carrying <c>Current</c>+<c>Max</c> and writing the real component makes the
    /// receiver's health identical to the authority's by construction.</para>
    ///
    /// <para>⚠ <b>This deliberately OVERWRITES the TKB seed.</b> <c>CombatTkbTranslator</c> seeds
    /// <see cref="Health"/> from the platform definition when the component is absent, so a ghost starts
    /// with the TKB default; the first sample — delivered immediately on join, because the topic is
    /// <c>TransientLocal</c> with <c>KeepLast(1)</c> keyed by entity — replaces it with the scenario's
    /// authored value. The authority always wins.</para>
    /// </summary>
    public class EntityDamageIngressTranslator : CycloneTranslator<EntityDamage, EntityDamage>
    {
        private const string DdsTopicName = "EntityDamage";
        private const long OrdinalValue = (long)EDescriptorType.dtEntityDamage;

        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly long _localNodeId;

        public override TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityDamageIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap,
            GhostCreationSystem ghostCreationSystem,
            long localNodeId)
            : base(participant, DdsTopicName, OrdinalValue, entityMap)
        {
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _localNodeId = localNodeId;
        }

        protected override void Decode(in EntityDamage data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            long netId = data.EntityId;
            if (!EntityMap.TryGetEntity(netId, out var entity))
            {
                var repo = view as EntityRepository;
                if (repo == null)
                {
                    FdpLog<EntityDamageIngressTranslator>.Warn(
                        "[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                    return;
                }

                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }

            cmd.SetComponent(entity, new Health { Current = data.Current, Max = data.Max });
        }

        public override void ScanAndPublish(ISimulationView view) { }

        public override void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityDamage health)
                return;

            repo.SetComponent(entity, new Health { Current = health.Current, Max = health.Max });
        }
    }
}
