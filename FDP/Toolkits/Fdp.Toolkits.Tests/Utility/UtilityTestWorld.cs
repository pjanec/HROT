using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Modules.Geographic.Components;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Utility;

namespace Fdp.Toolkit.Tests.Utility
{
    /// <summary>
    /// Test-world helper for Utility AI Phase-0 integration tests.
    /// Wraps an <see cref="EntityRepository"/>, registers all Phase-0-relevant
    /// component types, and provides convenience factory and mutation methods.
    /// </summary>
    public sealed class UtilityTestWorld : IDisposable
    {
        /// <summary>The underlying ECS repository.</summary>
        public EntityRepository Repo { get; }

        /// <summary>Monotonically increasing tick counter used for <c>AddOrUpdateTarget</c> calls.</summary>
        public uint Tick { get; private set; }

        /// <summary>
        /// Pre-built instance scorer backed by the catalog populated in the constructor.
        /// Callers must register input readers (e.g. via <see cref="StandardInputs.RegisterAll"/>)
        /// before invoking Scorer methods.
        /// </summary>
        public UtilityScorer Scorer { get; }

        public UtilityTestWorld()
        {
            Repo = new EntityRepository();

            // ── Register all component types ─────────────────────────────────────
            Repo.RegisterComponent<Health>();
            Repo.RegisterComponent<WeaponState>();
            Repo.RegisterComponent<WeaponMountInfo>();
            Repo.RegisterComponent<PartMetadata>();
            Repo.RegisterComponent<TargetMemory>();
            Repo.RegisterComponent<SensorContactList>();
            Repo.RegisterComponent<EqsSensor>();
            Repo.RegisterComponent<EqsCognitiveBuffer>();
            Repo.RegisterComponent<UnitRoster>();
            Repo.RegisterComponent<UnitSubordinate>();
            Repo.RegisterComponent<Blackboard1024>();
            Repo.RegisterComponent<Position>();
            Repo.RegisterComponent<UtilityDebugFlags>();
            Repo.RegisterComponent<UtilityTraceWorkingMemory1024>();
            Repo.RegisterComponent<UtilityResultBuffer>();

            // Build catalog and scorer once; input readers registered separately by caller.
            UtilityDecisionCatalog.RegisterAll(out var registry);
            Scorer = new UtilityScorer(registry);
        }

        public void Dispose() => Repo.Dispose();

        // ── Factory methods ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates an agent entity with Health, WeaponState (primary mount with MaxAmmo),
        /// Position (zero), and TargetMemory.
        /// </summary>
        /// <param name="health01">Health fraction in [0,1].</param>
        /// <param name="ammo01">Ammo fraction in [0,1] of <paramref name="initialAmmunition"/>.</param>
        /// <param name="initialAmmunition">Max ammo capacity (used to derive current ammo from fraction).</param>
        public Entity SpawnAgent(float health01, float ammo01, int initialAmmunition = 30)
        {
            var entity = Repo.CreateEntity();

            Repo.AddComponent(entity, new Health
            {
                Current = health01 * 100f,
                Max     = 100f
            });

            int ammo = (int)MathF.Round(ammo01 * initialAmmunition);
            Repo.AddComponent(entity, new WeaponState
            {
                Ammo           = ammo,
                MaxAmmo        = initialAmmunition,
                MuzzleVelocity = 800f
            });

            Repo.AddComponent(entity, new Position { Value = Vector3.Zero });
            Repo.AddComponent(entity, new TargetMemory());
            Repo.AddComponent(entity, new UtilityResultBuffer());
            Repo.AddComponent(entity, new UtilityDebugFlags { TraceEnabled = 1 });
            Repo.AddComponent(entity, new UtilityTraceWorkingMemory1024());

            return entity;
        }

        /// <summary>
        /// Creates a child weapon-mount entity linked to <paramref name="owner"/>.
        /// Adds WeaponState, WeaponMountInfo, and PartMetadata.
        /// </summary>
        public Entity SpawnWeaponMount(Entity owner, int mountIndex, ulong weaponGuid,
            float effRange, float ammo01, int initialAmmunition)
        {
            var child = Repo.CreateEntity();
            int ammo = (int)MathF.Round(ammo01 * initialAmmunition);

            Repo.AddComponent(child, new WeaponState
            {
                Ammo           = ammo,
                MaxAmmo        = initialAmmunition,
                MuzzleVelocity = 800f
            });
            Repo.AddComponent(child, new WeaponMountInfo
            {
                MountIndex     = mountIndex,
                WeaponGuid     = weaponGuid,
                EffectiveRange = effRange
            });
            Repo.AddComponent(child, new PartMetadata
            {
                ParentEntity      = owner,
                InstanceId        = mountIndex,
                DescriptorOrdinal = 0
            });

            // WeaponRangeBandFit reads Position from the mount entity.
            Repo.AddComponent(child, new Position { Value = Vector3.Zero });

            return child;
        }

