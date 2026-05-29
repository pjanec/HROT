using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Modules.Geographic.Components;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// FNV-1a-16 identifiers for all Phase 1 standard input readers.
    /// Each constant is computed as <c>(ushort)(Fnv1a32(name) &amp; 0xFFFF)</c>
    /// where Fnv1a32 uses basis=2166136261 and prime=16777619.
    /// </summary>
    public static class StandardInputIds
    {
        // Group A: weapon / health / distance
        public const ushort AmmoFraction             = 0x2C39;
        public const ushort WeaponHasAmmo            = 0xC96D;
        public const ushort WeaponReadiness          = 0xA563;
        public const ushort HealthFraction           = 0x13D9;
        public const ushort ContactHealthFraction    = 0xA533;
        public const ushort DistanceToContext        = 0x96DE;

        // Group B: perception
        public const ushort ContactThreatLevel       = 0x055B;
        public const ushort HasLineOfSight           = 0xF98D;
        public const ushort HaveLiveTarget           = 0xC20C;
        public const ushort EnemyStrengthRatio       = 0x5635;

        // Group C: EQS
        public const ushort EqsTopScore              = 0x2227;
        public const ushort EqsResultCount          = 0x71F0;

        // Group D: assignment / misc
        public const ushort IsAssignedTarget         = 0x76F0;
        public const ushort AllyAdvancingNearby      = 0x141B;
        public const ushort Constant                 = 0xAB45;
        public const ushort WeaponRangeBandFit       = 0x2C0C;
        public const ushort WeaponEffectivenessVsTarget = 0xEE5F;
    }

    /// <summary>
    /// Phase 1 catalog of standard Utility AI input readers.
    /// Call <see cref="RegisterAll"/> once at startup to register all 17 readers.
    /// </summary>
    public static unsafe class StandardInputs
    {
        // ── Group A: weapon / health / distance ──────────────────────────────────

        /// <summary>
        /// Returns Ammo/MaxAmmo clamped to [0,1] for the WeaponState on ctx.Self.
        /// Returns 0 if MaxAmmo is 0 or WeaponState is absent.
        /// </summary>
        [UtilityInput("AmmoFraction")]
        public static float AmmoFraction(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<WeaponState>(ctx.Self)) return 0f;
            ref readonly var ws = ref ctx.Repo.GetComponentRO<WeaponState>(ctx.Self);
            float result = ws.MaxAmmo == 0 ? 0f : Math.Clamp((float)ws.Ammo / ws.MaxAmmo, 0f, 1f);
            Debug.Assert(result >= 0f && result <= 1f);
            return result;
        }

        /// <summary>
        /// Returns 1 if Ammo &gt; 0 on ctx.Self's WeaponState, else 0.
        /// Returns 0 if WeaponState is absent.
        /// </summary>
        [UtilityInput("WeaponHasAmmo")]
        public static float WeaponHasAmmo(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<WeaponState>(ctx.Self)) return 0f;
            ref readonly var ws = ref ctx.Repo.GetComponentRO<WeaponState>(ctx.Self);
            float result = ws.Ammo > 0 ? 1f : 0f;
            Debug.Assert(result >= 0f && result <= 1f);
            return result;
        }

        /// <summary>
        /// Returns 1 if CooldownSecondsRemaining &lt;= 0 on ctx.Self's WeaponState, else 0.
        /// Returns 0 if WeaponState is absent.
        /// </summary>
        [UtilityInput("WeaponReadiness")]
        public static float WeaponReadiness(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<WeaponState>(ctx.Self)) return 0f;
            ref readonly var ws = ref ctx.Repo.GetComponentRO<WeaponState>(ctx.Self);
            float result = ws.CooldownSecondsRemaining <= 0f ? 1f : 0f;
            Debug.Assert(result >= 0f && result <= 1f);
            return result;
        }

        /// <summary>
        /// Returns Current/Max clamped to [0,1] for the Health on ctx.Self.
        /// Returns 0 if Max is 0 or Health is absent.
        /// </summary>
        [UtilityInput("HealthFraction")]
        public static float HealthFraction(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<Health>(ctx.Self)) return 0f;
            ref readonly var h = ref ctx.Repo.GetComponentRO<Health>(ctx.Self);
            float result = h.Max == 0f ? 0f : Math.Clamp(h.Current / h.Max, 0f, 1f);
            Debug.Assert(result >= 0f && result <= 1f);
            return result;
        }

        /// <summary>
        /// Returns Current/Max clamped to [0,1] for the Health on ctx.Context.
        /// Returns 0 if ctx.Context has no Health or Max is 0.
        /// </summary>
        [UtilityInput("ContactHealthFraction")]
        public static float ContactHealthFraction(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<Health>(ctx.Context)) return 0f;
            ref readonly var h = ref ctx.Repo.GetComponentRO<Health>(ctx.Context);
            float result = h.Max == 0f ? 0f : Math.Clamp(h.Current / h.Max, 0f, 1f);
            Debug.Assert(result >= 0f && result <= 1f);
            return result;
        }

        /// <summary>
        /// Returns 1 - clamp(distance/MaxRange, 0, 1) where distance is between ctx.Self and ctx.Context.
        /// MaxRange defaults to 1000 m when Params.MaxRange &lt;= 0.
        /// Returns 0 if either entity lacks a Position component.
        /// </summary>
        [UtilityInput("DistanceToContext")]
        public static float DistanceToContext(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<Position>(ctx.Self) || !ctx.Repo.HasComponent<Position>(ctx.Context)) return 0f;
            ref readonly var selfPos = ref ctx.Repo.GetComponentRO<Position>(ctx.Self);
            ref readonly var ctxPos  = ref ctx.Repo.GetComponentRO<Position>(ctx.Context);
            float maxRange = ctx.Params.MaxRange > 0f ? ctx.Params.MaxRange : 1000f;
            float distance = Vector3.Distance(selfPos.Value, ctxPos.Value);
            float result   = Math.Clamp(1f - distance / maxRange, 0f, 1f);
            Debug.Assert(result >= 0f && result <= 1f);
            return result;
        }

        // ── Group B: perception ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the ThreatScore for ctx.Context from ctx.Self's TargetMemory, clamped to [0,1].
        /// Returns 0 if ctx.Context is not found in TargetMemory or TargetMemory is absent.
        /// </summary>
        [UtilityInput("ContactThreatLevel")]
        public static float ContactThreatLevel(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<TargetMemory>(ctx.Self)) return 0f;
            ref readonly var mem = ref ctx.Repo.GetComponentRO<TargetMemory>(ctx.Self);
            long targetId = (long)ctx.Context.PackedValue;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.EntityIds[i] == targetId)
                {
                    float result = Math.Clamp(mem.ThreatScores[i], 0f, 1f);
                    Debug.Assert(result >= 0f && result <= 1f);
                    return result;
                }
            }
            return 0f;
        }

        /// <summary>
        /// Returns 1 if ctx.Context was detected visually (Visual bit set in Modalities) in ctx.Self's TargetMemory,
        /// else 0. Returns 0 if ctx.Context is not in TargetMemory or TargetMemory is absent.
        /// </summary>
        [UtilityInput("HasLineOfSight")]
        public static float HasLineOfSight(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<TargetMemory>(ctx.Self)) return 0f;
            ref readonly var mem = ref ctx.Repo.GetComponentRO<TargetMemory>(ctx.Self);
            long targetId = (long)ctx.Context.PackedValue;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.EntityIds[i] == targetId)
                {
                    float result = (mem.Modalities[i] & (byte)SensorModality.Visual) != 0 ? 1f : 0f;
                    Debug.Assert(result >= 0f && result <= 1f);
                    return result;
                }
            }
            return 0f;
        }

        /// <summary>
        /// Returns 1 if ctx.Self's TargetMemory has Count &gt; 0, else 0.
        /// Returns 0 if TargetMemory is absent.
        /// </summary>
        [UtilityInput("HaveLiveTarget")]
        public static float HaveLiveTarget(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<TargetMemory>(ctx.Self)) return 0f;
            ref readonly var mem = ref ctx.Repo.GetComponentRO<TargetMemory>(ctx.Self);
            float result = mem.Count > 0 ? 1f : 0f;
            Debug.Assert(result >= 0f && result <= 1f);
            return result;
        }

        /// <summary>
        /// Returns the ratio of total enemy threat score to (selfHealthFraction * MaxTrackedTargets),
        /// clamped to [0,1]. Returns 0 if TargetMemory is absent, Count is 0, or denominator is 0.
        /// </summary>
        [UtilityInput("EnemyStrengthRatio")]
        public static float EnemyStrengthRatio(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<TargetMemory>(ctx.Self)) return 0f;
            ref readonly var mem = ref ctx.Repo.GetComponentRO<TargetMemory>(ctx.Self);
            if (mem.Count == 0) return 0f;

            float threatSum = 0f;
            for (int i = 0; i < mem.Count; i++)
                threatSum += mem.ThreatScores[i];

            float selfHealthFraction = 0f;
            if (ctx.Repo.HasComponent<Health>(ctx.Self))
            {
                ref readonly var h = ref ctx.Repo.GetComponentRO<Health>(ctx.Self);
                if (h.Max > 0f)
                    selfHealthFraction = Math.Clamp(h.Current / h.Max, 0f, 1f);
            }

            float denominator = selfHealthFraction * PerceptionConstants.MaxTrackedTargets;
            if (denominator <= 0f) return 0f;

            float result = Math.Clamp(threatSum / denominator, 0f, 1f);
            Debug.Assert(result >= 0f && result <= 1f);
            return result;
        }

        // ── Group C: EQS ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the top-result score from the EQS sensor child matching ctx.Params.BlueprintId.
        /// Returns 0 if no matching sensor, buffer not ready, or buffer is empty.
        /// </summary>
        [UtilityInput("EqsTopScore")]
        public static float EqsTopScore(in UtilityInputCtx ctx)
        {
            if (!TryFindEqsChild(ctx.Repo, ctx.Self, ctx.Params.BlueprintId, out var child)) return 0f;
            ref readonly var buf = ref ctx.Repo.GetComponentRO<EqsCognitiveBuffer>(child);
            if (!buf.IsReady || buf.Count == 0) return 0f;
            float result = Math.Clamp(buf.GetTop().Score, 0f, 1f);
            Debug.Assert(result >= 0f && result <= 1f);
            return result;
        }

        /// <summary>
        /// Returns (float)Count / 16 for the EQS sensor child matching ctx.Params.BlueprintId.
        /// Returns 0 if no matching sensor or buffer not ready.
        /// </summary>
        [UtilityInput("EqsResultCount")]
        public static float EqsResultCount(in UtilityInputCtx ctx)
        {
            if (!TryFindEqsChild(ctx.Repo, ctx.Self, ctx.Params.BlueprintId, out var child)) return 0f;
            ref readonly var buf = ref ctx.Repo.GetComponentRO<EqsCognitiveBuffer>(child);
            if (!buf.IsReady) return 0f;
            float result = Math.Clamp((float)buf.Count / 16f, 0f, 1f);
            Debug.Assert(result >= 0f && result <= 1f);
            return result;
        }

        // ── Group D: assignment / misc ───────────────────────────────────────────

        /// <summary>
        /// Returns 1 if ctx.Self is assigned to ctx.Context in the squad leader's ThreatMatrixAssignmentState.
        /// Returns 1 (neutral pass) if ctx.Self has no UnitSubordinate, the commander has no Blackboard1024
        /// or UnitRoster, the member index is not found, or no assignment has been made yet (handle==0).
        /// Returns 0 only when an explicit non-zero assignment exists and it does not match ctx.Context.
        /// </summary>
        [UtilityInput("IsAssignedTarget")]
        public static float IsAssignedTarget(in UtilityInputCtx ctx)
        {
            var repo = ctx.Repo;
            if (!repo.HasComponent<UnitSubordinate>(ctx.Self)) return 1f;
            ref readonly var sub = ref repo.GetComponentRO<UnitSubordinate>(ctx.Self);
            var commander = sub.Commander;
            if (!repo.HasComponent<Blackboard1024>(commander) || !repo.HasComponent<UnitRoster>(commander)) return 1f;
            ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
            int idx = UnitRoster.IndexOf(ref roster, (long)ctx.Self.PackedValue);
            if (idx < 0) return 1f;
            ref var bb = ref repo.GetComponentRW<Blackboard1024>(commander);
            ref var state = ref ThreatMatrixAssignmentState.Project(ref bb);
            long assignedHandle = state.GetAssignedTarget(idx);
            if (assignedHandle == 0L) return 1f;
            float result = assignedHandle == (long)ctx.Context.PackedValue ? 1f : 0f;
            Debug.Assert(result >= 0f && result <= 1f);
            return result;
        }

        /// <summary>
        /// Phase 1 stub returning 0.
        /// Phase 2 will scan nearby entities for friendly units advancing toward ctx.Context.
        /// </summary>
        [UtilityInput("AllyAdvancingNearby")]
        public static float AllyAdvancingNearby(in UtilityInputCtx ctx)
        {
            // Phase 2 pending: requires spatial query for nearby advancing allies.
            return 0f;
        }

        /// <summary>
        /// Returns ctx.Params.MaxRange as a constant value in [0,1].
        /// Useful for injecting a design-time constant into a consideration chain.
        /// </summary>
        [UtilityInput("Constant")]
        public static float Constant(in UtilityInputCtx ctx)
        {
            float result = Math.Clamp(ctx.Params.MaxRange, 0f, 1f);
            Debug.Assert(result >= 0f && result <= 1f);
            return result;
        }

        /// <summary>
        /// Returns a score based on how well the target (ctx.Context) falls within the weapon's effective range.
        /// Finds the child entity whose WeaponMountInfo.MountIndex matches ctx.Params.MountIndex and
        /// whose PartMetadata.ParentEntity is ctx.Self (or ctx.Self itself when it carries WeaponMountInfo).
        /// Returns distance / effectiveRange (unclamped; a Bell curve on the caller side handles both sides).
        /// Returns 0 if no matching mount is found or positions are absent.
        /// </summary>
        [UtilityInput("WeaponRangeBandFit")]
        public static float WeaponRangeBandFit(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<Position>(ctx.Self) || !ctx.Repo.HasComponent<Position>(ctx.Context)) return 0f;

            if (!TryFindMountChild(ctx.Repo, ctx.Self, ctx.Params.MountIndex, out var mountChild)) return 0f;

            ref readonly var mi      = ref ctx.Repo.GetComponentRO<WeaponMountInfo>(mountChild);
            ref readonly var selfPos = ref ctx.Repo.GetComponentRO<Position>(ctx.Self);
            ref readonly var ctxPos  = ref ctx.Repo.GetComponentRO<Position>(ctx.Context);
            float effectiveRange     = mi.EffectiveRange;
            if (effectiveRange <= 0f) return 0f;

            float distance = Vector3.Distance(selfPos.Value, ctxPos.Value);
            float result   = distance / effectiveRange;
            return result;
        }

        /// <summary>
        /// Phase 1: delegates to <see cref="WeaponRangeBandFit"/>.
        /// Phase 2+ will incorporate armor class and target type modifiers.
        /// </summary>
        [UtilityInput("WeaponEffectivenessVsTarget")]
        public static float WeaponEffectivenessVsTarget(in UtilityInputCtx ctx)
            => WeaponRangeBandFit(ctx);

        // ── RegisterAll ──────────────────────────────────────────────────────────

        /// <summary>
        /// Registers all 17 Phase 1 standard input readers into <see cref="UtilityInputRegistrar"/>.
        /// Call once at application startup before any scoring pass.
        /// </summary>
        public static void RegisterAll()
        {
            UtilityInputReaderStore.Register(StandardInputIds.AmmoFraction,             &AmmoFraction);
            UtilityInputReaderStore.Register(StandardInputIds.WeaponHasAmmo,            &WeaponHasAmmo);
            UtilityInputReaderStore.Register(StandardInputIds.WeaponReadiness,          &WeaponReadiness);
            UtilityInputReaderStore.Register(StandardInputIds.HealthFraction,           &HealthFraction);
            UtilityInputReaderStore.Register(StandardInputIds.ContactHealthFraction,    &ContactHealthFraction);
            UtilityInputReaderStore.Register(StandardInputIds.DistanceToContext,        &DistanceToContext);
            UtilityInputReaderStore.Register(StandardInputIds.ContactThreatLevel,       &ContactThreatLevel);
            UtilityInputReaderStore.Register(StandardInputIds.HasLineOfSight,           &HasLineOfSight);
            UtilityInputReaderStore.Register(StandardInputIds.HaveLiveTarget,           &HaveLiveTarget);
            UtilityInputReaderStore.Register(StandardInputIds.EnemyStrengthRatio,       &EnemyStrengthRatio);
            UtilityInputReaderStore.Register(StandardInputIds.EqsTopScore,              &EqsTopScore);
            UtilityInputReaderStore.Register(StandardInputIds.EqsResultCount,           &EqsResultCount);
            UtilityInputReaderStore.Register(StandardInputIds.IsAssignedTarget,         &IsAssignedTarget);
            UtilityInputReaderStore.Register(StandardInputIds.AllyAdvancingNearby,      &AllyAdvancingNearby);
            UtilityInputReaderStore.Register(StandardInputIds.Constant,                 &Constant);
            UtilityInputReaderStore.Register(StandardInputIds.WeaponRangeBandFit,       &WeaponRangeBandFit);
            UtilityInputReaderStore.Register(StandardInputIds.WeaponEffectivenessVsTarget, &WeaponEffectivenessVsTarget);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Finds the first child entity of <paramref name="owner"/> whose EqsSensor.BlueprintId
        /// matches <paramref name="blueprintId"/>.
        /// </summary>
        private static bool TryFindEqsChild(EntityRepository repo, Entity owner, uint blueprintId, out Entity child)
        {
            var query = repo.Query().With<EqsSensor>().With<PartMetadata>().Build();
            foreach (var e in query)
            {
                ref readonly var pm = ref repo.GetComponentRO<PartMetadata>(e);
                if (!pm.ParentEntity.Equals(owner)) continue;
                ref readonly var sensor = ref repo.GetComponentRO<EqsSensor>(e);
                if (sensor.BlueprintId != blueprintId) continue;
                child = e;
                return true;
            }
            child = default;
            return false;
        }

        /// <summary>
        /// Finds the first child entity of <paramref name="owner"/> whose WeaponMountInfo.MountIndex
        /// matches <paramref name="mountIndex"/>. If <paramref name="owner"/> itself carries
        /// <see cref="WeaponMountInfo"/>, it is returned directly (self-mount case).
        /// </summary>
        private static bool TryFindMountChild(EntityRepository repo, Entity owner, int mountIndex, out Entity child)
        {
            // Self-mount: the owner entity itself is the weapon mount.
            if (repo.HasComponent<WeaponMountInfo>(owner))
            {
                child = owner;
                return true;
            }
            // Otherwise search child entities.
            var query = repo.Query().With<WeaponMountInfo>().With<PartMetadata>().Build();
            foreach (var e in query)
            {
                ref readonly var pm = ref repo.GetComponentRO<PartMetadata>(e);
                if (!pm.ParentEntity.Equals(owner)) continue;
                ref readonly var mi = ref repo.GetComponentRO<WeaponMountInfo>(e);
                if (mi.MountIndex != mountIndex) continue;
                child = e;
                return true;
            }
            child = default;
            return false;
        }
    }
}
