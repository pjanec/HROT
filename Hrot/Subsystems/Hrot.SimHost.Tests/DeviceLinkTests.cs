using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Hrot.AI.Behaviors.Devices.Binoculars;
using Hrot.AI.Behaviors.Devices.Rangefinder;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ Simulated devices SD-S5b (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d D-119) — THE LINK, end to end through the production registry, the
    /// device tick and the muscle's laser: the rangefinder ranges a marker and SENDs; the binoculars it is linked to (its <c>Peer0</c>) hold the marker's point in
    /// their own memory ONE TICK after the send — never in the sending tick — and a third, unlinked binoculars receive nothing; a repeat is never applied twice;
    /// the send works paused (a device-local act). Run under <c>HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1</c> too (the device tick as the parallel phase: the send
    /// rides the batch's command buffer, the receiver reads only itself). 🔴 Red-proof (compiles): <c>DeviceMessages.Ingest</c> removed from the device tick ⇒
    /// nothing received ⇒ red.
    /// </summary>
    public sealed class DeviceLinkTests
    {
        private sealed class Rig : IDisposable
        {
            public readonly EntityRepository W = new();
            public readonly BehaviorRegistry Registry = new();
            public readonly BehaviorIngressSystem Ingress;
            public readonly DeviceTickSystem Devices;
            public readonly LaserSpotSystem Laser = new();
            public SpatialHashGrid Grid;
            public readonly Entity R, B, C, Target;

            public Rig()
            {
                SimHostComponentRegistry.RegisterAll(W);
                CognitiveComponentRegistry.RegisterAll(W);
                BlueprintTierTable.RegisterAll(W);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                Ingress = new BehaviorIngressSystem(Registry);
                Devices = new DeviceTickSystem(Registry, Ingress);
                Grid = SpatialHashGrid.Create(100, 100, 20f, 1000, Allocator.Persistent, originX: -1000f, originY: -1000f);
                Grid.Clear();
                R = DeviceOf(BehaviorNames.DeviceRangefinder, 640);
                B = DeviceOf(BehaviorNames.DeviceBinoculars, 641);
                C = DeviceOf(BehaviorNames.DeviceBinoculars, 641);
                W.AddComponent(R, DeviceLinks.To(EntityRef.Of(W, B)));
                Target = W.CreateEntity();   // a marker 450 m east
                W.AddComponent(Target, new SimTransform { Position = new Vector3(450f, 0f, 0f), Rotation = Quaternion.Identity });
                W.AddComponent(Target, new PhysicsCollider { Radius = 0.5f, Height = 2f, CollisionLayer = 1 });
                Grid.Add(Target, new Vector2(450f, 0f));
                W.SetSingleton(new SpatialGridData { Grid = Grid });
                W.SetSingleton(new Fdp.Core.GlobalTime { TotalTime = 12.5 });
            }

            private Entity DeviceOf(string behavior, long type)
            {
                var d = W.CreateEntity();
                W.AddComponent(d, new SimTransform { Position = new Vector3(0f, 0f, 1.6f), Rotation = Quaternion.Identity });
                W.AddComponent(d, new BehaviorState());
                W.SetAuthority<BehaviorState>(d, true);
                W.AddComponent(d, new DeviceInfo { DeviceType = type, Behavior = new FixedString64(behavior) });
                W.AddComponent(d, new NetworkIdentity { Value = 7000 + d.Index });   // ⭐ the link and the message carry network ids (R-184)
                return d;
            }

            public void Press(byte range, byte send = 0)
            {
                var c = new DeviceControl();
                c.Presses[RangefinderChannels.Range] = range; c.Presses[RangefinderChannels.Send] = send;
                var input = new DeviceInput { Control = c };
                if (W.HasComponent<DeviceInput>(R)) W.SetComponent(R, input); else W.AddComponent(R, input);
            }

            public void Frame(float dt)
            {
                W.Bus.SwapBuffers();
                Devices.Execute(W, dt);
                Laser.Execute(W, dt);
            }

            public BinocularsState Bn(Entity e) => UnitMemory.Get<BinocularsState>(W, e);
            public RangefinderState Rf => UnitMemory.Get<RangefinderState>(W, R);

            public void Dispose() { Grid.Dispose(); W.Dispose(); }
        }

        [Fact]
        public void SDS5b_TheRangefindersSend_ReachesItsLinkedBinoculars_OneTickLate_AndNoOtherDevice()
        {
            using var r = new Rig();
            r.Press(0);
            for (int i = 0; i < 3; i++) r.Frame(0.016f);                    // started, based
            r.Press(1);
            for (int i = 0; i < 3; i++) r.Frame(0.016f);                    // laser → spot → range
            Assert.Equal(1, r.Rf.HasRange);
            Assert.InRange(r.Rf.LastPoint.X, 449f, 450f);
            Assert.Equal(0, r.Bn(r.B).HasTarget);

            r.Press(1, send: 1);                                             // SEND
            r.Frame(0f);                                                     // the sending tick (paused: a device-local act)
            Assert.Equal(1u, r.Rf.Sent);
            Assert.Equal(0, r.Bn(r.B).HasTarget);                            // ⭐ never in the sending tick
            r.Frame(0f);                                                     // the next: the receiver's ingest files it, its logic applies it
            var b = r.Bn(r.B);
            Assert.Equal(1, b.HasTarget);
            Assert.InRange(b.Target.Point.X, 449f, 450f);
            Assert.InRange(b.Target.Range, 449f, 450f);
            Assert.Equal(640, b.Target.FromType);
            Assert.Equal(12.5, b.Target.TakenAt);
            Assert.Equal(EntityRef.Of(r.W, r.R), b.TargetFrom);
            Assert.Equal(0, r.Bn(r.C).HasTarget);                            // ⭐ the unlinked device receives nothing
            Assert.False(r.W.HasComponent<DeviceInbox>(r.C));
            for (int i = 0; i < 5; i++) r.Frame(0.016f);                     // re-read: applied once
            Assert.Equal(1u, r.Bn(r.B).Messages.Seq0);
        }

        [Fact]
        public void SDS5b_NoRange_NoSend_AndNoLink_NoSend()
        {
            using var r = new Rig();
            r.Press(0);
            for (int i = 0; i < 3; i++) r.Frame(0.016f);
            r.Press(0, send: 1);                                             // SEND with nothing measured
            for (int i = 0; i < 3; i++) r.Frame(0.016f);
            Assert.Equal(0u, r.Rf.Sent);
            r.W.RemoveComponent<DeviceLinks>(r.R);
            r.Press(1, send: 1);
            for (int i = 0; i < 3; i++) r.Frame(0.016f);
            Assert.Equal(1, r.Rf.HasRange);
            r.Press(1, send: 2);                                             // SEND with a range, no link
            for (int i = 0; i < 3; i++) r.Frame(0.016f);
            Assert.Equal(0u, r.Rf.Sent);
            Assert.Equal(0, r.Bn(r.B).HasTarget);
        }
    }
}
