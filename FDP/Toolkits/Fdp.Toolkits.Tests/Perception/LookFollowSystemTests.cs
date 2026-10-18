using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Look;
using Xunit;

namespace Fdp.Toolkits.Tests.Perception
{
    /// <summary>
    /// ⭐ <c>CE-3252</c> (LD-1, LD-C, R-2049) — the head on the muscle: standing it leads to the look; walking it only GLANCES (≤ 2 s,
    /// then ≥ 1 s back on the way); with no look it returns to the body; an entity look follows the entity; the eye forward is the body's
    /// turned by the head. 📄 docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md §3.2a, §3.2b. New feature, no suite before.
    /// </summary>
    public sealed class LookFollowSystemTests
    {
        private static (EntityRepository World, Entity Man) World(float bodyYaw = 0f, float speed = 0f)
        {
            var w = new EntityRepository();
            w.RegisterComponent<SimTransform>();
            w.RegisterComponent<VehicleState>();
            w.RegisterComponent<LookState>();
            var man = w.CreateEntity();
            w.AddComponent(man, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, bodyYaw) });
            w.AddComponent(man, new VehicleState { Speed = speed });
            return (w, man);
        }

        private static float Deg(float rad) => rad * 180f / MathF.PI;

        /// <summary>✅ Red-proof: the standing branch writes 0 ⇒ the head stays on the body.</summary>
        [Fact]
        public void CE3252_Standing_TheHeadLeadsToTheLook_ClampedToTheNeck()
        {
            var (w, man) = World();
            var sys = new LookFollowSystem();
            Assert.True(LookOf.Want(w, man, new Vector3(0f, 10f, 0f)));   // 90° left
            sys.Execute(w, 0.1f);
            var look = w.GetComponentRO<LookState>(man);
            Assert.Equal(LookSource.LookOrdered, look.Source);
            Assert.Equal(70f, Deg(look.HeadYaw), 1);   // the neck stops at 70°; the body turns the rest (CarKinematicsSystem)

            LookOf.Want(w, man, new Vector3(10f, 10f, 0f));   // 45° left — within the neck
            sys.Execute(w, 0.1f);
            Assert.Equal(45f, Deg(w.GetComponentRO<LookState>(man).HeadYaw), 1);
            var eye = LookOf.EyeForward(w, man, w.GetComponentRO<SimTransform>(man).Rotation);
            Assert.Equal(45f, Deg(MathF.Atan2(eye.Y, eye.X)), 1);

            LookOf.Release(w, man);
            sys.Execute(w, 0.1f);
            Assert.Equal((0f, LookSource.LookFree), (w.GetComponentRO<LookState>(man).HeadYaw, w.GetComponentRO<LookState>(man).Source));
            w.Dispose();
        }

        /// <summary>LD-C — walking, a look off his way is a glance: ≤ 2 s turned, then ≥ 1 s back on the way, then again.
        /// ✅ Red-proof: the glance limit removed ⇒ the head stays turned past 2 s.</summary>
        [Fact]
        public void CE3252_Walking_ALookOffTheWay_IsAGlance_TwoSecondsThenOneBack()
        {
            var (w, man) = World(speed: 1.5f);
            var sys = new LookFollowSystem();
            LookOf.Want(w, man, new Vector3(0f, 10f, 0f));
            float Tick(float at) { w.SetSimulationTime(at); sys.Execute(w, 0.1f); return w.GetComponentRO<LookState>(man).HeadYaw; }

            Assert.NotEqual(0f, Tick(1.0f));                // the glance begins
            Assert.Equal(LookSource.LookGlance, w.GetComponentRO<LookState>(man).Source);
            Assert.NotEqual(0f, Tick(2.9f));                // still within its 2 s
            Assert.Equal(0f, Tick(3.2f));                   // past 2 s: back on the way, resting until 4.2
            Assert.Equal(0f, Tick(3.6f));
            Assert.Equal(0f, Tick(4.1f));
            Assert.NotEqual(0f, Tick(4.3f));                // the next glance
            w.Dispose();
        }

        /// <summary>An entity look follows the entity as it moves; it keeps the last place when the entity dies.</summary>
        [Fact]
        public void CE3252_AnEntityLook_FollowsIt()
        {
            var (w, man) = World();
            var other = w.CreateEntity();
            w.AddComponent(other, new SimTransform { Position = new Vector3(10f, 0f, 0f), Rotation = Quaternion.Identity });
            var sys = new LookFollowSystem();
            Assert.True(LookOf.Want(w, man, default, other));
            sys.Execute(w, 0.1f);
            Assert.Equal(0f, Deg(w.GetComponentRO<LookState>(man).HeadYaw), 1);

            w.SetComponent(other, new SimTransform { Position = new Vector3(10f, 10f, 0f), Rotation = Quaternion.Identity });
            sys.Execute(w, 0.1f);
            Assert.Equal(45f, Deg(w.GetComponentRO<LookState>(man).HeadYaw), 1);

            w.DestroyEntity(other);
            sys.Execute(w, 0.1f);
            var look = w.GetComponentRO<LookState>(man);
            Assert.True(look.TargetEntity.IsNull);
            Assert.Equal(new Vector3(10f, 10f, 0f), look.Target);
            w.Dispose();
        }

        /// <summary>No <see cref="LookState"/> (a vehicle, an aircraft) ⇒ the eye forward is the body's, exactly as before LD-2.</summary>
        [Fact]
        public void CE3252_NoLook_TheEyeIsTheBody()
        {
            var w = new EntityRepository();
            w.RegisterComponent<SimTransform>();
            var car = w.CreateEntity();
            var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.7f);
            Assert.Equal(Vector3.Transform(Vector3.UnitX, rot), LookOf.EyeForward(w, car, rot));
            w.Dispose();
        }
    }
}
