using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Perception.Translators
{
    /// <summary>
    /// Translates <see cref="SensorCapabilitiesDto"/> into perception ECS components.
    /// Skips each component if its type is not registered on this node.
    /// </summary>
    public sealed class PerceptionTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(SensorCapabilitiesDto);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var dto = template.GetDescriptor<SensorCapabilitiesDto>();
            if (dto == null) return;

            float halfFovRad = dto.FieldOfViewDegrees * 0.5f * (float)Math.PI / 180f;
            float fovCos     = (float)Math.Cos(halfFovRad);

            if (repo.IsComponentTypeRegistered<PerceptionReceptor>() && !repo.HasComponent<PerceptionReceptor>(entity))
                repo.AddComponent(entity, new PerceptionReceptor
                {
                    VisionRange      = dto.VisionRange,
                    HearingRange     = dto.HearingRange,
                    FieldOfViewCos   = fovCos
                });

            if (dto.VisionRange > 0f)
            {
                if (repo.IsComponentTypeRegistered<TargetMemory>() && !repo.HasComponent<TargetMemory>(entity))
                    repo.AddComponent(entity, new TargetMemory());

                if (repo.IsComponentTypeRegistered<SensorContactList>() && !repo.HasComponent<SensorContactList>(entity))
                    repo.AddComponent(entity, new SensorContactList());

                if (repo.IsComponentTypeRegistered<ActiveSensorTracks>() && !repo.HasComponent<ActiveSensorTracks>(entity))
                    repo.AddComponent(entity, new ActiveSensorTracks());
            }
        }
    }
}
