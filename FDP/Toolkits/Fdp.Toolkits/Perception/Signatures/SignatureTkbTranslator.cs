using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Perception.Signatures
{
    /// <summary>
    /// ⭐ <c>CE-3061</c> — stamps an entity's signatures from its TKB <see cref="SignaturesDto"/>. Skips each component its node
    /// does not register (the same contract as every TKB translator).
    /// </summary>
    public sealed class SignatureTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(SignaturesDto);
        }

        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(ThermalState);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var dto = template.GetDescriptor<SignaturesDto>();
            if (dto?.Thermal is { } th && repo.IsComponentTypeRegistered<ThermalState>() && !repo.HasComponent<ThermalState>(entity))
                repo.AddComponent(entity, new ThermalState
                {
                    BaseSignature        = th.BaseSignature,
                    RunningHeatPerSecond = th.RunningHeatPerSecond,
                    FiringHeatPerShot    = th.FiringHeatPerShot,
                    CooldownPerSecond    = th.CooldownPerSecond,
                    ReferenceSpeed       = th.ReferenceSpeed,
                });
        }
    }
}
