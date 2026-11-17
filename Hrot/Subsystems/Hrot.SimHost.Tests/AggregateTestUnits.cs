using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ agg-behaviors ST-0 — the one way the rails make a STOCHASTIC unit: an <see cref="AggregateUnit"/> (Stochastic), a full
    /// <see cref="UnitSlotTable"/> and its <see cref="UnitHoldings"/> on the unit entity itself, as the contract says (NOTE_AggBackend_To_Agg_Contract §1).</summary>
    internal static class AggregateTestUnits
    {
        /// <summary>Makes <paramref name="e"/> a stochastic unit of <paramref name="slots"/> authorized slots: the first <paramref name="up"/> up
        /// (health 25, ammo <paramref name="ammoEach"/>), <paramref name="broken"/> of those with stress 0.9, the rest down. The component set
        /// is added once (a second call rewrites it).</summary>
        public static void Make(EntityRepository w, Entity e, int slots, int up, int broken = 0, byte echelon = 1, ushort ammoEach = 10,
                                AggregateResolution resolution = AggregateResolution.Stochastic)
        {
            if (!w.IsComponentTypeRegistered<AggregateUnit>()) w.RegisterComponent<AggregateUnit>();
            if (!w.IsComponentTypeRegistered<UnitSlotTable>()) w.RegisterComponent<UnitSlotTable>();
            if (!w.IsComponentTypeRegistered<UnitHoldings>()) w.RegisterComponent<UnitHoldings>();
            var t = new UnitSlotTable { Count = (byte)slots };
            for (int i = 0; i < slots; i++)
                t.Slots[i] = new UnitSlot
                {
                    Role = i == 0 ? SlotRole.Leader : SlotRole.Rifleman,
                    Health = (System.Half)(i < up ? 25f : 0f),
                    Stress = (System.Half)(i < up && i >= up - broken ? 0.9f : 0.1f),
                    Ammo = ammoEach,
                };
            var a = new AggregateUnit { Resolution = resolution, Echelon = echelon };
            if (w.HasComponent<AggregateUnit>(e)) { w.SetComponent(e, a); w.SetComponent(e, t); }
            else { w.AddComponent(e, a); w.AddComponent(e, t); w.AddComponent(e, new UnitHoldings()); }
        }

        /// <summary>⭐ ST-3 (R-8314) — gives the world a TKB whose soldier (Leader and Rifleman alike) is spawned with <paramref name="basicLoad"/> rounds, and maps the
        /// slot roles to it (<see cref="KillerVictimTkb.PersonalSoldiers"/>, a process-wide static the host sets from the TKB) — so <see cref="UnitAmmunition.TryShare"/>
        /// can answer. Returns the previous map: restore it in a <c>finally</c>.</summary>
        public static System.Collections.Generic.IReadOnlyDictionary<SlotRole, Fdp.Core.TkbTypeId>? GiveABasicLoad(EntityRepository w, int basicLoad)
        {
            const long Soldier = 7401;
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var man = new Fdp.Interfaces.TkbTemplate("TestSoldier", Soldier);
            man.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = new() { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { InitialAmmunition = basicLoad } } });
            db.Register(man);
            w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            var was = KillerVictimTkb.PersonalSoldiers;
            KillerVictimTkb.PersonalSoldiers = new System.Collections.Generic.Dictionary<SlotRole, Fdp.Core.TkbTypeId>
            { [SlotRole.Leader] = new(Soldier), [SlotRole.Rifleman] = new(Soldier) };
            return was;
        }

        /// <summary>⭐ ST-3 — the killer–victim table the attrition rails fight with (agg-backend's <c>StochasticCombatRails.Table()</c> recipe, <c>KillerVictimGenerator.Build</c> from a
        /// 2-mil rifle that hits a man for 12.5): the personal weapon of a Leader and a Rifleman. Set <see cref="KillerVictimTable.Current"/> to it for the run, restore it after.</summary>
        public static KillerVictimTable FightTable(float roundsPerSecond = 0.5f)
        {
            var rifle = new TkbTypeId(9001);
            var t = KillerVictimGenerator.Build(new[] { new WeaponNumbers(rifle, DispersionMils: 2f, Penetration: 6f, DamagePerHit: 12.5f, EffectiveRoundsPerSecond: roundsPerSecond, MaxRange: 600f) },
                new[] { new TargetNumbers(KillerVictimTable.Personnel, Radius: 0.3f, Health: 25f) });
            t.SetPersonalWeapon(SlotRole.Rifleman, rifle);
            t.SetPersonalWeapon(SlotRole.Leader, rifle);
            return t;
        }
    }
}
