using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

using EcsNavigationStatus = FDP.Toolkit.Navigation.NavigationStatus;
using EcsNavResult        = FDP.Toolkit.Navigation.NavigationResult;

namespace Hrot.SimHost.Network
{
    /// <summary>
    /// Egress translator: reads the ECS <see cref="EcsNavigationStatus"/> component
    /// for all locally-owned entities and publishes a DDS <see cref="Hrot.NED.Descriptors.NavigationStatus"/>
    /// sample for each.
    ///
    /// <para>
    /// Entities with <c>status.Result == NavigationResult.InProgress</c> and
    /// <c>status.IntentId == 0</c> (uninitialised) are still published to keep
    /// remote Brain nodes informed.
    /// </para>
    /// </summary>
    public sealed class NavigationStatusEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<Hrot.NED.Descriptors.NavigationStatus> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName      => "NavigationStatus";
        public long   DescriptorOrdinal => 53;

        public NavigationStatusEgressTranslator(
            DdsParticipant   dds,
            NetworkEntityMap entityMap)
        {
            _writer    = new DdsWriter<Hrot.NED.Descriptors.NavigationStatus>(dds, "NavigationStatus");
            _entityMap = entityMap ?? throw new System.ArgumentNullException(nameof(entityMap));
        }

        /// <summary>No ingress for this translator.</summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <summary>
        /// Publishes <see cref="Hrot.NED.Descriptors.NavigationStatus"/> to DDS for every
        /// authority-owned entity that has a <see cref="EcsNavigationStatus"/> component.
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<EcsNavigationStatus>()
                .With<NetworkIdentity>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            foreach (var entity in query)
            {
                // Only publish navigation status for locally-owned (Muscle) entities.
                if (!view.HasAuthority(entity, DescriptorOrdinal))
                    continue;

                var status = view.GetComponentRO<EcsNavigationStatus>(entity);
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

                _writer.Write(new Hrot.NED.Descriptors.NavigationStatus
                {
                    EntityId = (int)netId.Value,
                    IntentId = status.IntentId,
                    Result   = MapResult(status.Result)
                });

                FdpLog<NavigationStatusEgressTranslator>.Debug(
                    "[TRACE-SH] NavigationStatus egress: EntityId={0} IntentId={1} Result={2}",
                    netId.Value, status.IntentId, status.Result);
            }
        }

        /// <summary>No ghost promotion needed.</summary>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <summary>No DDS dispose needed.</summary>
        public void Dispose(long networkEntityId) { }

        // ── Enum mapping ──────────────────────────────────────────────────────

        private static ENavigationResult MapResult(EcsNavResult result) => result switch
        {
            EcsNavResult.Arrived           => ENavigationResult.RES_ARRIVED,
            EcsNavResult.FailedBlocked     => ENavigationResult.RES_FAILED_BLOCKED,
            EcsNavResult.FailedUnreachable => ENavigationResult.RES_FAILED_UNREACHABLE,
            _                              => ENavigationResult.RES_IN_PROGRESS,
        };
    }
}
