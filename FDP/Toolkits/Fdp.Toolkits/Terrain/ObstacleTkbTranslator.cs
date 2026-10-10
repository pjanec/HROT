using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-3136</c> P-7a (D11, O3/O4, R-242) — projects <see cref="StaticObstacleDto"/>: the <see cref="StaticObstacle"/> marker
    /// with its material, the <see cref="ObstacleShape"/> box (⭐ unless the entity already carries one — a scenario's per-instance
    /// size), and a <see cref="PhysicsCollider"/> for movement and the spatial index (radius = half the box's diagonal, the box's
    /// height). The collider is NOT what stops rounds or sight: the obstacle is baked into the terrain (R-243) and the bullet and
    /// sight paths skip a <see cref="StaticObstacle"/>'s collider. Each write is guarded by <c>IsComponentTypeRegistered</c>.
    /// </summary>
    public sealed class ObstacleTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(StaticObstacleDto);
        }

        /// <summary>⭐ <see cref="ObstacleShape"/> is <c>[PerInstanceValue]</c>: a per-instance size reaches the other nodes through the
        /// <c>EntityObstacleShape</c> descriptor, and their ghost promotion waits for it (R-136).</summary>
        public IEnumerable<Type> GetProducedComponents()
        {
            yield return typeof(StaticObstacle);
            yield return typeof(ObstacleShape);
            yield return typeof(PhysicsCollider);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var dto = template.GetDescriptor<StaticObstacleDto>();
            if (dto == null) return;

            if (repo.IsComponentTypeRegistered<StaticObstacle>())
                repo.AddComponent(entity, new StaticObstacle { Material = new FixedString32(dto.Material ?? "concrete") });

            var shape = new ObstacleShape { Length = dto.Length, Width = dto.Width, Height = dto.Height };
            if (repo.IsComponentTypeRegistered<ObstacleShape>())
            {
                if (repo.HasComponent<ObstacleShape>(entity)) shape = repo.GetComponent<ObstacleShape>(entity);   // the instance's size wins
                else repo.AddComponent(entity, shape);
            }

            if (repo.IsComponentTypeRegistered<PhysicsCollider>())
                repo.AddComponent(entity, new PhysicsCollider
                {
                    Radius         = 0.5f * MathF.Sqrt(shape.Length * shape.Length + shape.Width * shape.Width),
                    CollisionLayer = PhysicsConstants.EntityCollisionLayer,
                    Height         = shape.Height,
                });
        }
    }
}
