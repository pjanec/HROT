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
    }
}
