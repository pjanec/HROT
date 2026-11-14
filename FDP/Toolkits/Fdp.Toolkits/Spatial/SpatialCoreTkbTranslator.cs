using System;
using System.Collections.Generic;
using System.Numerics;
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

        /// <summary>
        /// ⭐⭐ <b>Both are <c>[PerInstanceValue]</c>, and only one survives the derivation</b> — which is the
        /// point of intersecting rather than declaring. <c>SimTransform</c> is paired with
        /// <c>dtWorldPos</c> by <c>GeoSpatialEgressTranslator.TargetComponentIds</c> so it CAN arrive;
        /// <c>SimVelocity</c> reaches <c>DescriptorOwnershipMap</c> only through the explicit
        /// <c>RegisterMapping</c> authority block, which feeds no descriptor pairing ⇒ nothing ingresses it
        /// and it is excluded for that reason, not by mislabelling it.
        /// 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §6.6a "THE FOURTH INTERSECTION".
        /// </summary>
        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(SimTransform);
            yield return typeof(SimVelocity);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            if (template.GetDescriptor<TkbMasterDto>() == null) return;

            if (repo.IsComponentTypeRegistered<SimTransform>() && !repo.HasComponent<SimTransform>(entity))
                // ⭐⭐ CE-4080 — a pose-less entity is UNTURNED (identity), not a zero quaternion: `default(SimTransform).Rotation` is (0,0,0,0),
                //   which is no rotation at all — Quaternion.Inverse of it is NaN. 📐 Measured live: a late-joining Player promotes a stationary tank
                //   BEFORE its first WorldPos arrives, so this stamp is what the node holds (and its FPS crosshair tests) until the sample lands.
                repo.AddComponent(entity, new SimTransform { Rotation = Quaternion.Identity });

            if (repo.IsComponentTypeRegistered<SimVelocity>() && !repo.HasComponent<SimVelocity>(entity))
                repo.AddComponent(entity, new SimVelocity());
        }
    }
}
