using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Squad.Command;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ <c>CE-7025</c> / <c>CE-7003</c> (City Battle §5, §11 S-1b, C-2; Indirect Fire IF-5) — smoke from the guns, asked for up the chain.</summary>
    public partial class SquadManeuverTests
    {
        /// <summary>⭐⭐ <c>CE-7025</c> — a SMOKE strike tells every gun to load its smoke round (<c>round</c> 1 on its FireAtPoint); a plain strike
        /// keeps round 0 (its first: the HE). ✅ Red-proof: the battery's member order not carrying <c>Round</c> ⇒ round 0 under a smoke strike.</summary>
        [Fact]
        public void CE7025_ASmokeStrike_TellsEveryGunToLoadItsSmoke_APlainOneDoesNot()
        {
            var point = new Vector3(3000f, 0f, 0f);
            foreach (var smoke in new[] { true, false })
            {
                var s = Battery(new Vector3(0f, -50f, 0f), new Vector3(0f, 50f, 0f));
                s.Order(System.Text.Json.JsonSerializer.Serialize(new Hrot.AI.Behaviors.Orders.StrikeOrder
                {
                    Location = point, Control = Hrot.AI.Behaviors.Orders.FireMissionControl.WhenReady, Smoke = smoke,
                }, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed), BehaviorNames.ArtilleryUnitStrike);
                for (int i = 0; i < 4; i++) s.Tick();
                var orders = FireOrders(s);
                Assert.Equal(2, orders.Count);
                Assert.All(orders.Values, o => Assert.Equal(smoke ? 1 : 0, o.Round));
            }
        }

        /// <summary>⭐⭐ <c>CE-7025</c> / <c>CE-7003</c> — a FireSupport REQUEST flagged SMOKE goes to the BATTERY as a smoke strike, never to the
        /// helicopter (smoke is the guns' only); a plain one still goes to the free helicopter first. ✅ Red-proof: the request's round not kept
        /// in the picture ⇒ the helicopter is sent with no smoke.</summary>
        [Fact]
        public void CE7025_ASmokeRequest_GoesToTheBatteryAsSmoke_NotToTheHelicopter()
        {
            var p = Headquarters();
            Entity heli = p.S.Members[1], battery = p.S.Members[2];
            var street = new Vector3(0f, 1200f, 0f);
            p.Report(p.S.Members[0], new CommandReport
            {
                Kind = ReportKind.Request, Subtype = (byte)RequestKind.FireSupport, At = street, Count = 1, Equipment = ElementEquipment.Smoke,
            });
            p.Ticks(4);
            Assert.Equal(0, p.OrdersTo(heli, BehaviorNames.AirArmedSearch));
            Assert.Equal(1, p.OrdersTo(battery, StrikeIntent));
            var strike = System.Text.Json.JsonSerializer.Deserialize<Hrot.AI.Behaviors.Orders.StrikeOrder>(
                p.S.Orders.Last(o => o.Entity.Equals(battery)).JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            Assert.True(strike.Smoke, "a smoke strike");
            Assert.True(Vector3.Distance(StrikePointOf(p, battery), street) < 1f);
        }

        /// <summary>⭐ <c>CE-7005</c> + <c>CE-7025</c> — the company's tempo request at half the deadline asks for SMOKE (the flag on its REQUEST).
        /// ✅ Red-proof: <c>AskFires</c> called with the default round ⇒ no smoke flag.</summary>
        [Fact]
        public void CE7025_TheCompanysHalfDeadlineRequest_AsksForSmoke()
        {
            var c = new Company();
            CompanyAttack(c, 100f, new Vector3(0f, 600f, 0f));
            c.Ticks(6);
            c.Set(c.Second); c.Ticks(5);
            c.Over(55);
            var q = c.S.Repo.GetComponentRO<ReportOutbox>(c.S.Commander).Queued;
            bool smoke = false;
            for (int i = 0; i < q.Count; i++)
                if (q.Items[i].Report.Kind == ReportKind.Request && q.Items[i].Report.Subtype == (byte)RequestKind.FireSupport)
                    smoke |= (q.Items[i].Report.Equipment & ElementEquipment.Smoke) != 0;
            Assert.True(smoke, "the half-deadline request asks for smoke");
        }
    }
}
