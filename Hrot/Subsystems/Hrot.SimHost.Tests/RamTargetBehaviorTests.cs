using System.Numerics;
using CarKinem.Core;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Combat.Components;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4622</c> K-2 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md RM-2, RM-3) — the SHIPPED <c>RamTarget</c> through the real ingress and brain
    /// tick: the vehicle marks its own hull with an active <see cref="RamOrder"/> naming the target, commits a LETHAL act on the car and on its driver, and drives at the
    /// target's LEAD point; when the target is down it ends and switches the order off. A crowd: the order says so, and it ends at the place.
    /// </summary>
    public sealed unsafe class RamTargetBehaviorTests
    {
        private const int TargetNet = 4622;

        private static Fdp.Toolkit.Navigation.MoveToParams Move(EntityRepository repo, Entity e)
        {
            var ch = repo.GetComponentRO<LocomotionChannel>(e);
            return *(Fdp.Toolkit.Navigation.MoveToParams*)ch.Params;
        }

        private static (StationWeaponBehaviorTests.World W, Entity Target, Entity Driver) Rig()
        {
            var w = new StationWeaponBehaviorTests.World();
            var repo = w.Repo;
            if (!repo.IsComponentTypeRegistered<RamOrder>()) repo.RegisterComponent<RamOrder>();
            if (!repo.IsComponentTypeRegistered<HostileAct>()) repo.RegisterComponent<HostileAct>();
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()) repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            if (!repo.IsComponentTypeRegistered<Health>()) repo.RegisterComponent<Health>();
            if (!repo.IsComponentTypeRegistered<SimVelocity>()) repo.RegisterComponent<SimVelocity>();
            if (!repo.IsComponentTypeRegistered<PassengerBuffer>()) repo.RegisterComponent<PassengerBuffer>();
            var target = repo.CreateEntity();
            repo.AddComponent(target, new SimTransform { Position = new Vector3(60f, 0f, 0f), Rotation = Quaternion.Identity });
            repo.AddComponent(target, new SimVelocity { Linear = new Vector3(0f, 2f, 0f) });   // walking north
            repo.AddComponent(target, new Health { Current = 100f, Max = 100f });
            repo.AddComponent(target, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = TargetNet });
            var driver = repo.CreateEntity();
            repo.AddComponent(driver, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            var seats = new PassengerBuffer { Count = 1 };
            seats.Passengers[0] = driver;
            repo.AddComponent(w.Man, seats);   // the harness's unit stands in for the car (its own brain, its own move channel)
            return (w, target, driver);
        }

        /// <summary>⭐ RM-2 / RM-3 — the order on the hull, the car Lethal and its driver too, the pursuit at the target's LEAD point (ahead of him along his walk), and on his
        /// death the end with the order OFF. ✅ Red-proofs: the lead dropped (aim at where he is) ⇒ red; the act on the car dropped ⇒ red; the act committed at the start (R-2134) ⇒ red; the order left on ⇒ red.</summary>
        [Fact]
        public void CE4622_K2_RamTarget_MarksTheHull_CommitsALethalAct_AndLeadsTheTarget()
        {
            var (w, target, driver) = Rig();
            var repo = w.Repo;
            w.Start(BehaviorNames.RamTarget, "{\"Target\":" + TargetNet + ",\"Speed\":16}");
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.True(RamOrder.On(repo, w.Man, out var order));
            Assert.Equal(TargetNet, order.Target.NetworkId);
            Assert.Equal(0, order.Crowd);
            Assert.NotEqual(HostileActKind.Lethal, HostileActs.Of(repo, w.Man));   // ⭐ R-2134 — 60 m out: not yet an act
            repo.GetComponentRW<SimTransform>(w.Man).Position = new Vector3(15f, 0f, 0f);   // inside the 50 m run-in
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal(HostileActKind.Lethal, HostileActs.Of(repo, w.Man));      // the CAR is the act's actor (KV-5)
            Assert.Equal(HostileActKind.Lethal, HostileActs.Of(repo, driver));     // and its driver
            repo.GetComponentRW<SimTransform>(w.Man).Position = Vector3.Zero;
            var to = Move(repo, w.Man).Destination;
            Assert.Equal(16f, Move(repo, w.Man).Speed, 2);
            Assert.True(to.Y > 1f, $"it drives at where he WILL be (lead), not where he is: aim {to}");
            Assert.InRange(to.X, 59f, 61f);
            Assert.Null(w.Finished);

            repo.GetComponentRW<Health>(target).Current = 0f;                   // struck down
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal(NodeStatus.Success, w.Finished);
            Assert.False(RamOrder.On(repo, w.Man, out _));                      // the order OFF (it stays on the hull: the off replicates)
        }

        /// <summary>⭐ RM-2 (KV-6) — a CROWD: no target, the order says Crowd (the mover then gives way to no person); it ends at the place.
        /// ✅ Red-proof: the crowd flag dropped ⇒ red.</summary>
        [Fact]
        public void CE4622_K2_RamTarget_IntoACrowd_DrivesAtThePlace_AndEndsThere()
        {
            var (w, _, _) = Rig();
            var repo = w.Repo;
            w.Start(BehaviorNames.RamTarget, "{\"At\":[40,10,0]}");
            for (int i = 0; i < 4; i++) w.Tick();
            Assert.True(RamOrder.On(repo, w.Man, out var order));
            Assert.Equal(1, order.Crowd);
            Assert.True(order.Target.IsNone);
            Assert.Equal(new Vector3(40f, 10f, 0f), Move(repo, w.Man).Destination);
            Assert.Equal(RamTargetBehavior.DefaultSpeed, Move(repo, w.Man).Speed, 2);
            repo.GetComponentRW<SimTransform>(w.Man).Position = new Vector3(39f, 10f, 0f);
            for (int i = 0; i < 3; i++) w.Tick();
            Assert.Equal(NodeStatus.Success, w.Finished);
            Assert.False(RamOrder.On(repo, w.Man, out _));
        }
    }
}
