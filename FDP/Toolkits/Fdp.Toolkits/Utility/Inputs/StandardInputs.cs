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
using Fdp.Toolkit.Squad;

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
        /// <summary>⭐ CE-3054 — how dangerous the context contact is (<see cref="Fdp.Toolkit.Perception.ThreatDanger"/>).</summary>
        public const ushort ContactDanger            = 0xF4D0;
        /// <summary>⭐ CE-3054 — how fresh the context contact is in the unit's memory.</summary>
        public const ushort ContactFreshness         = 0x73FA;

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
    /// Call <see cref="RegisterAll"/> once at startup to register all 19 readers.
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
        /// ⭐ CE-3054 C — the context contact's THREAT: its danger (judged now, <see cref="ThreatDanger"/>) × its freshness in
        /// ctx.Self's memory (<see cref="ThreatFreshness"/>). 0 when the contact is not remembered.
        /// ⛔ SUPERSEDED: the raw memory score clamped to [0,1] — the score saturates at 500, so every contact read 1 for ~44 s.
        /// </summary>
        [UtilityInput("ContactThreatLevel")]
        public static float ContactThreatLevel(in UtilityInputCtx ctx)
        {
            float freshness = ContactFreshness(ctx);
            if (freshness <= 0f) return 0f;
            float result = ThreatDanger.Of(ctx.Repo, ctx.Self, ctx.Context) * freshness;
            Debug.Assert(result >= 0f && result <= 1f);
            return result;
        }

        /// <summary>⭐ CE-3054 B — how dangerous the context contact is, in [0,1] (<see cref="ThreatDanger.Of"/>).</summary>
        [UtilityInput("ContactDanger")]
        public static float ContactDanger(in UtilityInputCtx ctx)
            => ThreatDanger.Of(ctx.Repo, ctx.Self, ctx.Context);

        /// <summary>
        /// ⭐ CE-3054 C — how fresh the context contact is in ctx.Self's memory, in [0,1] (1 = tracked long enough to
        /// saturate). 0 when it is not remembered.
        /// </summary>
        [UtilityInput("ContactFreshness")]
        public static float ContactFreshness(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<TargetMemory>(ctx.Self)) return 0f;
            ref readonly var mem = ref ctx.Repo.GetComponentRO<TargetMemory>(ctx.Self);
            long targetId = (long)ctx.Context.PackedValue;
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == targetId) return ThreatFreshness.Of(mem.ThreatScores[i]);
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
        /// ⭐ CE-3054 C — 1 when ctx.Self remembers a LIVE contact (tracked now, or fresh enough —
        /// <see cref="ThreatFreshness.IsLive"/>), else 0.
        /// ⛔ SUPERSEDED: <c>Count &gt; 0</c> — a contact seen once long ago kept an attack posture alive.
        /// </summary>
        [UtilityInput("HaveLiveTarget")]
        public static float HaveLiveTarget(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<TargetMemory>(ctx.Self)) return 0f;
            ref readonly var mem = ref ctx.Repo.GetComponentRO<TargetMemory>(ctx.Self);
            for (int i = 0; i < mem.Count; i++)
                if (ThreatFreshness.IsLive(ctx.Repo, ctx.Self, in mem, i)) return 1f;
            return 0f;
        }

        /// <summary>
        /// ⭐ CE-3054 C — the enemy's share of the strength in play: Σ danger of every REMEMBERED contact ÷ (that sum + the
        /// unit's own strength, <see cref="ThreatDanger.OwnStrength"/>). Freshness is NOT applied — hidden does not mean
        /// harmless (R-194, Decision Layer G1). One armed enemy vs a healthy armed unit = 0.5. 0 with nothing remembered.
        /// ⛔ SUPERSEDED: Σ raw scores ÷ (health × 16) — the scores DECAYED, so a hidden enemy made the unit braver.
        /// </summary>
        [UtilityInput("EnemyStrengthRatio")]
        public static float EnemyStrengthRatio(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<TargetMemory>(ctx.Self)) return 0f;
            ref readonly var mem = ref ctx.Repo.GetComponentRO<TargetMemory>(ctx.Self);
            if (mem.Count == 0) return 0f;

            float enemy = 0f;
            for (int i = 0; i < mem.Count; i++)
                enemy += ThreatDanger.Of(ctx.Repo, ctx.Self, new Entity((ulong)mem.EntityIds[i]));

            float total = enemy + ThreatDanger.OwnStrength(ctx.Repo, ctx.Self);
            float result = total <= 0f ? 0f : Math.Clamp(enemy / total, 0f, 1f);
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
            if (!repo.HasComponent<SquadCognitiveState>(commander) || !repo.HasComponent<UnitRoster>(commander)) return 1f;
            ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
            int idx = UnitRoster.IndexOf(ref roster, ctx.Self);
            if (idx < 0) return 1f;
            ref var state = ref repo.GetComponentRW<SquadCognitiveState>(commander).Assignment;
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
        /// Registers all 19 standard input readers into <see cref="UtilityInputRegistrar"/>.
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
            UtilityInputReaderStore.Register(StandardInputIds.ContactDanger,            &ContactDanger);
            UtilityInputReaderStore.Register(StandardInputIds.ContactFreshness,         &ContactFreshness);
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
        /// <summary>
        /// ⭐ <c>CE-2071</c> — the unit's sensor running <paramref name="blueprintId"/>, through the ONE lookup
        /// (<see cref="Fdp.Toolkit.Perception.Sensors.UnitSensors.OfTemplate"/>: the current run's own sensor, then the TKB's,
        /// then any other, lowest part id). 🔴 Was a private scan that took the FIRST matching child in query order, so with
        /// two sensors on one template it could read another run's stale results. Read only; never cached.
        /// </summary>
        private static bool TryFindEqsChild(EntityRepository repo, Entity owner, uint blueprintId, out Entity child)
        {
            child = Fdp.Toolkit.Perception.Sensors.UnitSensors.OfTemplate(repo, owner, blueprintId);
            return !child.IsNull && repo.HasComponent<EqsCognitiveBuffer>(child);
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
