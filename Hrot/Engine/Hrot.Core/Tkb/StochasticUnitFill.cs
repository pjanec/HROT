using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Definitions.Tkb;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐⭐ <c>CE-8016</c> (agg-behaviors' F-9; closes CE-8013 ① and CE-8008's personal weapons; SV-3, X-3) — a stochastic unit's STATE FROM ITS TKB
    /// COMPOSITE: the composite (<see cref="TkbCompositionDef"/>, nested platoon → squads → men) flattened to its LEAF slots — each person a
    /// <see cref="UnitSlot"/> (the role from its designation, else its first mount's category; health full; ammunition its basic load,
    /// <see cref="UnitAmmunition.BasicLoad"/>), each vehicle / weapon system a <see cref="UnitHolding"/> (count, its basic load × count, its
    /// mobility from <see cref="LocomotionDto"/>). ⭐ The leaf index IS <see cref="UnitSlot.CompositeSlot"/> + 1 and the key the split derives the
    /// child's identity from (SV-4) — <see cref="ChildType"/> inverts it for <see cref="BusResolutionSwitch"/>. ⭐ The SAME composite the
    /// physical units are built from (SV-3: the composite lists the PHYSICAL slots).
    /// </summary>
    public static class StochasticUnitFill
    {
        private const int MaxDepth = 6;

        /// <summary>One leaf of a composite: its type and the designation of the slot that holds it.</summary>
        public readonly record struct Leaf(TkbTypeId Type, TacticalDesignation Designation);

        /// <summary>The composite of <paramref name="unitType"/> flattened, in declaration order (empty when it has none).</summary>
        public static List<Leaf> Leaves(ITkbDatabase db, TkbTypeId unitType)
        {
            var list = new List<Leaf>();
            Expand(db, unitType, TacticalDesignation.Undefined, list, 0);
            return list;
        }

        private static void Expand(ITkbDatabase db, TkbTypeId type, TacticalDesignation des, List<Leaf> into, int depth)
        {
            if (!db.TryGetByType(type, out var t) || t == null) return;
            var comp = t.GetDescriptor<TkbCompositionDef>();
            if (comp == null || comp.Subordinates.Count == 0 || depth >= MaxDepth)
            {
                if (depth > 0) into.Add(new Leaf(type, des));   // the unit itself is never its own leaf
                return;
            }
            foreach (var slot in comp.Subordinates)
                for (int i = 0; i < slot.Count; i++)
                    Expand(db, slot.TkbType, slot.Designation, into, depth + 1);
        }

        /// <summary>Is <paramref name="type"/> a person (walks: <see cref="LocomotionClass.Pedestrian"/>, else a life form by its DIS kind)?</summary>
        public static bool IsPerson(ITkbDatabase db, TkbTypeId type)
        {
            if (!db.TryGetByType(type, out var t) || t == null) return false;
            var loco = t.GetDescriptor<LocomotionDto>();
            if (loco != null && loco.Class != LocomotionClass.Unknown) return loco.Class == LocomotionClass.Pedestrian;
            return t.DisType.Kind == 3;
        }

        /// <summary>The slot role of a person: from the slot's designation, else from his first mount's category.</summary>
        public static SlotRole RoleOf(ITkbDatabase db, in Leaf leaf)
        {
            switch (leaf.Designation)
            {
                case TacticalDesignation.Commander:
                case TacticalDesignation.SquadLeader:
                case TacticalDesignation.TeamLeader: return SlotRole.Leader;
                case TacticalDesignation.MachineGunner:
                case TacticalDesignation.AutomaticRifleman: return SlotRole.MachineGunner;
                case TacticalDesignation.Grenadier: return SlotRole.Grenadier;
                case TacticalDesignation.Marksman: return SlotRole.Marksman;
                case TacticalDesignation.AntiTank: return SlotRole.AntiTank;
                case TacticalDesignation.Medic: return SlotRole.Medic;
            }
            if (db.TryGetByType(leaf.Type, out var t) && t?.GetDescriptor<WeaponSuiteDto>() is { Mounts.Count: > 0 } suite)
                switch (suite.Mounts[0].Category)
                {
                    case WeaponCategory.MachineGun: return SlotRole.MachineGunner;
                    case WeaponCategory.Atgm: return SlotRole.AntiTank;
                    case WeaponCategory.Grenade: return SlotRole.Grenadier;
                }
            return SlotRole.Rifleman;
        }

        /// <summary>The echelon byte of a composite's <c>Echelon</c> name (<c>Fdp.Toolkit.Squad.Echelon</c>: 0 squad, 1 platoon, 2 company).</summary>
        public static byte EchelonOf(string? name) => name?.Trim().ToLowerInvariant() switch
        {
            "squad" or "team" or "section" => 0, "platoon" => 1, _ => 2,
        };

        /// <summary>
        /// ⭐ The unit's slot table and holdings from its composite. <paramref name="roles"/> (optional) collects role → soldier type, first seen
        /// wins — the personal weapon each role fires (<see cref="KillerVictimTkb.PersonalSoldiers"/>). Returns the persons the table could
        /// not hold (over <see cref="UnitSlotTable.Capacity"/>) — 0 normally.
        /// </summary>
        public static int Build(ITkbDatabase db, TkbTypeId unitType, out UnitSlotTable slots, out UnitHoldings holdings, Dictionary<SlotRole, TkbTypeId>? roles = null)
        {
            slots = new UnitSlotTable();
            holdings = new UnitHoldings();
            var leaves = Leaves(db, unitType);
            int dropped = 0;
            for (int k = 0; k < leaves.Count; k++)
            {
                var leaf = leaves[k];
                if (IsPerson(db, leaf.Type))
                {
                    if (slots.Count >= UnitSlotTable.Capacity || k >= 255) { dropped++; continue; }
                    var role = RoleOf(db, in leaf);
                    roles?.TryAdd(role, leaf.Type);
                    slots.Slots[slots.Count] = new UnitSlot
                    {
                        Role = role, Health = (Half)UnitSplits.SlotFullHealth, Stress = (Half)0f,
                        Ammo = (ushort)Math.Clamp(UnitAmmunition.BasicLoad(db, leaf.Type), 0, ushort.MaxValue),
                        CompositeSlot = (byte)(k + 1),
                    };
                    slots.Count++;
                    continue;
                }
                AddHolding(db, ref holdings, leaf.Type);
            }
            return dropped;
        }

        private static void AddHolding(ITkbDatabase db, ref UnitHoldings h, TkbTypeId type)
        {
            int basic = UnitAmmunition.BasicLoad(db, type);
            for (int i = 0; i < h.Count; i++)
                if (h.Items[i].Type == type)
                {
                    h.Items[i].Count++; h.Items[i].Operational++; h.Items[i].Ammo += (uint)Math.Max(0, basic);
                    return;
                }
            if (h.Count >= UnitHoldings.Capacity) return;
            db.TryGetByType(type, out var t);
            var loco = t?.GetDescriptor<LocomotionDto>()?.Class ?? LocomotionClass.Unknown;
            bool vehicle = loco is LocomotionClass.Wheeled or LocomotionClass.Tracked or LocomotionClass.Amphibious or LocomotionClass.Rotorcraft or LocomotionClass.FixedWing;
            h.Items[h.Count++] = new UnitHolding
            {
                Type = type, Count = 1, Operational = 1, Ammo = (uint)Math.Max(0, basic),
                Kind = vehicle ? HoldingKind.Vehicle : HoldingKind.Weapon,
                Mobility = loco == LocomotionClass.Tracked ? HoldingMobility.Tracked : vehicle ? HoldingMobility.Wheeled : HoldingMobility.NoMobility,
            };
        }

        /// <summary>⭐ CE-8013 ① — the TKB type of the unit's composite slot <paramref name="compositeSlot"/> (1-based, as filled), for
        /// <see cref="BusResolutionSwitch.ChildType"/>.</summary>
        public static TkbTypeId ChildType(ITkbDatabase db, TkbTypeId unitType, int compositeSlot)
        {
            var leaves = Leaves(db, unitType);
            int k = compositeSlot - 1;
            return k >= 0 && k < leaves.Count ? leaves[k].Type : default;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-8016</c> — fills an AUTHORED stochastic unit at spawn: a unit with <see cref="AggregateUnit"/> (stochastic), a TKB type, and no
    /// (or an empty) <see cref="UnitSlotTable"/> gets its slots and holdings from its composite, and its echelon if unset; the role → soldier
    /// map feeds <see cref="KillerVictimTkb.PersonalSoldiers"/> (first seen wins). Serial, the unit's owner only; a unit is filled once (it then
    /// has slots). 0 B once nothing is left to fill.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class StochasticUnitFillSystem : IEcsModuleSystem
    {
        private readonly bool _gateOnAuthority;
        private EntityQuery? _units;
        private EntityRepository? _queriesFor;
        private readonly Dictionary<SlotRole, TkbTypeId> _roles = new();

        public StochasticUnitFillSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<AggregateUnit>() || !repo.IsComponentTypeRegistered<UnitSlotTable>()
                || !repo.IsComponentTypeRegistered<UnitHoldings>() || !repo.IsComponentTypeRegistered<TkbIdentity>() || !repo.HasSingletonManaged<ITkbDatabase>()) return;
            if (!ReferenceEquals(_queriesFor, repo))
            {
                _units = repo.Query().With<AggregateUnit>().With<TkbIdentity>().WithOwnedWhen<AggregateUnit>(_gateOnAuthority).Build();
                _queriesFor = repo;
            }
            ITkbDatabase? db = null;
            bool squadState = repo.IsComponentTypeRegistered<Fdp.Toolkit.Squad.SquadCognitiveState>();
            foreach (var e in _units!)
            {
                if (repo.GetComponentRO<AggregateUnit>(e).Effective != AggregateResolution.Stochastic) continue;
                // ⭐ CE-8021 (agg-behaviors' F-11) — the unit's own contact pool (N-2): a memberless unit gets none from
                // SquadStateProvisioning (it provisions commanders WITH subordinates), so its sensing reported into nothing
                if (squadState && !repo.HasComponent<Fdp.Toolkit.Squad.SquadCognitiveState>(e)) repo.AddComponent(e, new Fdp.Toolkit.Squad.SquadCognitiveState());
                if (repo.HasComponent<UnitSlotTable>(e) && repo.GetComponentRO<UnitSlotTable>(e).Count > 0) continue;
                db ??= repo.GetSingletonManaged<ITkbDatabase>();
                if (db == null) return;
                var type = repo.GetComponentRO<TkbIdentity>(e).TkbType;
                StochasticUnitFill.Build(db, type, out var slots, out var holdings, _roles);
                if (slots.Count == 0 && holdings.Count == 0) continue;   // no composite: nothing to fill (said by the empty table)
                if (repo.HasComponent<UnitSlotTable>(e)) repo.GetComponentRW<UnitSlotTable>(e) = slots; else repo.AddComponent(e, slots);
                if (!repo.HasComponent<UnitHoldings>(e) || repo.GetComponentRO<UnitHoldings>(e).Count == 0)
                {
                    if (repo.HasComponent<UnitHoldings>(e)) repo.GetComponentRW<UnitHoldings>(e) = holdings; else repo.AddComponent(e, holdings);
                }
                if (db.TryGetByType(type, out var t) && t?.GetDescriptor<TkbCompositionDef>() is { } comp)
                {
                    ref var u = ref repo.GetComponentRW<AggregateUnit>(e);
                    if (u.Echelon == 0) u.Echelon = StochasticUnitFill.EchelonOf(comp.Echelon);
                }
                if (KillerVictimTkb.PersonalSoldiers == null && _roles.Count > 0)
                    KillerVictimTkb.PersonalSoldiers = new Dictionary<SlotRole, TkbTypeId>(_roles);
            }
        }
    }
}
