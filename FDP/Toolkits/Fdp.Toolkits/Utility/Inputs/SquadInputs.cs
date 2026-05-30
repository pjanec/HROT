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
            UtilityInputReaderStore.Register(SquadInputIds.SquadAmmoRollup,         &SquadAmmoRollup);
            UtilityInputReaderStore.Register(SquadInputIds.ActiveFeatureThreatRating, &ActiveFeatureThreatRating);
            UtilityInputReaderStore.Register(SquadInputIds.ActiveFeatureKindIs,     &ActiveFeatureKindIs);
            UtilityInputReaderStore.Register(SquadInputIds.SquadPoolThreatAggregate, &SquadPoolThreatAggregate);
            UtilityInputReaderStore.Register(SquadInputIds.AssignedRole,             &AssignedRole);
            UtilityInputReaderStore.Register(SquadInputIds.AssignedSlot,             &AssignedSlot);
        }

        /// <summary>
        /// Returns 1f if the squad commander's merged contact pool contains ctx.Context,
        /// else 0f.
        /// <para>
        /// ctx.Self must have a <see cref="UnitSubordinate"/> pointing at a commander with a
        /// <see cref="Blackboard1024"/>. Returns 0f if any prerequisite is missing.
        /// </para>
        /// </summary>
        [UtilityInput("SquadKnowsContact")]
        public static float SquadKnowsContact(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<UnitSubordinate>(ctx.Self)) return 0f;
            ref readonly var sub = ref ctx.Repo.GetComponentRO<UnitSubordinate>(ctx.Self);
            var commander = sub.Commander;
            if (commander.Equals(Entity.Null)) return 0f;
            if (!ctx.Repo.HasComponent<Blackboard1024>(commander)) return 0f;

            ref var bb = ref Unsafe.AsRef(in ctx.Repo.GetComponentRO<Blackboard1024>(commander));
            ref readonly var state = ref SquadCognitiveState.Project(ref bb);

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
        /// Returns the threat score for ctx.Context from the squad commander's merged contact pool,
        /// clamped to [0, 1]. Returns 0f if the contact is not in the pool or any prerequisite
        /// is missing.
        /// </summary>
        [UtilityInput("SquadContactThreatLevel")]
        public static float SquadContactThreatLevel(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<UnitSubordinate>(ctx.Self)) return 0f;
            ref readonly var sub = ref ctx.Repo.GetComponentRO<UnitSubordinate>(ctx.Self);
            var commander = sub.Commander;
            if (commander.Equals(Entity.Null)) return 0f;
            if (!ctx.Repo.HasComponent<Blackboard1024>(commander)) return 0f;

            ref var bb = ref Unsafe.AsRef(in ctx.Repo.GetComponentRO<Blackboard1024>(commander));
            ref readonly var state = ref SquadCognitiveState.Project(ref bb);

            long candidateId = (long)ctx.Context.PackedValue;
            var span = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref Unsafe.AsRef(in state.Contacts.Contacts)), 16);

            for (int i = 0; i < state.Contacts.Count; i++)
            {
                if (span[i].EntityId == candidateId)
                    return Math.Clamp(span[i].ThreatScore, 0f, 1f);
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
            if (!ctx.Repo.HasComponent<Blackboard1024>(ctx.Self)) return 1f;

            ref readonly var roster = ref ctx.Repo.GetComponentRO<UnitRoster>(ctx.Self);
            float sumCurrent = 0f;
            float sumMax     = 0f;

            for (int m = 0; m < roster.Count; m++)
            {
                var member = new Entity((ulong)roster.SubordinateEntities[m]);
                if (!ctx.Repo.HasComponent<Health>(member)) continue;
                ref readonly var h = ref ctx.Repo.GetComponentRO<Health>(member);
                sumCurrent += h.Current;
                sumMax     += h.Max;
            }

            if (sumMax <= 0f) return 1f;
            return Math.Clamp(sumCurrent / sumMax, 0f, 1f);
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
                var member = new Entity((ulong)roster.SubordinateEntities[m]);
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
            if (!ctx.Repo.HasComponent<Blackboard1024>(ctx.Self)) return 0f;
            ref var bb = ref ctx.Repo.GetComponentRW<Blackboard1024>(ctx.Self);
            ref readonly var state = ref SquadCognitiveState.Project(ref bb);
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
            if (!ctx.Repo.HasComponent<Blackboard1024>(ctx.Self)) return 0f;
            ref var bb = ref ctx.Repo.GetComponentRW<Blackboard1024>(ctx.Self);
            ref readonly var state = ref SquadCognitiveState.Project(ref bb);
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
        /// Returns 0f when ctx.Self has no <see cref="Blackboard1024"/>.
        /// </summary>
        [UtilityInput("SquadPoolThreatAggregate")]
        public static float SquadPoolThreatAggregate(in UtilityInputCtx ctx)
        {
            if (!ctx.Repo.HasComponent<Blackboard1024>(ctx.Self)) return 0f;
            ref var bb = ref ctx.Repo.GetComponentRW<Blackboard1024>(ctx.Self);
            ref readonly var state = ref SquadCognitiveState.Project(ref bb);
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
        /// with a <see cref="Blackboard1024"/> and <see cref="UnitRoster"/>.
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
            if (!ctx.Repo.HasComponent<Blackboard1024>(commander)) return 0f;
            if (!ctx.Repo.HasComponent<UnitRoster>(commander)) return 0f;

            ref readonly var roster = ref ctx.Repo.GetComponentRO<UnitRoster>(commander);
            long selfPacked = (long)ctx.Self.PackedValue;
            int memberIndex = -1;
            for (int m = 0; m < roster.Count; m++)
            {
                if (roster.SubordinateEntities[m] == selfPacked) { memberIndex = m; break; }
            }
            if (memberIndex < 0) return 0f;

            ref var bb = ref Unsafe.AsRef(in ctx.Repo.GetComponentRO<Blackboard1024>(commander));
            ref readonly var state = ref SquadCognitiveState.Project(ref bb);

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
        /// with a <see cref="Blackboard1024"/> and <see cref="UnitRoster"/>.
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
            if (!ctx.Repo.HasComponent<Blackboard1024>(commander)) return 0f;
            if (!ctx.Repo.HasComponent<UnitRoster>(commander)) return 0f;

            ref readonly var roster = ref ctx.Repo.GetComponentRO<UnitRoster>(commander);
            long selfPacked = (long)ctx.Self.PackedValue;
            int memberIndex = -1;
            for (int m = 0; m < roster.Count; m++)
            {
                if (roster.SubordinateEntities[m] == selfPacked) { memberIndex = m; break; }
            }
            if (memberIndex < 0) return 0f;

            ref var bb = ref Unsafe.AsRef(in ctx.Repo.GetComponentRO<Blackboard1024>(commander));
            ref readonly var state = ref SquadCognitiveState.Project(ref bb);

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
