using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-6005</c> (city battle O-2; 📄 docs/DESIGN_Peek_And_Fire.md §9.10) — <see cref="WreckObstacleSystem"/>: a heavy vehicle's death asks
    /// for ONE wreck obstacle on the hulk (its footprint, heading and hull height) through the creation request, on the Health owner, once.
    /// ⭐ Parallel contract (CE-6012 reruns these unchanged with <c>HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1</c>): one world, no
    /// statics, no clock.
    /// </summary>
    public sealed class WreckObstacleTests : IDisposable
    {
        private const long Tank = 100, Car = 102, Wreck = 8819;
        private readonly EntityRepository _w = new();
        private readonly WreckObstacleSystem _sys = new();

        public WreckObstacleTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterComponent<PhysicsCollider>();
            _w.RegisterComponent<global::CarKinem.Core.VehicleParams>();
            _w.RegisterComponent<NetworkAuthority>();
            _w.RegisterManagedEvent<EntityCreationRequest>();
            var db = new TkbDatabase();
            var tank = new TkbTemplate("Tank", Tank);
            tank.AddDescriptor(new WreckObstacleDto { ObstacleType = Wreck });
            db.Register(tank);
            db.Register(new TkbTemplate("Car", Car));
            var wreck = new TkbTemplate("Wreck", Wreck);
            wreck.AddDescriptor(new StaticObstacleDto { Length = 6.5f, Width = 3.4f, Height = 2.4f, Material = "car-body", MaxHealth = 600f });
            db.Register(wreck);
            _w.SetSingletonManaged<ITkbDatabase>(db);
        }

        public void Dispose() => _w.Dispose();

        private Entity Vehicle(long type, Vector3 at, float yaw, float hp = 500f)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
            _w.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            _w.AddComponent(e, new Health { Current = hp, Max = 500f });
            _w.AddComponent(e, new PhysicsCollider { Radius = 3.7f, Height = 2.6f });
            _w.AddComponent(e, new global::CarKinem.Core.VehicleParams { Length = 6.55f, Width = 3.6f });
            return e;
        }

        private EntityCreationRequest[] Frame()
        {
            _w.Bus.SwapBuffers();
            _sys.Execute(_w, 0.016f);
            _w.Bus.SwapBuffers();
            return _w.Bus.ReadManaged<EntityCreationRequest>().ToArray();
        }

        private void Kill(Entity e) => _w.GetComponentRW<Health>(e).Current = 0f;

        /// <summary>
        /// ⭐⭐⭐ <c>CE6005_W1</c> — a tank seen alive, then dead, asks for ONE wreck obstacle: the wreck type, at the hulk's position, along its heading,
        /// sized by its <c>VehicleParams</c> × its hull height; the frames after ask nothing more. 🔴 Red-proof: drop the crossing test (ask whenever it
        /// is dead) ⇒ a second request ⇒ red.
        /// </summary>
        [Fact]
        public void CE6005_W1_AHeavyVehiclesDeath_AsksOnceForAWreckObstacle_OnTheHulk()
        {
            var tank = Vehicle(Tank, new Vector3(100, 50, 0), MathF.PI / 2);
            Assert.Empty(Frame());
            Kill(tank);
            var req = Assert.Single(Frame());
            Assert.Equal(Wreck, (long)req.TkbType);
            var tf = Assert.Single(req.InitialComponents!.OfType<SimTransform>());
            var shape = Assert.Single(req.InitialComponents!.OfType<ObstacleShape>());
            Assert.Equal(new Vector3(100, 50, 0), (Vector3)tf.Position);
            Assert.Equal(MathF.PI / 2, TerrainObstacles.Yaw(tf.Rotation), 3);
            Assert.Equal((6.55f, 3.6f, 2.6f), (shape.Length, shape.Width, shape.Height));
            for (int i = 0; i < 5; i++) Assert.Empty(Frame());
            Assert.Equal(1, _sys.Requested);
        }

        /// <summary>
        /// ⭐⭐ <c>CE6005_W2</c> — no request for: a tank FIRST SEEN dead (a saved wreck — its obstacle was saved too, or cleared), a light car (no
        /// <see cref="WreckObstacleDto"/>), a tank whose Health another node owns (that node asks). 🔴 Red-proof: drop the Health-owner gate ⇒ red.
        /// </summary>
        [Fact]
        public void CE6005_W2_NoWreck_ForASavedHulk_ALightCar_OrANodeThatDoesNotOwnTheHealth()
        {
            var saved = Vehicle(Tank, new Vector3(10, 0, 0), 0f, hp: 0f);
            var car = Vehicle(Car, new Vector3(20, 0, 0), 0f);
            var theirs = Vehicle(Tank, new Vector3(30, 0, 0), 0f);
            _w.AddComponent(theirs, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            Assert.Empty(Frame());
            Kill(car); Kill(theirs);
            Assert.Empty(Frame());
            Assert.Empty(Frame());
        }

        /// <summary>⭐ <c>CE6005_W3</c> — a replay never asks (the recording holds the obstacle) and forgets: a death seen across a replay is no crossing.
        /// 🔴 Red-proof: drop the replay gate ⇒ red.</summary>
        [Fact]
        public void CE6005_W3_AReplayNeverAsks()
        {
            var tank = Vehicle(Tank, new Vector3(0, 0, 0), 0f);
            Assert.Empty(Frame());
            Fdp.Toolkit.Replay.ReplayState.Enter(_w);
            Kill(tank);
            Assert.Empty(Frame());
            Fdp.Toolkit.Replay.ReplayState.Leave(_w);
            Assert.Empty(Frame());   // the replay dropped the "was alive" memory: a hulk first seen dead after it is no crossing
        }

        /// <summary>⭐ HOT PATH (R-2084) — a steady frame over 50 living tanks allocates 0 B. 🔴 Red-proof: a boxed read per vehicle ⇒ bytes ⇒ red.</summary>
        [Fact]
        public void CE6005_H1_ASteadyFrameAllocatesNothing()
        {
            for (int i = 0; i < 50; i++) Vehicle(Tank, new Vector3(i * 10f, 0, 0), 0f);
            for (int i = 0; i < 5; i++) _sys.Execute(_w, 0.016f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) _sys.Execute(_w, 0.016f);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(bytes == 0, $"100 steady frames allocated {bytes} B");
        }
    }
}
