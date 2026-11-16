using System;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;

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

        // ── ⭐ CE-8013 ③ (X-4) — a slot's rounds onto its soldier at the handover, and back at the merge ─────────────────────────────

        /// <summary>
        /// ⭐ The handover: the soldier was spawned with his full basic load; each of his mounts (his own <see cref="WeaponState"/> and his mount
        /// children) keeps the SAME share of what it carries that the slot has left of the soldier's total — so a slot at half its rounds
        /// hands over a man at half on every weapon. Allocation-free (rare: once per child per split).
        /// </summary>
        public static void ToSoldier(EntityRepository repo, Entity soldier, int slotRounds)
        {
            if (!repo.IsComponentTypeRegistered<WeaponState>()) return;
            long carried = Sum(repo, soldier);
            if (carried <= 0) return;
            double share = Math.Clamp((double)Math.Max(0, slotRounds) / carried, 0.0, 1.0);
            long given = 0; Entity last = default; bool any = false;
            Scale(repo, soldier, share, ref given, ref last, ref any);
            // the rounding remainder goes to the last mount, so the soldier carries exactly min(slot, carried)
            long want = Math.Min(Math.Max(0, slotRounds), carried);
            if (any && given < want)
            {
                ref var w = ref repo.GetComponentRW<WeaponState>(last);
                w.Ammo += (int)(want - given);
            }
        }

        /// <summary>⭐ The merge: the soldier's rounds over all his mounts, clamped to a slot's 16 bits; −1 when he carries no weapon state.</summary>
        public static int ToSlot(EntityRepository repo, Entity soldier)
        {
            if (!repo.IsComponentTypeRegistered<WeaponState>()) return -1;
            bool has = repo.HasComponent<WeaponState>(soldier) || HasMountChild(repo, soldier);
            if (!has) return -1;
            return (int)Math.Clamp(Sum(repo, soldier), 0, ushort.MaxValue);
        }

        private static long Sum(EntityRepository repo, Entity soldier)
        {
            long n = 0;
            if (repo.HasComponent<WeaponState>(soldier)) n += Math.Max(0, repo.GetComponentRO<WeaponState>(soldier).Ammo);
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>())
                foreach (var c in repo.Query().With<WeaponState>().With<Fdp.Toolkit.Replication.Components.PartMetadata>().IncludeAll().Build())
                    if (repo.GetComponentRO<Fdp.Toolkit.Replication.Components.PartMetadata>(c).ParentEntity == soldier)
                        n += Math.Max(0, repo.GetComponentRO<WeaponState>(c).Ammo);
            return n;
        }

        private static bool HasMountChild(EntityRepository repo, Entity soldier)
        {
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>()) return false;
            foreach (var c in repo.Query().With<WeaponState>().With<Fdp.Toolkit.Replication.Components.PartMetadata>().IncludeAll().Build())
                if (repo.GetComponentRO<Fdp.Toolkit.Replication.Components.PartMetadata>(c).ParentEntity == soldier) return true;
            return false;
        }

        private static void Scale(EntityRepository repo, Entity soldier, double share, ref long given, ref Entity last, ref bool any)
        {
            if (repo.HasComponent<WeaponState>(soldier))
            {
                ref var w = ref repo.GetComponentRW<WeaponState>(soldier);
                w.Ammo = (int)Math.Floor(Math.Max(0, w.Ammo) * share); given += w.Ammo; last = soldier; any = true;
            }
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>()) return;
            foreach (var c in repo.Query().With<WeaponState>().With<Fdp.Toolkit.Replication.Components.PartMetadata>().IncludeAll().Build())
            {
                if (repo.GetComponentRO<Fdp.Toolkit.Replication.Components.PartMetadata>(c).ParentEntity != soldier) continue;
                ref var w = ref repo.GetComponentRW<WeaponState>(c);
                w.Ammo = (int)Math.Floor(Math.Max(0, w.Ammo) * share); given += w.Ammo; last = c; any = true;
            }
        }
    }
}
