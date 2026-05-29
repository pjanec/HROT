using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat.Translators
{
    /// <summary>
    /// Translates <see cref="CombatPlatformDefDto"/> and <see cref="WeaponSuiteDto"/>
    /// into combat ECS components.
    /// <para>
    /// The <see cref="PhysicsCollider"/> update is <em>additive</em>:
    /// if <c>VehicleKinematicsTkbTranslator</c> already stamped a vehicle-specific
    /// collider, this translator ORs the combat collision layer onto the existing
    /// component rather than overwriting the radius.
    /// </para>
    /// </summary>
    public sealed class CombatTkbTranslator : ITkbEntityTranslator
    {
        private const float DefaultColliderRadius  = 2.5f;
        private const float DefaultMuzzleVelocity  = 800f;
        private const byte  CombatCollisionLayer   = 2;

        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(CombatPlatformDefDto);
            yield return typeof(WeaponSuiteDto);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            // ── Health ────────────────────────────────────────────────────────────
            var platformDef = template.GetDescriptor<CombatPlatformDefDto>();
            if (platformDef != null)
            {
                if (repo.IsComponentTypeRegistered<Health>() && !repo.HasComponent<Health>(entity))
                    repo.AddComponent(entity, new Health
                    {
                        Current = platformDef.MaxHealth,
                        Max     = platformDef.MaxHealth
                    });

                // Additive collider: OR combat layer onto vehicle collider if present,
                // or stamp a fresh collider if the kinematics translator did not run.
                if (repo.IsComponentTypeRegistered<PhysicsCollider>())
                {
                    if (repo.HasComponent<PhysicsCollider>(entity))
                    {
                        ref var col = ref repo.GetComponentRW<PhysicsCollider>(entity);
                        col.CollisionLayer |= CombatCollisionLayer;
                    }
                    else
                    {
                        repo.AddComponent(entity, new PhysicsCollider
                        {
                            Radius         = DefaultColliderRadius,
                            CollisionLayer = CombatCollisionLayer
                        });
                    }
                }
            }

            // ── WeaponState (primary mount) + child mount entities ────────────────
            var suite = template.GetDescriptor<WeaponSuiteDto>();
            if (suite != null && suite.Mounts.Count > 0)
            {
                var primary = suite.Mounts[0];
                // Primary mount: WeaponState stays on the owner entity (back-compat with actuators).
                if (repo.IsComponentTypeRegistered<WeaponState>() && !repo.HasComponent<WeaponState>(entity))
                    repo.AddComponent(entity, new WeaponState
                    {
                        Ammo           = primary.InitialAmmunition,
                        MaxAmmo        = primary.InitialAmmunition,
                        MuzzleVelocity = primary.MuzzleVelocity > 0f
                            ? primary.MuzzleVelocity
                            : DefaultMuzzleVelocity
                    });

                // Additional mounts (index 1+): each gets a child entity.
                if (repo.IsComponentTypeRegistered<WeaponMountInfo>() && repo.IsComponentTypeRegistered<PartMetadata>())
                {
                    var caps = template.GetDescriptor<WeaponCapabilitiesDto>(); // may be null
                    for (int i = 1; i < suite.Mounts.Count; i++)
                    {
                        var mount = suite.Mounts[i];
                        var child = repo.CreateEntity();
                        repo.AddComponent(child, new WeaponState
                        {
                            Ammo           = mount.InitialAmmunition,
                            MaxAmmo        = mount.InitialAmmunition,
                            MuzzleVelocity = mount.MuzzleVelocity > 0f ? mount.MuzzleVelocity : DefaultMuzzleVelocity
                        });
                        repo.AddComponent(child, new WeaponMountInfo
                        {
                            MountIndex     = i,
                            WeaponGuid     = mount.WeaponGuid,
                            EffectiveRange = caps?.EffectiveRange ?? 0f,
                        });
                        repo.AddComponent(child, new PartMetadata
                        {
                            ParentEntity      = entity,
                            InstanceId        = i,
                            DescriptorOrdinal = 0,
                        });
                    }
                }
            }
        }
    }
}
