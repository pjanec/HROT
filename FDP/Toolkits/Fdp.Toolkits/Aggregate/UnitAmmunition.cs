using System;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Interfaces;

namespace Fdp.Toolkit.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8014</c> (answers agg-behaviors' F-1, <c>NOTE_AggBehaviors_To_AggBackend.md</c>; R-8004) — a stochastic unit's AMMUNITION SHARE
    /// (the SITREP's ammo colour): its rounds left ÷ its BASIC LOAD. ⭐ The basic load is DERIVED from the TKB — the same numbers a physical
    /// soldier / vehicle is spawned with (each mount's <see cref="WeaponMountDto.InitialAmmunition"/>) — ⛔ no recorded capacity field (SV-12:
    /// the layout stays). A slot's load is its role's soldier type's (<see cref="KillerVictimTkb.PersonalSoldiers"/>), a holding's is its type's
    /// × its count. Unknown (false) without a TKB or with nothing to measure.
    /// </summary>
    public static class UnitAmmunition
    {
        /// <summary>The rounds a type is spawned with: the sum over its mounts' initial ammunition (0 = none / unknown).</summary>
        public static int BasicLoad(ITkbDatabase db, TkbTypeId type)
        {
            if (!db.TryGetByType(type, out var t) || t == null) return 0;
            var suite = t.GetDescriptor<WeaponSuiteDto>();
            if (suite == null) return 0;
            int n = 0;
            for (int i = 0; i < suite.Mounts.Count; i++) n += Math.Max(0, suite.Mounts[i].InitialAmmunition);
            return n;
        }

        /// <summary>The unit's rounds left ÷ its basic load (0..1); false when it cannot be known.</summary>
        public static bool TryShare(EntityRepository world, Entity unit, out float share)
        {
            share = 0f;
            if (!world.HasSingletonManaged<ITkbDatabase>() || !world.IsComponentTypeRegistered<UnitSlotTable>()) return false;
            var db = world.GetSingletonManaged<ITkbDatabase>();
            if (db == null) return false;
            long left = 0, load = 0;
            var personal = KillerVictimTkb.PersonalSoldiers;
            if (personal != null && world.HasComponent<UnitSlotTable>(unit))
            {
                ref readonly var s = ref world.GetComponentRO<UnitSlotTable>(unit);
                for (int i = 0; i < s.Count; i++)
                {
                    if (!s.IsUp(i) || !personal.TryGetValue(s.Slots[i].Role, out var soldier)) continue;
                    int basic = BasicLoad(db, soldier);
                    if (basic <= 0) continue;
                    load += basic;
                    left += Math.Min(basic, (int)s.Slots[i].Ammo);
                }
            }
            if (world.IsComponentTypeRegistered<UnitHoldings>() && world.HasComponent<UnitHoldings>(unit))
            {
                ref readonly var h = ref world.GetComponentRO<UnitHoldings>(unit);
                for (int i = 0; i < h.Count; i++)
                {
                    if (h.Items[i].Kind == HoldingKind.Sensor || h.Items[i].Operational == 0) continue;
                    long basic = (long)BasicLoad(db, h.Items[i].Type) * h.Items[i].Operational;
                    if (basic <= 0) continue;
                    load += basic;
                    left += Math.Min(basic, (long)h.Items[i].Ammo);
                }
            }
            if (load <= 0) return false;
            share = Math.Clamp((float)left / load, 0f, 1f);
            return true;
        }
    }
}
