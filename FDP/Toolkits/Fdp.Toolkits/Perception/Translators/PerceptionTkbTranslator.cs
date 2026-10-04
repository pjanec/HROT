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

        /// <summary>⭐ None of these carries <c>[PerInstanceValue]</c> — a receptor is a template constant and
        /// the two memories legitimately start empty — so this translator contributes nothing to any
        /// promotion gate. ⚠ It is still declared: the set is a statement about the TRANSLATOR, not about
        /// today's attribute list, and a later <c>[PerInstanceValue]</c> must find it already here.</summary>
        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(PerceptionReceptor);
            yield return typeof(TargetMemory);
            yield return typeof(ActiveSensorTracks);
            yield return typeof(SensorMount);
            yield return typeof(SensorTag);
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

            // ⭐ Posture eye heights — only when the template states any; otherwise the LOS strategy's default
            // soldier mount applies (docs/DESIGN_Terrain_World.md §7.1 W5).
            if ((dto.EyeHeightStanding > 0f || dto.EyeHeightCrouched > 0f || dto.EyeHeightProne > 0f)
                && repo.IsComponentTypeRegistered<SensorMount>() && !repo.HasComponent<SensorMount>(entity))
            {
                float standing = dto.EyeHeightStanding > 0f ? dto.EyeHeightStanding : 1.7f;
                repo.AddComponent(entity, new SensorMount
                {
                    Standing = standing,
                    Crouched = dto.EyeHeightCrouched > 0f ? dto.EyeHeightCrouched : standing,
                    Prone    = dto.EyeHeightProne    > 0f ? dto.EyeHeightProne    : standing,
                });
            }

            if (dto.VisionRange > 0f)
            {
                if (repo.IsComponentTypeRegistered<TargetMemory>() && !repo.HasComponent<TargetMemory>(entity))
                    repo.AddComponent(entity, new TargetMemory());

                if (repo.IsComponentTypeRegistered<ActiveSensorTracks>() && !repo.HasComponent<ActiveSensorTracks>(entity))
                    repo.AddComponent(entity, new ActiveSensorTracks());
            }

            // ⭐ The unit's SENSORS — one child per entry, built the same way on every node (R-185 K, design §5.1).
            for (int i = 0; i < dto.Sensors.Count; i++)
                Fdp.Toolkit.Perception.Sensors.SensorChildFactory.EnsureTkbChild(repo, entity, i, dto.Sensors[i]);

            // ⭐⭐ CE-3038 — a TKB that lists no sensors but can SEE gets ONE implicit visual sensor (part 1000): vision is a
            //    sensor like any other, solved by the EQS solver and debounced by its memory stage (design §5.5). ⛔ The
            //    unit no longer carries a SensorContactList — each perception sensor keeps its own.
            if (dto.Sensors.Count == 0 && dto.VisionRange > 0f)
            {
                var visual = Fdp.Toolkit.Perception.Sensors.SensorChildFactory.EnsureTkbChild(
                    repo, entity, 0, Fdp.Toolkit.Perception.Sensors.VisualPerception.ImplicitEntry(dto));
                if (!visual.IsNull)
                {
                    var tag = repo.GetComponentRO<SensorTag>(visual);
                    tag.Implicit = 1;
                    repo.SetComponent(visual, tag);
                }
            }
        }
    }
}
