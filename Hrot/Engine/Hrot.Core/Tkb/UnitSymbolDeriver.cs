using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;
using GizmoMap.Contracts.Symbology;
using Hrot.Map.Definitions.Tkb;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ <c>CE-8609</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §6 S-1…S-3, mockup ⑧; docs/UX/DESIGN_Unit_Symbols.md P-1) — a UNIT type's NATO
    /// symbol, DERIVED from what it is MADE OF: its TKB composition walked down to the bodies (child types × count, composites recursed), each body read from its
    /// own TKB data — the DIS type (a lifeform is a man; a land platform's SISO category: tank, IFV, artillery, mortar, utility), its weapons, its seats — or
    /// its authored <see cref="MapVisualDef.SymbolRole"/>. Weighed (a man 1, a crew weapon 3, a combat vehicle 6): the dominant role is the ICON; men carried
    /// by tracked / wheeled carriers ⇒ mechanised / motorised; a second role ≥ 15 % of the weight ⇒ the SECONDARY mark; the vehicles' majority track ⇒ the
    /// MOBILITY; the unit's own DIS subcategory ⇒ the ECHELON (UnitEchelon numbers are 2525's). Nothing found ⇒ the unit's DIS arm.
    /// <para>⚠ S-3: from the TYPE, never the live roster — losses never change the symbol (they are the card's). A stochastic unit keeps the composite type
    /// it was made from, so both kinds read the same. The affiliation is the caller's (the side can change; the type cannot). Cached per database × type:
    /// derived once, then a lookup (0 B).</para>
    /// </summary>
    public static class UnitSymbolDeriver
    {
        public const float ManWeight = 1f, CrewWeaponWeight = 3f, CombatVehicleWeight = 6f, SupportVehicleWeight = 1f, SecondaryShare = 0.15f;
        private const int MaxDepth = 6;

        private enum Body : byte { None, Man, CrewWeapon, CombatVehicle, Carrier, SupportVehicle }

        private static readonly ConditionalWeakTable<ITkbDatabase, ConcurrentDictionary<long, SymbolCode>> _cache = new();

        /// <summary>The symbol of unit type <paramref name="unitType"/> (affiliation <c>Unknown</c> — the caller sets the side); false when the type is not a
        /// unit (no composition and no unit DIS kind) or is unknown.</summary>
        public static bool TryOfType(ITkbDatabase db, long unitType, out SymbolCode symbol)
        {
            var map = _cache.GetValue(db, static _ => new ConcurrentDictionary<long, SymbolCode>());
            if (map.TryGetValue(unitType, out symbol)) return symbol.Echelon != SymbolEchelon.NoEchelon || symbol.Role != SymbolRole.NoRole;
            symbol = Derive(db, unitType);
            map.TryAdd(unitType, symbol);
            return symbol.Echelon != SymbolEchelon.NoEchelon || symbol.Role != SymbolRole.NoRole;
        }

        /// <summary>The same symbol with the side's frame.</summary>
        public static SymbolCode WithSide(in SymbolCode s, ForceId side) => new(side switch
        {
            ForceId.Friend => SymbolAffiliation.Friend, ForceId.Hostile => SymbolAffiliation.Hostile, ForceId.Neutral => SymbolAffiliation.Neutral, _ => SymbolAffiliation.Unknown,
        }, s.Role, s.Echelon, s.Mobility, s.Secondary);

        // per derivation only (once per unit type, then cached) — a class, so a role's weight is a plain array slot
        private sealed class Tally
        {
            public readonly float[] Weight = new float[(int)SymbolRole.Support + 1];
            public float Men, TrackedCarriers, WheeledCarriers, Tracked, Wheeled;
            public ref float Of(SymbolRole r) => ref Weight[r == SymbolRole.NoRole ? (int)SymbolRole.Infantry : (int)r];
        }

        private static SymbolCode Derive(ITkbDatabase db, long unitType)
        {
            if (!db.TryGetByType(unitType, out var unit) || unit == null) return default;
            var composition = unit.GetDescriptor<TkbCompositionDef>();
            bool isUnitDis = unit.DisType.Kind == UnitDis.Kind;
            if (composition == null && !isUnitDis) return default;

            var t = new Tally();
            if (composition != null) Walk(db, composition, 1f, 0, t);

            // the icon: the heaviest role
            var dominant = SymbolRole.NoRole; float best = 0f, total = 0f;
            for (var r = SymbolRole.Infantry; r <= SymbolRole.Support; r++)
            {
                float w = t.Of(r); total += w;
                if (w > best) { best = w; dominant = r; }
            }
            if (dominant == SymbolRole.NoRole && isUnitDis) dominant = ArmOf((UnitArm)unit.DisType.Category);

            // men carried ⇒ mechanised / motorised (S-2)
            if (dominant == SymbolRole.Infantry && t.Men > 0f && t.TrackedCarriers + t.WheeledCarriers > 0f)
                dominant = t.TrackedCarriers >= t.WheeledCarriers ? SymbolRole.Mechanized : SymbolRole.Motorized;

            // the second role, when it carries a real share and has a mark
            var secondary = SymbolRole.NoRole; float second = 0f;
            for (var r = SymbolRole.Infantry; r <= SymbolRole.Support; r++)
            {
                if (r == SymbolRole.Infantry || r == dominant || SymbolCode.MarkOf(r).Length == 0) continue;
                float w = t.Of(r);
                if (w > second) { second = w; secondary = r; }
            }
            if (total <= 0f || second < SecondaryShare * total) secondary = SymbolRole.NoRole;

            var mobility = t.Tracked + t.Wheeled <= 0f ? SymbolMobility.NoMobility : t.Tracked >= t.Wheeled ? SymbolMobility.Tracked : SymbolMobility.Wheeled;
            var echelon = isUnitDis && unit.DisType.Subcategory is >= 1 and <= 9 ? (SymbolEchelon)unit.DisType.Subcategory
                        : composition != null ? EchelonOf(composition.Echelon) : SymbolEchelon.NoEchelon;
            return new SymbolCode(SymbolAffiliation.Unknown, dominant, echelon, mobility, secondary);
        }

        private static void Walk(ITkbDatabase db, TkbCompositionDef composition, float times, int depth, Tally t)
        {
            if (depth > MaxDepth) return;
            var slots = composition.Subordinates;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot.Count <= 0 || !db.TryGetByType((long)slot.TkbType, out var child) || child == null) continue;
                float n = slot.Count * times;
                var sub = child.GetDescriptor<TkbCompositionDef>();
                if (sub != null) { Walk(db, sub, n, depth + 1, t); continue; }
                var (body, role, tracked) = Classify(child);
                switch (body)
                {
                    case Body.Man: t.Men += n; t.Of(role) += n * ManWeight; break;
                    case Body.CrewWeapon: t.Of(role) += n * CrewWeaponWeight; break;
                    case Body.CombatVehicle: t.Of(role) += n * CombatVehicleWeight; Count(t, tracked, n); break;
                    case Body.Carrier:
                        if (tracked) t.TrackedCarriers += n; else t.WheeledCarriers += n;
                        Count(t, tracked, n); break;
                    case Body.SupportVehicle: t.Of(role) += n * SupportVehicleWeight; Count(t, tracked, n); break;
                }
            }
        }

        private static void Count(Tally t, bool tracked, float n) { if (tracked) t.Tracked += n; else t.Wheeled += n; }

        /// <summary>One body type: what it is and the role it lends a unit (authored <see cref="MapVisualDef.SymbolRole"/> wins).</summary>
        private static (Body Body, SymbolRole Role, bool Tracked) Classify(TkbTemplate t)
        {
            var authored = t.GetDescriptor<MapVisualDef>()?.SymbolRole;
            var dis = t.DisType;
            var weapon = MainWeapon(t);
            bool carries = t.GetDescriptor<CarrierDefDto>() is { Seats.Count: > 0 };
            bool tracked = t.GetDescriptor<SimVehicleDef>() is { } v ? v.Mobility == TerrainMobility.Tracked : dis.Category is 1 or 2 or 4 or 8 or 9;
            if (dis.Kind == 3)   // a lifeform: a man
                return (Body.Man, authored ?? (weapon is WeaponCategory.Atgm or WeaponCategory.Rocket ? SymbolRole.AntiArmour : weapon == WeaponCategory.Mortar ? SymbolRole.Mortar : SymbolRole.Infantry), false);
            if (dis.Kind != 1 || dis.Domain != 1) return (Body.None, SymbolRole.NoRole, false);   // not a land platform (aircraft, features, munitions)
            if (t.GetDescriptor<WeaponSystemDto>() != null || dis.Category is 5 or 10)   // a crew-served weapon (a mortar, a towed gun, a tripod)
                return (Body.CrewWeapon, authored ?? weapon switch
                {
                    WeaponCategory.Mortar => SymbolRole.Mortar, WeaponCategory.Howitzer => SymbolRole.Artillery,
                    WeaponCategory.Atgm or WeaponCategory.Rocket => SymbolRole.AntiArmour, _ => dis.Category == 10 ? SymbolRole.Mortar : dis.Category == 5 ? SymbolRole.Artillery : SymbolRole.Infantry,
                }, false);
            switch (dis.Category)
            {
                case 1: return (Body.CombatVehicle, authored ?? SymbolRole.Armour, tracked);                    // a tank
                case 4: return (Body.CombatVehicle, authored ?? SymbolRole.Artillery, tracked);                 // self-propelled artillery
                case 2: case 3: return authored is { } a2 && a2 != SymbolRole.Mechanized ? (Body.CombatVehicle, a2, tracked) : (Body.Carrier, SymbolRole.NoRole, tracked);   // IFV / APC
            }
            if (authored is { } a) return (Body.CombatVehicle, a, tracked);
            if (carries) return (Body.Carrier, SymbolRole.NoRole, tracked);                                    // a truck, a technical with a bed
            return weapon is WeaponCategory.Atgm ? (Body.CombatVehicle, SymbolRole.AntiArmour, tracked)
                 : weapon is WeaponCategory.TankGun ? (Body.CombatVehicle, SymbolRole.Armour, tracked)
                 : (Body.SupportVehicle, SymbolRole.Support, tracked);
        }

        private static WeaponCategory MainWeapon(TkbTemplate t)
        {
            var best = WeaponCategory.Unknown;
            if (t.GetDescriptor<SimCombatDef>() is { } c)
                for (int i = 0; i < c.Weapons.Count; i++) best = Heavier(best, c.Weapons[i].Category);
            if (t.GetDescriptor<WeaponSuiteDto>() is { } s)
                for (int i = 0; i < s.Mounts.Count; i++) best = Heavier(best, s.Mounts[i].Category);
            return best;
        }

        // the weapon that says most about what a body is for
        private static WeaponCategory Heavier(WeaponCategory a, WeaponCategory b) => Rank(b) > Rank(a) ? b : a;
        private static int Rank(WeaponCategory c) => c switch
        {
            WeaponCategory.Howitzer => 9, WeaponCategory.TankGun => 8, WeaponCategory.Mortar => 7, WeaponCategory.Atgm => 6, WeaponCategory.Rocket => 5,
            WeaponCategory.Autocannon => 4, WeaponCategory.MachineGun => 3, WeaponCategory.Grenade => 2, WeaponCategory.SmallArms => 1, _ => 0,
        };

        private static SymbolRole ArmOf(UnitArm arm) => arm switch
        {
            UnitArm.Infantry => SymbolRole.Infantry, UnitArm.Armor => SymbolRole.Armour, UnitArm.MechanizedInfantry => SymbolRole.Mechanized,
            UnitArm.Artillery => SymbolRole.Artillery, UnitArm.Reconnaissance => SymbolRole.Reconnaissance, _ => SymbolRole.NoRole,
        };

        private static SymbolEchelon EchelonOf(string? name)
        {
            if (string.IsNullOrEmpty(name)) return SymbolEchelon.NoEchelon;
            try { return (SymbolEchelon)(byte)UnitDis.EchelonOf(name); } catch (ArgumentException) { return SymbolEchelon.NoEchelon; }
        }
    }
}
