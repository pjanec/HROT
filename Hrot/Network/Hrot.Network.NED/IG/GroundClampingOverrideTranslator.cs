using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using DdsEClampingMode = Hrot.NED.Descriptors.EClampingMode;
using IgEClampingMode  = Fdp.Modules.Geographic.EClampingMode;
using Fdp.Interfaces;

namespace Hrot.Network.NED.IG
{
    /// <summary>
    /// Ingress-only <see cref="IDescriptorTranslator"/> that maps
    /// <see cref="GroundClampingOverride"/> DDS samples into per-entity
    /// <see cref="GroundClampingConfig"/> ECS components.
    ///
    /// <para>
    /// When a sample arrives the translator:
    /// <list type="number">
    ///   <item>Resolves the DDS <c>EntityId</c> to a local <see cref="Entity"/> via
    ///   <see cref="NetworkEntityMap"/>.</item>
    ///   <item>Preserves <see cref="GroundClampingConfig.BaseRequiresClamping"/> if the
    ///   component already exists, otherwise initialises it to zero (aircraft default).</item>
    ///   <item>Writes the updated <see cref="GroundClampingConfig"/> through the
    ///   <see cref="IEntityCommandBuffer"/> so the mutation is applied on the main thread
    ///   after ingress completes.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <see cref="ScanAndPublish"/>, <see cref="ApplyToEntity"/>, and
    /// <see cref="Dispose"/> are no-ops (this is ingress-only; there is no egress
    /// for clamping overrides from the IG side).
    /// </para>
    /// </summary>
    public sealed class GroundClampingOverrideTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<GroundClampingOverride> _reader;
        private readonly NetworkEntityMap _entityMap;

        public long   DescriptorOrdinal => 66;
        public string TopicName         => "GroundClampingOverride";

        public GroundClampingOverrideTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap)
        {
            _reader    = new DdsReader<GroundClampingOverride>(participant);
            _entityMap = entityMap;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            using var loan = _reader.Take();

            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;

                long entityId = sample.Data.EntityId;
                if (!_entityMap.TryGetEntity(entityId, out var entity)) continue;

                // Map wire enum -> engine enum (ordinal values are identical: 0/1/2).
                var engineMode = (IgEClampingMode)(int)sample.Data.Mode;

                // Preserve BaseRequiresClamping from the existing component if present.
                byte baseRequiresClamping = 0;
                if (view.HasComponent<GroundClampingConfig>(entity))
                {
                    baseRequiresClamping = view.GetComponentRO<GroundClampingConfig>(entity).BaseRequiresClamping;
                }

                cmd.SetComponent(entity, new GroundClampingConfig
                {
                    Mode                 = engineMode,
                    BaseRequiresClamping = baseRequiresClamping,
                });
            }
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not GroundClampingOverride wire) return;

            var engineMode = (IgEClampingMode)(int)wire.Mode;

            byte baseRequiresClamping = 0;
            if (repo.HasComponent<GroundClampingConfig>(entity))
                baseRequiresClamping = repo.GetComponent<GroundClampingConfig>(entity).BaseRequiresClamping;

            repo.SetComponent(entity, new GroundClampingConfig
            {
                Mode                 = engineMode,
                BaseRequiresClamping = baseRequiresClamping,
            });
        }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
