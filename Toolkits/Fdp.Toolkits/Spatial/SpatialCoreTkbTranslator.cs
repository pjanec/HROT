using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Spatial
{
    /// <summary>
    /// Baseline translator that stamps zeroed spatial components on every entity
    /// that has a <see cref="TkbMasterDto"/> descriptor.
    /// Must run before any translator that reads <c>SimTransform</c> or
    /// <c>SimVelocity</c>.
    /// </summary>
    public sealed class SpatialCoreTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(TkbMasterDto);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            if (template.GetDescriptor<TkbMasterDto>() == null) return;

            if (repo.IsComponentTypeRegistered<SimTransform>())
                repo.AddComponent(entity, new SimTransform());

            if (repo.IsComponentTypeRegistered<SimVelocity>())
                repo.AddComponent(entity, new SimVelocity());
        }
    }
}
