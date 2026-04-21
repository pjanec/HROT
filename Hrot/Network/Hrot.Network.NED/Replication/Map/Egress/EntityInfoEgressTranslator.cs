using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Utilities;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
	/// <summary>
	/// Egress translator that publishes <see cref="EntityInfo"/> DDS samples
	/// from the internal <see cref="EntityInfo"/> managed ECS component.
	///
	/// This ensures that when SimHost creates an entity with a force affiliation
	/// (e.g. Hostile, set via <c>CreateEntityRequest.InitialDescriptors</c>),
	/// the affiliation is broadcast back to IG and ExCon via the DDS "EntityInfo"
	/// topic so that rendering and selection panels show the correct side colour
	/// and symbol (Task 18 fix).
	///
	/// <para>
	/// Only locally-owned entities are published (<see cref="AuthorityExtensions.HasAuthority"/>).
	/// Publication is reliable (publish-once-on-first + dirty on change).
	/// </para>
	/// </summary>
	public class EntityInfoEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityInfo";
        private const long OrdinalValue = (long)EDescriptorType.dtEntityInfo;

        private readonly DdsWriter<Hrot.NED.Descriptors.EntityInfo> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly long _localNodeId;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityInfoEgressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            long localNodeId)
        {
			_writer = new DdsWriter<Hrot.NED.Descriptors.EntityInfo>( participant, DdsTopicName );
            _entityMap = entityMap ?? throw new System.ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
        }

        // â”€â”€ Ingress (egress-only â€” nothing to consume) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

		// â”€â”€ Egress â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

		/// <summary>
		/// Scans all authority-owned entities with <see cref="EntityInfo"/> and
		/// publishes <see cref="EntityInfo"/> DDS samples for dirty / new entries.
		/// </summary>
		public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<NetworkIdentity>()
                .With<Fdp.Core.EntityInfo>()
                // Include Constructing so affiliation is broadcast at spawn time.
                .WithLifecycle( EntityLifecycle.All)
                .Build();

            long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                // Only publish for entities this node owns.
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                // Smart egress: Hrot.NED.Descriptors.EntityInfo is RELIABLE â€” publish once on first
                // encounter, then only when the IgEntityData component is dirty.
                if (!SmartEgressUtil.ShouldPublish(view, entity, DescriptorOrdinal, isUnreliable: false))
                    continue;

                ref readonly var netId  = ref view.GetComponentRO<NetworkIdentity>(entity);
                ref readonly var data   = ref view.GetComponentRO<Fdp.Core.EntityInfo>(entity);

				_writer.Write(new Hrot.NED.Descriptors.EntityInfo
                {
                    EntityId        = (int)netId.Value,
                    Name            = data.Name.ToString(),
                    ForceIdentifier = MapForceId(data.ForceId),
                    CommanderId     = data.CommanderId,
                });

                SentSampleCount++;
                SmartEgressUtil.MarkPublished(view, entity, DescriptorOrdinal);

                FdpLog<EntityInfoEgressTranslator>.Debug(
                    "[Node-{0}] Egress: Hrot.NED.Descriptors.EntityInfo NetID={1} Force={2}",
                    _localNodeId, netId.Value, data.ForceId);
            }
        }

        // â”€â”€ Ghost promotion (n/a for egress-only translator) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

		/// <summary>
		/// Sends a DDS dispose for the named <see cref="EntityInfo"/> instance.
		/// </summary>
		public void Dispose(long networkEntityId)
        {
			_writer.DisposeInstance(new Hrot.NED.Descriptors.EntityInfo { EntityId = (int)networkEntityId } );
        }

        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static eForceIdentifier MapForceId(ForceId forceId) =>
            forceId switch
            {
                ForceId.Friend  => eForceIdentifier.FORCE_FRIENDLY,
                ForceId.Hostile => eForceIdentifier.FORCE_OPPOSING,
                ForceId.Neutral => eForceIdentifier.FORCE_NEUTRAL,
                _               => eForceIdentifier.FORCE_UNKNOWN,
            };
    }
}