        /// <summary>
        /// Sets the <see cref="WeaponState.Ammo"/> of a weapon mount resolved by <paramref name="mountIndex"/>.
        /// MountIndex 0 is the owner entity itself; index 1+ are children found via PartMetadata.
        /// </summary>
        public unsafe void SetWeaponAmmo(Entity owner, int mountIndex, float ammo01)
        {
            if (mountIndex == 0)
            {
                ref var ws = ref Repo.GetComponentRW<WeaponState>(owner);
                int maxAmmo = ws.MaxAmmo;
                ws.Ammo = (int)MathF.Round(ammo01 * maxAmmo);
                return;
            }

            // Find child mount entity with matching MountIndex and ParentEntity == owner
            var query = Repo.Query().With<WeaponMountInfo>().With<PartMetadata>().Build();
            foreach (var e in query)
            {
                ref readonly var pm = ref Repo.GetComponentRO<PartMetadata>(e);
                if (!pm.ParentEntity.Equals(owner)) continue;
                ref readonly var mi = ref Repo.GetComponentRO<WeaponMountInfo>(e);
                if (mi.MountIndex != mountIndex) continue;

                ref var ws = ref Repo.GetComponentRW<WeaponState>(e);
                int maxAmmo = ws.MaxAmmo;
                ws.Ammo = (int)MathF.Round(ammo01 * maxAmmo);
                return;
            }
        }

        /// <summary>
        /// Seeds a contact into <paramref name="self"/>'s TargetMemory using real
        /// <see cref="TargetMemory.AddOrUpdateTarget"/>.
        /// </summary>
        public void SeedContact(Entity self, Entity contact, float distanceM, float threatBoost,
            float contactHealth01, bool hasLos)
        {
            var modality = hasLos ? SensorModality.Visual : SensorModality.Acoustic;
            ref var mem = ref Repo.GetComponentRW<TargetMemory>(self);
            long entityId = (long)contact.PackedValue;
            TargetMemory.AddOrUpdateTarget(
                ref mem,
                entityId:   entityId,
                posX:       distanceM,
                posY:       0f,
                scoreBoost: threatBoost,
                tick:       ++Tick,
                modality:   modality);

            if (contactHealth01 >= 0f)
            {
                if (!Repo.HasComponent<Health>(contact))
                {
                    Repo.AddComponent(contact, new Health { Current = contactHealth01 * 100f, Max = 100f });
                }
                else
                {
                    ref var h = ref Repo.GetComponentRW<Health>(contact);
                    h.Current = contactHealth01 * h.Max;
                }
            }

            if (!Repo.HasComponent<Position>(contact))
            {
                Repo.AddComponent(contact, new Position { Value = new Vector3(distanceM, 0f, 0f) });
            }
        }

        /// <summary>
        /// Creates a child sensor entity with <see cref="EqsSensor"/> + seeded <see cref="EqsCognitiveBuffer"/>
        /// + <see cref="PartMetadata"/>.
        /// Uses <c>GetSpanRW()</c> to seed the buffer (avoids [InlineArray] defensive-copy trap).
        /// </summary>
        public Entity SpawnEqsSensor(Entity owner, uint blueprintId, float topScore, int count, int instanceId)
        {
            var child = Repo.CreateEntity();

            Repo.AddComponent(child, new EqsSensor
            {
                BlueprintId  = blueprintId,
                ContextSlot0 = owner
            });

            // Seed the cognitive buffer via GetSpanRW() — NEVER direct indexer assignment.
            var buf = new EqsCognitiveBuffer
            {
                Count           = count,
                LastUpdateTick  = 1u
            };
            var span = buf.GetSpanRW();
            for (int i = 0; i < count && i < 16; i++)
            {
                span[i] = new EqsResult
                {
                    EntityId = (long)owner.PackedValue,
                    Score    = topScore - i * 0.1f  // descending scores
                };
            }
            Repo.AddComponent(child, buf);

            Repo.AddComponent(child, new PartMetadata
            {
                ParentEntity      = owner,
                InstanceId        = instanceId,
                DescriptorOrdinal = 0
            });

            return child;
        }

        /// <summary>
        /// Creates a leader entity with UnitRoster, Blackboard1024, TargetMemory, and Position.
        /// </summary>
        public Entity SpawnLeader()
        {
            var entity = Repo.CreateEntity();
            Repo.AddComponent(entity, new UnitRoster());
            Repo.AddComponent(entity, new Blackboard1024());
            Repo.AddComponent(entity, new TargetMemory());
            Repo.AddComponent(entity, new Position { Value = Vector3.Zero });
            return entity;
        }

        /// <summary>
        /// Creates a squad member entity (SpawnAgent), links it to the leader via
        /// <see cref="UnitSubordinate"/>, and registers it in the leader's <see cref="UnitRoster"/>.
        /// </summary>
        public unsafe Entity SpawnSquadMember(Entity leader, float health01, float ammo01,
            bool asLauncher = false)
        {
            var member = SpawnAgent(health01, ammo01);

            Repo.AddComponent(member, new UnitSubordinate
            {
                Commander   = leader,
                Designation = TacticalDesignation.Undefined
            });

            ref var roster = ref Repo.GetComponentRW<UnitRoster>(leader);
            UnitRoster.Add(ref roster, (long)member.PackedValue);

            if (asLauncher)
                SpawnWeaponMount(member, mountIndex: 1, weaponGuid: Weapons.LauncherGuid,
                                 effRange: 350f, ammo01: ammo01, initialAmmunition: 4);

            return member;
        }

