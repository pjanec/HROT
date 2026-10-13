using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.DangerArea;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// FNV-1a-16 identifiers for squad-tier Utility AI input readers.
    /// Each constant is computed as <c>(ushort)(Fnv1a32(name) &amp; 0xFFFF)</c>
    /// where Fnv1a32 uses basis=2166136261 and prime=16777619.
    /// </summary>
    public static class SquadInputIds
    {
        // FNV-1a-32("SquadKnowsContact") & 0xFFFF
        public const ushort SquadKnowsContact       = 0xBA51;
        // FNV-1a-32("SquadContactThreatLevel") & 0xFFFF
        public const ushort SquadContactThreatLevel  = 0x2457;
        // FNV-1a-32("SquadStrengthRatio") & 0xFFFF
        public const ushort SquadStrengthRatio       = 0x6EDF;
        // FNV-1a-32("SquadEnemyStrengthRatio") & 0xFFFF — ⭐ CE-2123 S2
        public const ushort SquadEnemyStrengthRatio  = 0x1349;
        // FNV-1a-32("SquadAmmoRollup") & 0xFFFF
        public const ushort SquadAmmoRollup          = 0x8501;
        // FNV-1a-32("ActiveFeatureThreatRating") & 0xFFFF
        public const ushort ActiveFeatureThreatRating = 0xE922;
        // FNV-1a-32("ActiveFeatureKindIs") & 0xFFFF
        public const ushort ActiveFeatureKindIs      = 0x6679;
        // FNV-1a-32("SquadPoolThreatAggregate") & 0xFFFF
        public const ushort SquadPoolThreatAggregate = 0x0426;
        // FNV-1a-32("AssignedRole") & 0xFFFF
        public const ushort AssignedRole             = 0x3FD1;
        // FNV-1a-32("AssignedSlot") & 0xFFFF
        public const ushort AssignedSlot             = 0x8BC9;
        // FNV-1a-32("CasualtyUrgency") & 0xFFFF — ⭐ CE-2152 C-c R-d
        public const ushort CasualtyUrgency          = 0x631E;
        // FNV-1a-32("CasualtyExposure") & 0xFFFF — ⭐ CE-2152 C-c R-d
        public const ushort CasualtyExposure         = 0x813A;
        // FNV-1a-32("SquadQuietSeconds") & 0xFFFF — ⭐ CE-2152 C-c R-d
        public const ushort SquadQuietSeconds        = 0xCA98;
    }

    /// <summary>
    /// Squad-tier Utility AI input readers.
    /// Register all readers via <see cref="RegisterAll"/>.
    /// </summary>
    public static unsafe class SquadInputs
    {
        /// <summary>
        /// Registers all squad input readers into <see cref="UtilityInputReaderStore"/>.
        /// Call once at application startup before any scoring pass.
        /// </summary>
        public static void RegisterAll()
        {
            UtilityInputReaderStore.Register(SquadInputIds.SquadKnowsContact,      &SquadKnowsContact);
            UtilityInputReaderStore.Register(SquadInputIds.SquadContactThreatLevel, &SquadContactThreatLevel);
            UtilityInputReaderStore.Register(SquadInputIds.SquadStrengthRatio,      &SquadStrengthRatio);
            UtilityInputReaderStore.Register(SquadInputIds.SquadEnemyStrengthRatio, &SquadEnemyStrengthRatio);
            UtilityInputReaderStore.Register(SquadInputIds.SquadAmmoRollup,         &SquadAmmoRollup);
            UtilityInputReaderStore.Register(SquadInputIds.ActiveFeatureThreatRating, &ActiveFeatureThreatRating);
            UtilityInputReaderStore.Register(SquadInputIds.ActiveFeatureKindIs,     &ActiveFeatureKindIs);
            UtilityInputReaderStore.Register(SquadInputIds.SquadPoolThreatAggregate, &SquadPoolThreatAggregate);
            UtilityInputReaderStore.Register(SquadInputIds.AssignedRole,             &AssignedRole);
            UtilityInputReaderStore.Register(SquadInputIds.AssignedSlot,             &AssignedSlot);
            UtilityInputReaderStore.Register(SquadInputIds.CasualtyUrgency,          &CasualtyUrgency);
            UtilityInputReaderStore.Register(SquadInputIds.CasualtyExposure,         &CasualtyExposure);
            UtilityInputReaderStore.Register(SquadInputIds.SquadQuietSeconds,        &SquadQuietSeconds);
        }

        // ── ⭐ CE-2152 C-c (R-d, Squad Tactics §7.5.4) — the rescue decision's inputs. ctx.Self is the COMMANDER. ────────────

        /// <summary>A casualty with this long or more to live counts as "fresh" (s) — the design's <c>BleedOutSeconds</c>.</summary>
        public const float BleedOutSeconds = 120f;

        /// <summary>How long without the squad under fire counts as fully quiet (s).</summary>
        public const float QuietSeconds = 10f;

        /// <summary>A casualty with less than this to live starts lifting <see cref="SquadQuietSeconds"/> (s), fully at half of it: a
        /// dying man is worth some risk.</summary>
        public const float DyingSeconds = 60f;

        /// <summary>
        /// ⭐ How urgent the WORST casualty still to care for is: 0 with none; 0.8 for a fresh one (≥ <see cref="BleedOutSeconds"/> to live)
        /// rising to 1 as he bleeds out. ⭐ The floor is the calibration, MEASURED (§7.5.7): at 0.6 a quiet squad with a hidden casualty
        /// scored Rescue 0.883 against a strong squad's Assault 0.888; at 0.8 Rescue scores ≈ 0.95.
        /// </summary>
        [UtilityInput("CasualtyUrgency")]
        public static float CasualtyUrgency(in UtilityInputCtx ctx)
        {
            if (SquadCasualties.Worst(ctx.Repo, ctx.Self, out _, out float left) < 0) return 0f;
            float fresh = Math.Clamp(left / BleedOutSeconds, 0f, 1f);
            return 0.8f + 0.2f * (1f - fresh);
        }

        /// <summary>
        /// ⭐ How many of the squad's IDENTIFIED pooled contacts can see the worst casualty, as a share (0 = none, 1 = all): the
        /// terrain sight test where this node holds the terrain (each contact's standing eye → the man lying, +0.3 m), else — no
        /// terrain here — a contact within <see cref="ExposureFallbackMetres"/> counts as seeing him. ⭐ FOG: the squad's pool, never
        /// the truth — an unseen sniper exposes nothing here. 0 with no casualty or no contact.
        /// </summary>
        [UtilityInput("CasualtyExposure")]
        public static float CasualtyExposure(in UtilityInputCtx ctx)
        {
            if (SquadCasualties.Worst(ctx.Repo, ctx.Self, out var man, out _) < 0 || !ctx.Repo.HasComponent<SimTransform>(man)) return 0f;
            if (!ctx.Repo.HasComponent<SquadCognitiveState>(ctx.Self)) return 0f;
            ref readonly var state = ref ctx.Repo.GetComponentRO<SquadCognitiveState>(ctx.Self);
            var span = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref Unsafe.AsRef(in state.Contacts.Contacts)), 16);
            var at = ctx.Repo.GetComponentRO<SimTransform>(man).Position + new System.Numerics.Vector3(0f, 0f, 0.3f);
            var sight = Fdp.Toolkit.Spatial.Eqs.EqsTerrainSight.Sight(ctx.Repo, null);
            int contacts = 0, seeing = 0;
            for (int i = 0; i < state.Contacts.Count && i < 16; i++)
            {
                if ((span[i].Flags & SquadContact.AnonymousFlag) != 0 || span[i].EntityId <= 0) continue;
                contacts++;
                var eye = new System.Numerics.Vector3(span[i].PositionX, span[i].PositionY, span[i].PositionZ + 1.7f);
                bool sees = sight != null
                    ? sight.HasLineOfSight(eye, at)
                    : System.Numerics.Vector3.Distance(eye, at) <= ExposureFallbackMetres;
                if (sees) seeing++;
            }
            return contacts == 0 ? 0f : (float)seeing / contacts;
        }

        /// <summary>Without terrain on this node, a contact this close to the casualty counts as seeing him (m).</summary>
        public const float ExposureFallbackMetres = 150f;

        /// <summary>
        /// ⭐ How QUIET the squad is: 0.2 right after it was under fire, rising to 1 after <see cref="QuietSeconds"/> without (never under
        /// fire = 1) — from the commander's <see cref="RecentSenses"/> (<c>SquadUnderFire</c>). ⭐ A casualty within
        /// <see cref="DyingSeconds"/> of bleeding out lifts it toward 1 (fully at half that) (light fire is accepted for a dying man; heavy AIMED fire on him is
        /// still caught by <see cref="CasualtyExposure"/>).
        /// </summary>
        [UtilityInput("SquadQuietSeconds")]
        public static float SquadQuietSeconds(in UtilityInputCtx ctx)
        {
            float quiet = 1f;
            if (ctx.Repo.HasComponent<RecentSenses>(ctx.Self)
                && ctx.Repo.GetComponentRO<RecentSenses>(ctx.Self).TryGetLast(Fdp.Toolkit.Perception.Events.SensorChange.SquadUnderFire, out double at))
            {
                double now = ctx.Repo.HasSingleton<GlobalTime>() ? ctx.Repo.GetSingleton<GlobalTime>().TotalTime : 0d;
                quiet = 0.2f + 0.8f * Math.Clamp((float)(now - at) / QuietSeconds, 0f, 1f);
            }
            if (SquadCasualties.Worst(ctx.Repo, ctx.Self, out _, out float left) >= 0 && left < DyingSeconds)
                quiet = Math.Max(quiet, Math.Clamp((DyingSeconds - left) / (DyingSeconds * 0.5f), 0f, 1f));
            return quiet;
        }

        /// <summary>
        /// Returns 1f if the squad commander's merged contact pool contains ctx.Context,
        /// else 0f.
        /// <para>
        /// ctx.Self must have a <see cref="UnitSubordinate"/> pointing at a commander with a
        /// <see cref="SquadCognitiveState"/>. Returns 0f if any prerequisite is missing.
        /// </para>
        /// </summary>
        [UtilityInput("SquadKnowsContact")]
        public static float SquadKnowsContact(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<UnitSubordinate>(ctx.Self)) return 0f;
            ref readonly var sub = ref ctx.Repo.GetComponentRO<UnitSubordinate>(ctx.Self);
            var commander = sub.Commander;
            if (commander.Equals(Entity.Null)) return 0f;
            if (!ctx.Repo.HasComponent<SquadCognitiveState>(commander)) return 0f;

            ref readonly var state = ref ctx.Repo.GetComponentRO<SquadCognitiveState>(commander);

            long candidateId = (long)ctx.Context.PackedValue;
            var span = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref Unsafe.AsRef(in state.Contacts.Contacts)), 16);

            for (int i = 0; i < state.Contacts.Count; i++)
            {
                if (span[i].EntityId == candidateId) return 1f;
            }
            return 0f;
        }

        /// <summary>
        /// ⭐ CE-3054 — the threat of ctx.Context from the squad commander's merged contact pool: danger × freshness, as
        /// <c>StandardInputs.ContactThreatLevel</c>. Returns 0f if the contact is not in the pool or any prerequisite
        /// is missing. ⛔ SUPERSEDED: the pooled score clamped to [0, 1] (it saturates at 500).
        /// </summary>
        [UtilityInput("SquadContactThreatLevel")]
        public static float SquadContactThreatLevel(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<UnitSubordinate>(ctx.Self)) return 0f;
            ref readonly var sub = ref ctx.Repo.GetComponentRO<UnitSubordinate>(ctx.Self);
            var commander = sub.Commander;
            if (commander.Equals(Entity.Null)) return 0f;
            if (!ctx.Repo.HasComponent<SquadCognitiveState>(commander)) return 0f;

            ref readonly var state = ref ctx.Repo.GetComponentRO<SquadCognitiveState>(commander);

            long candidateId = (long)ctx.Context.PackedValue;
            var span = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref Unsafe.AsRef(in state.Contacts.Contacts)), 16);

            for (int i = 0; i < state.Contacts.Count; i++)
            {
                if (span[i].EntityId == candidateId)
                    // ⭐ CE-3054 — as ContactThreatLevel: danger (judged now) × freshness (the pooled score is a freshness score).
                    return Fdp.Toolkit.Perception.ThreatDanger.Of(ctx.Repo, ctx.Self, ctx.Context)
                         * Fdp.Toolkit.Perception.ThreatFreshness.Of(span[i].ThreatScore);
            }
            return 0f;
        }

        /// <summary>
        /// Returns the ratio of current squad health to max squad health.
        /// Walks ctx.Self's <see cref="UnitRoster"/>; accumulates Health.Current / Health.Max
        /// for members that have a <see cref="Health"/> component.
        /// Returns 1f if no members have Health (full strength assumed).
        /// </summary>
        [UtilityInput("SquadStrengthRatio")]
        public static float SquadStrengthRatio(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<UnitRoster>(ctx.Self)) return 1f;
            if (!ctx.Repo.HasComponent<SquadCognitiveState>(ctx.Self)) return 1f;

            ref readonly var roster = ref ctx.Repo.GetComponentRO<UnitRoster>(ctx.Self);
            float sumCurrent = 0f;
            float sumMax     = 0f;

            for (int m = 0; m < roster.Count; m++)
            {
                var member = roster.SubordinateEntities[m];
                if (!ctx.Repo.HasComponent<Health>(member)) continue;
                ref readonly var h = ref ctx.Repo.GetComponentRO<Health>(member);
                sumCurrent += h.Current;
                sumMax     += h.Max;
            }

            if (sumMax <= 0f) return 1f;
            return Math.Clamp(sumCurrent / sumMax, 0f, 1f);
        }

        /// <summary>
        /// ⭐ <c>CE-2123</c> S2 — the ENEMY's share of the strength in play, for the SQUAD: Σ danger of every pooled contact ÷ (that
        /// sum + Σ the live members' own strength). The squad twin of <c>EnemyStrengthRatio</c> (same <c>ThreatDanger</c> scale; a
        /// HEARD pooled contact counts as armed — the pool keeps no sound class). One armed enemy vs four healthy armed members =
        /// 0.2. 0 with an empty pool. ctx.Self is the COMMANDER. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §4.5 S2-D.
        /// </summary>
        [UtilityInput("SquadEnemyStrengthRatio")]
        public static float SquadEnemyStrengthRatio(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<SquadCognitiveState>(ctx.Self) || !ctx.Repo.HasComponent<UnitRoster>(ctx.Self)) return 0f;
            ref readonly var state = ref ctx.Repo.GetComponentRO<SquadCognitiveState>(ctx.Self);
            if (state.Contacts.Count == 0) return 0f;
            var span = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref Unsafe.AsRef(in state.Contacts.Contacts)), 16);

            float enemy = 0f;
            for (int i = 0; i < state.Contacts.Count; i++)
                enemy += (span[i].Flags & SquadContact.AnonymousFlag) != 0
                    ? 1f
                    : Fdp.Toolkit.Perception.ThreatDanger.Of(ctx.Repo, ctx.Self, new Entity((ulong)span[i].EntityId));

            float own = 0f;
            ref readonly var roster = ref ctx.Repo.GetComponentRO<UnitRoster>(ctx.Self);
            for (int m = 0; m < roster.Count; m++)
            {
                var member = roster.SubordinateEntities[m];
                if (!ctx.Repo.IsAlive(member) || Fdp.Toolkit.Perception.ThreatDanger.IsOutOfTheFight(ctx.Repo, member)) continue;
                own += Fdp.Toolkit.Perception.ThreatDanger.OwnStrength(ctx.Repo, member);
            }

            float total = enemy + own;
            return total <= 0f ? 0f : Math.Clamp(enemy / total, 0f, 1f);
        }

        /// <summary>
        /// Returns the ratio of total ammo to total max ammo across squad members.
        /// Walks ctx.Self's <see cref="UnitRoster"/>; accumulates WeaponState.Ammo /
        /// WeaponState.MaxAmmo for members that have a <see cref="WeaponState"/> component
        /// with a positive MaxAmmo.
        /// Returns 1f if no members have a positive MaxAmmo (fully loaded assumed).
        /// </summary>
        [UtilityInput("SquadAmmoRollup")]
        public static float SquadAmmoRollup(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<UnitRoster>(ctx.Self)) return 1f;

            ref readonly var roster = ref ctx.Repo.GetComponentRO<UnitRoster>(ctx.Self);
            float sumAmmo    = 0f;
            float sumMaxAmmo = 0f;

            for (int m = 0; m < roster.Count; m++)
            {
                var member = roster.SubordinateEntities[m];
                if (!ctx.Repo.HasComponent<WeaponState>(member)) continue;
                ref readonly var ws = ref ctx.Repo.GetComponentRO<WeaponState>(member);
                if (ws.MaxAmmo <= 0) continue;
                sumAmmo    += Math.Max(ws.Ammo, 0);
                sumMaxAmmo += ws.MaxAmmo;
            }

            if (sumMaxAmmo <= 0f) return 1f;
            return Math.Clamp(sumAmmo / sumMaxAmmo, 0f, 1f);
        }

        /// <summary>
        /// Returns the ThreatRating of the active danger-area feature.
        /// Looks up state.ActiveFeatureId in ctx.Self's <see cref="DangerAreaCognitiveBuffer"/>.
        /// Returns 0f when ActiveFeatureId is 0, the buffer is absent, or the feature is not found.
        /// </summary>
        [UtilityInput("ActiveFeatureThreatRating")]
        public static float ActiveFeatureThreatRating(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<SquadCognitiveState>(ctx.Self)) return 0f;
            ref readonly var state = ref ctx.Repo.GetComponentRO<SquadCognitiveState>(ctx.Self);
            if (state.ActiveFeatureId == 0) return 0f;
            if (!ctx.Repo.HasComponent<DangerAreaCognitiveBuffer>(ctx.Self)) return 0f;
            ref readonly var buffer = ref ctx.Repo.GetComponentRO<DangerAreaCognitiveBuffer>(ctx.Self);
            var span = buffer.GetSpanRO();
            for (int i = 0; i < buffer.Count; i++)
            {
                if (span[i].FeatureId == state.ActiveFeatureId)
                    return Math.Clamp(span[i].ThreatRating, 0f, 1f);
            }
            return 0f;
        }

        /// <summary>
        /// Returns 1f if the active danger-area feature's Kind matches the kind encoded in
        /// ctx.Params.BlueprintId (low byte). Returns 0f otherwise or if prerequisites are missing.
        /// </summary>
        [UtilityInput("ActiveFeatureKindIs")]
        public static float ActiveFeatureKindIs(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<SquadCognitiveState>(ctx.Self)) return 0f;
            ref readonly var state = ref ctx.Repo.GetComponentRO<SquadCognitiveState>(ctx.Self);
            if (state.ActiveFeatureId == 0) return 0f;
            if (!ctx.Repo.HasComponent<DangerAreaCognitiveBuffer>(ctx.Self)) return 0f;
            ref readonly var buffer = ref ctx.Repo.GetComponentRO<DangerAreaCognitiveBuffer>(ctx.Self);
            var targetKind = (DangerAreaKind)(ctx.Params.BlueprintId & 0xFF);
            var span = buffer.GetSpanRO();
            for (int i = 0; i < buffer.Count; i++)
            {
                if (span[i].FeatureId == state.ActiveFeatureId)
                    return span[i].Kind == targetKind ? 1f : 0f;
            }
            return 0f;
        }

        /// <summary>
        /// Returns the aggregate threat across all contacts in the squad's contact pool,
        /// normalised to [0, 1] (max 16 contacts * 1.0f each = 16.0f).
        /// Returns 0f when ctx.Self has no <see cref="SquadCognitiveState"/>.
        /// </summary>
        [UtilityInput("SquadPoolThreatAggregate")]
        public static float SquadPoolThreatAggregate(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<SquadCognitiveState>(ctx.Self)) return 0f;
            ref readonly var state = ref ctx.Repo.GetComponentRO<SquadCognitiveState>(ctx.Self);
            var span = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref Unsafe.AsRef(in state.Contacts.Contacts)), 16);
            float sum = 0f;
            for (int i = 0; i < state.Contacts.Count; i++)
                sum += span[i].ThreatScore;
            return Math.Clamp(sum / 16f, 0f, 1f);
        }

        /// <summary>
        /// Returns 1f if the squad member's assigned role (from the commander's
        /// <see cref="SquadCognitiveState.Roles"/> array) matches the role id
        /// encoded in <c>ctx.Params.BlueprintId</c> (low byte), else 0f.
        /// <para>
        /// ctx.Self must have a <see cref="UnitSubordinate"/> pointing at a commander
        /// with a <see cref="SquadCognitiveState"/> and <see cref="UnitRoster"/>.
        /// Returns 0f if any prerequisite is missing or the member is not in the roster.
        /// </para>
        /// </summary>
        [UtilityInput("AssignedRole")]
        public static float AssignedRole(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<UnitSubordinate>(ctx.Self)) return 0f;
            ref readonly var sub = ref ctx.Repo.GetComponentRO<UnitSubordinate>(ctx.Self);
            var commander = sub.Commander;
            if (commander.Equals(Entity.Null)) return 0f;
            if (!ctx.Repo.HasComponent<SquadCognitiveState>(commander)) return 0f;
            if (!ctx.Repo.HasComponent<UnitRoster>(commander)) return 0f;

            ref readonly var roster = ref ctx.Repo.GetComponentRO<UnitRoster>(commander);
            int memberIndex = -1;
            for (int m = 0; m < roster.Count; m++)
            {
                if (roster.SubordinateEntities[m] == ctx.Self) { memberIndex = m; break; }
            }
            if (memberIndex < 0) return 0f;

            ref readonly var state = ref ctx.Repo.GetComponentRO<SquadCognitiveState>(commander);

            var roleSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<RoleAssignmentArray, RoleSlot>(
                    ref Unsafe.AsRef(in state.Roles)), 16);

            byte targetRoleId = (byte)(ctx.Params.BlueprintId & 0xFF);
            return roleSpan[memberIndex].RoleId == targetRoleId ? 1f : 0f;
        }

        /// <summary>
        /// Returns 1f if any squad slot assigned to the member's element matches
        /// the slot kind encoded in <c>ctx.Params.BlueprintId</c> (low byte), else 0f.
        /// <para>
        /// ctx.Self must have a <see cref="UnitSubordinate"/> pointing at a commander
        /// with a <see cref="SquadCognitiveState"/> and <see cref="UnitRoster"/>.
        /// Returns 0f if any prerequisite is missing or the member is not in the roster.
        /// </para>
        /// </summary>
        [UtilityInput("AssignedSlot")]
        public static float AssignedSlot(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<UnitSubordinate>(ctx.Self)) return 0f;
            ref readonly var sub = ref ctx.Repo.GetComponentRO<UnitSubordinate>(ctx.Self);
            var commander = sub.Commander;
            if (commander.Equals(Entity.Null)) return 0f;
            if (!ctx.Repo.HasComponent<SquadCognitiveState>(commander)) return 0f;
            if (!ctx.Repo.HasComponent<UnitRoster>(commander)) return 0f;

            ref readonly var roster = ref ctx.Repo.GetComponentRO<UnitRoster>(commander);
            int memberIndex = -1;
            for (int m = 0; m < roster.Count; m++)
            {
                if (roster.SubordinateEntities[m] == ctx.Self) { memberIndex = m; break; }
            }
            if (memberIndex < 0) return 0f;

            ref readonly var state = ref ctx.Repo.GetComponentRO<SquadCognitiveState>(commander);

            var elemSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<MemberElementIndexArray, byte>(
                    ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);
            byte memberElementIndex = elemSpan[memberIndex];

            var slotSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<SlotAssignmentArray, SlotState>(
                    ref Unsafe.AsRef(in state.Slots)), 12);

            byte targetSlotKind = (byte)(ctx.Params.BlueprintId & 0xFF);
            for (int s = 0; s < 12; s++)
            {
                if (slotSpan[s].ElementIndex == memberElementIndex
                    && slotSpan[s].SlotKind == targetSlotKind)
                    return 1f;
            }
            return 0f;
        }
    }
}
