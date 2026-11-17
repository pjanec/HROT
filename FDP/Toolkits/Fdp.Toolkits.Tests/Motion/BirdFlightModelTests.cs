using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Navigation;
using Xunit;

namespace Fdp.Toolkit.Tests.Motion
{
    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-2 (Animals AN-4, AR-2) — the BIRD model: sent to a perch 200 m away it climbs to its cruise height, BANKS round (a capped turn
    /// rate — it never pivots on the spot) and comes down onto the perch, arriving there; with no intent it stays still (perched). 🔴 Red-proof:
    /// lift the yaw-rate cap ⇒ the first step already faces the perch ⇒ red.
    /// </summary>
    public sealed class BirdFlightModelTests
    {
        [Fact]
        public void CE4623_ABird_Cruises_BanksRound_AndComesDownOntoItsPerch()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<SimTransform>();
            w.RegisterComponent<SimVelocity>();
            w.RegisterComponent<NavigationStatus>();
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = new Vector3(0f, 0f, 0f), Rotation = Quaternion.Identity });   // facing +X
            w.AddComponent(e, new SimVelocity());
            w.AddComponent(e, new NavigationStatus());
            var model = new BirdFlightModel();
            var perch = new Vector3(-200f, 0f, 6f);                                                     // behind it, a tree top
            var intent = new NavigationIntent { Mode = NavigationMode.DirectPoint, IntentId = 7, FinalDestination = perch, TargetSpeed = 10f };

            float maxZ = 0f, firstYaw = 0f;
            for (int i = 0; i < 1200; i++)
            {
                var f = new MotionFrame(w, e, ref w.GetComponentRW<SimTransform>(e), ref w.GetComponentRW<SimVelocity>(e));
                model.Autonomous(ref f, in intent, 0.05f);
                if (i == 0) firstYaw = RotorcraftModel.YawOf(w.GetComponentRO<SimTransform>(e).Rotation);
                maxZ = MathF.Max(maxZ, w.GetComponentRO<SimTransform>(e).Position.Z);
                if (w.GetComponentRO<NavigationStatus>(e).Result == NavigationResult.Arrived) break;
            }
            Assert.True(MathF.Abs(firstYaw) <= BirdFlightModel.MaxYawRate * 0.05f + 1e-3f, $"banks round, never pivots ({firstYaw:F3} rad)");
            Assert.True(maxZ > 15f, $"it climbs to cruise ({maxZ:F0} m)");
            Assert.Equal(NavigationResult.Arrived, w.GetComponentRO<NavigationStatus>(e).Result);
            Assert.True(Vector3.Distance(perch, w.GetComponentRO<SimTransform>(e).Position) < 2f, "on its perch");

            var idle = new NavigationIntent();                                                           // no intent: perched, still
            var g = new MotionFrame(w, e, ref w.GetComponentRW<SimTransform>(e), ref w.GetComponentRW<SimVelocity>(e));
            model.Autonomous(ref g, in idle, 0.05f);
            Assert.Equal(Vector3.Zero, w.GetComponentRO<SimVelocity>(e).Linear);
        }

        /// <summary>⭐ <c>CE-4649</c> part C ② (AN-20a) — a SHOT bird (Health ≤ 0) at 25 m FALLS under gravity (whatever it was told) and LIES still on the ground; a live one at the same height does not fall.
        /// ✅ Red-proof: the dead check removed ⇒ it hangs in the air ⇒ red.</summary>
        [Fact]
        public void CE4649_ADeadBird_Falls_AndLiesOnTheGround()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<SimTransform>();
            w.RegisterComponent<SimVelocity>();
            w.RegisterComponent<NavigationStatus>();
            w.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = new Vector3(10f, 0f, 25f), Rotation = Quaternion.Identity });
            w.AddComponent(e, new SimVelocity { Linear = new Vector3(10f, 0f, 0f) });
            w.AddComponent(e, new NavigationStatus());
            w.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 5f, Max = 5f });
            var model = new BirdFlightModel();
            var idle = new NavigationIntent();
            void Step() { var f = new MotionFrame(w, e, ref w.GetComponentRW<SimTransform>(e), ref w.GetComponentRW<SimVelocity>(e)); model.Autonomous(ref f, in idle, 0.05f); }
            for (int i = 0; i < 20; i++) Step();
            Assert.Equal(25f, w.GetComponentRO<SimTransform>(e).Position.Z);        // alive, perched in the air as before: still
            w.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(e).Current = 0f;
            Step();
            Assert.True(w.GetComponentRO<SimTransform>(e).Position.Z < 25f, "it starts to fall at once");
            for (int i = 0; i < 200; i++) Step();                                    // ~3 s fall from 25 m, then lying
            Assert.Equal(0f, w.GetComponentRO<SimTransform>(e).Position.Z, 2);
            Assert.Equal(Vector3.Zero, w.GetComponentRO<SimVelocity>(e).Linear);
        }
    }
}
