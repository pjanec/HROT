using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

using EcsNavigationStatus = FDP.Toolkit.Navigation.NavigationStatus;
using EcsNavResult        = FDP.Toolkit.Navigation.NavigationResult;

namespace Hrot.SimHost.Network
{
    /// <summary>
    /// Ingress translator: polls the DDS <c>NavigationStatus</c> topic and
    /// updates the ECS <see cref="EcsNavigationStatus"/> component for the
    /// matching entity.
    ///
    /// <para>
    /// No coordinate conversion is needed — <see cref="EcsNavigationStatus"/>
    /// contains only <c>IntentId</c> and <c>Result</c>, both of which are
    /// wire-stable scalar values.
    /// </para>
    /// </summary>
    public sealed class NavigationStatusIngressTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<Hrot.NED.Descriptors.NavigationStatus> _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName      => "NavigationStatus";
        public long   DescriptorOrdinal => 53;

        public NavigationStatusIngressTranslator(
            DdsParticipant   dds,
            NetworkEntityMap entityMap)
        {
            _reader    = new DdsReader<Hrot.NED.Descriptors.NavigationStatus>(dds, "NavigationStatus");
            _entityMap = entityMap ?? throw new System.ArgumentNullException(nameof(entityMap));
        }

        /// <summary>
        /// Polls DDS reader and updates <see cref="EcsNavigationStatus"/> on matching entities.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            using var loan = _reader.Take();

            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                var msg = sample.Data;

                if (!_entityMap.TryGetEntity(msg.EntityId, out var entity))
                {
                    FdpLog<NavigationStatusIngressTranslator>.Debug(
                        "[TRACE-SH] NavigationStatus ingress: unknown EntityId={0} — skipped", msg.EntityId);
                    continue;
                }

                cmd.SetComponent(entity, new EcsNavigationStatus
                {
                    IntentId = msg.IntentId,
                    Result   = MapResult(msg.Result)
                });

                FdpLog<NavigationStatusIngressTranslator>.Debug(
                    "[TRACE-SH] NavigationStatus ingress: EntityId={0} IntentId={1} Result={2}",
                    msg.EntityId, msg.IntentId, msg.Result);
            }
        }

        /// <summary>No egress for this translator.</summary>
        public void ScanAndPublish(ISimulationView view) { }

        /// <summary>No ghost promotion needed.</summary>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <summary>No DDS dispose needed.</summary>
        public void Dispose(long networkEntityId) { }

        // ── Enum mapping ──────────────────────────────────────────────────────

        private static EcsNavResult MapResult(ENavigationResult result) => result switch
        {
            ENavigationResult.RES_ARRIVED             => EcsNavResult.Arrived,
            ENavigationResult.RES_FAILED_BLOCKED      => EcsNavResult.FailedBlocked,
            ENavigationResult.RES_FAILED_UNREACHABLE  => EcsNavResult.FailedUnreachable,
            _                                         => EcsNavResult.InProgress,
        };
    }
}
