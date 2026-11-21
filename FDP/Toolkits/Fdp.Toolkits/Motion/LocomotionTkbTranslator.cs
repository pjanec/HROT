using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    /// <summary>⭐ CE-2168 (MD-A) — projects <c>Gen.Locomotion</c> onto the <see cref="Locomotion"/> component, on every node.</summary>
    public sealed class LocomotionTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors() { yield return typeof(LocomotionDto); }

        public IEnumerable<Type> GetProducedComponents() { yield return typeof(Locomotion); }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var dto = template.GetDescriptor<LocomotionDto>();
            if (dto == null || dto.Class == LocomotionClass.Unknown || !repo.IsComponentTypeRegistered<Locomotion>()) return;
            if (repo.HasComponent<Locomotion>(entity)) repo.SetComponent(entity, new Locomotion { Class = dto.Class });
            else repo.AddComponent(entity, new Locomotion { Class = dto.Class });
        }
    }
}
