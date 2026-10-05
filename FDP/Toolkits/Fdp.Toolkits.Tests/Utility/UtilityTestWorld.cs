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
            Repo.RegisterComponent<SquadCognitiveState>();
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
                // ⭐ CE-3054 — threatBoost is the contact's FRESHNESS in [0, 1]; the memory stores the score that reads as it.
                scoreBoost: threatBoost * PerceptionConstants.FreshnessSaturation,
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
            Repo.AddComponent(entity, default(SquadCognitiveState));
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
            UnitRoster.Add(ref roster, member);

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
            ref var state = ref Repo.GetComponentRW<SquadCognitiveState>(leader).Assignment;
            ref var roster = ref Repo.GetComponentRW<UnitRoster>(leader);
            int idx = UnitRoster.IndexOf(ref roster, member);
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
        /// ⭐ CE-3054 — makes <see cref="StandardInputs.EnemyStrengthRatio"/> (Σ danger ÷ (Σ danger + own strength)) read
        /// approximately <paramref name="ratio"/> (capped at 0.95) by adding ARMED dummy contacts to the memory. The danger
        /// is integral per contact, so the result is the nearest reachable value.
        /// ⛔ SUPERSEDED: scaling the memory scores — the ratio no longer reads them (hidden is not harmless).
        /// </summary>
        public unsafe void SetEnemyStrengthRatio(Entity entity, float ratio)
        {
            float v = Math.Clamp(ratio, 0f, 0.95f);
            float own = Fdp.Toolkit.Perception.ThreatDanger.OwnStrength(Repo, entity);
            ref var tm = ref Repo.GetComponentRW<TargetMemory>(entity);
            float enemy = 0f;
            for (int i = 0; i < tm.Count; i++)
                enemy += Fdp.Toolkit.Perception.ThreatDanger.Of(Repo, entity, new Entity((ulong)tm.EntityIds[i]));
            float wanted = v * own / (1f - v);
            int add = (int)MathF.Round(wanted - enemy);
            for (int k = 0; k < add && tm.Count < PerceptionConstants.MaxTrackedTargets; k++)
            {
                var dummy = Repo.CreateEntity();
                Repo.AddComponent(dummy, new Position { Value = new Vector3(100f + k, 0f, 0f) });
                Repo.AddComponent(dummy, new WeaponState { Ammo = 30, MaxAmmo = 30, MuzzleVelocity = 800f });
                TargetMemory.AddOrUpdateTarget(ref tm, (long)dummy.PackedValue,
                    posX: 100f + k, posY: 0f, scoreBoost: PerceptionConstants.FreshnessSaturation, tick: ++Tick,
                    modality: SensorModality.Visual);
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