        /// <summary>
        /// Reads the ThreatMatrixAssignmentState from the leader's blackboard via
        /// <c>Blackboard1024.Project&lt;T&gt;</c> + <c>UnitRoster.IndexOf</c>.
        /// Returns -1L if the member is not found in the roster.
        /// </summary>
        public long AssignmentFor(Entity leader, Entity member)
        {
            ref var bb = ref Repo.GetComponentRW<Blackboard1024>(leader);
            ref var state = ref SquadCognitiveState.Project(ref bb).Assignment;
            ref var roster = ref Repo.GetComponentRW<UnitRoster>(leader);
            int idx = UnitRoster.IndexOf(ref roster, (long)member.PackedValue);
            return idx >= 0 ? state.GetAssignedTarget(idx) : -1L;
        }

        /// <summary>
        /// Overwrites Health.Current on <paramref name="entity"/> to
        /// <paramref name="health01"/> * Health.Max.
        /// Creates a Health component if one is absent.
        /// </summary>
        public void SetHealth(Entity entity, float health01)
        {
            if (!Repo.HasComponent<Health>(entity))
            {
                Repo.AddComponent(entity, new Health { Current = health01 * 100f, Max = 100f });
                return;
            }
            ref var h = ref Repo.GetComponentRW<Health>(entity);
            h.Current = health01 * h.Max;
        }

        /// <summary>
        /// Adjusts the entity's TargetMemory ThreatScores so that
        /// <see cref="StandardInputs.EnemyStrengthRatio"/> returns approximately
        /// <paramref name="ratio"/> (clamped to [0,1]).
        /// If no contacts exist, seeds a synthetic entity with the required score.
        /// </summary>
        public unsafe void SetEnemyStrengthRatio(Entity entity, float ratio)
        {
            float healthFraction = 1f;
            if (Repo.HasComponent<Health>(entity))
            {
                ref readonly var h = ref Repo.GetComponentRO<Health>(entity);
                if (h.Max > 0f)
                    healthFraction = Math.Clamp(h.Current / h.Max, 0f, 1f);
            }
            float targetSum = ratio * healthFraction * PerceptionConstants.MaxTrackedTargets;

            ref var tm = ref Repo.GetComponentRW<TargetMemory>(entity);
            if (tm.Count == 0)
            {
                // Seed one synthetic contact with the required aggregate threat score.
                var dummy = Repo.CreateEntity();
                Repo.AddComponent(dummy, new Position { Value = new Vector3(100f, 0f, 0f) });
                TargetMemory.AddOrUpdateTarget(ref tm, (long)dummy.PackedValue,
                    posX: 100f, posY: 0f, scoreBoost: targetSum, tick: ++Tick,
                    modality: SensorModality.Visual);
            }
            else
            {
                // Scale existing ThreatScores proportionally.
                float currentSum = 0f;
                for (int i = 0; i < tm.Count; i++) currentSum += tm.ThreatScores[i];
                if (currentSum > 0f)
                {
                    float scale = targetSum / currentSum;
                    for (int i = 0; i < tm.Count; i++) tm.ThreatScores[i] *= scale;
                }
                else
                {
                    float perContact = targetSum / Math.Max(1, tm.Count);
                    for (int i = 0; i < tm.Count; i++) tm.ThreatScores[i] = perContact;
                }
            }
        }

        /// <summary>
        /// Creates a generic target entity with Health (full) and Position at (100, 0, 0).
        /// Position is placed at 100 m so that distance-based scoring does not collapse.
        /// </summary>
        public Entity SpawnTarget()
        {
            var t = Repo.CreateEntity();
            Repo.AddComponent(t, new Health { Current = 100f, Max = 100f });
            Repo.AddComponent(t, new Position { Value = new Vector3(100f, 0f, 0f) });
            return t;
        }

        /// <summary>
        /// Seeds each target into the leader's TargetMemory at 120 m, threat 0.6, full health, LOS.
        /// </summary>
        public void SeedSquadContacts(Entity leader, Entity[] targets)
        {
            foreach (var t in targets)
                SeedContact(leader, t, distanceM: 120f, threatBoost: 0.6f,
                            contactHealth01: 1f, hasLos: true);
        }

        /// <summary>
        /// Computes a 32-bit FNV-1a hash of a name string, matching the source-generator formula.
        /// Basis: 2166136261u, Prime: 16777619u.
        /// </summary>
        public static uint Fnv1a32(string name)
        {
            uint hash = 2166136261u;
            foreach (char c in name)
            {
                hash ^= (byte)c;
                hash *= 16777619u;
            }
            return hash;
        }
    }

    public static class Weapons
    {
        public const ulong RifleGuid    = 0x0000_0000_0000_0001UL;
        public const ulong PistolGuid   = 0x0000_0000_0000_0002UL;
        public const ulong LauncherGuid = 0x0000_0000_0000_0003UL;
    }
}
