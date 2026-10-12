using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Unit tests for <see cref="EmbarkExecutor"/> (BCS-P6-T3).
    /// Each test drives the executor directly — no dispatcher system — using a real
    /// <see cref="EntityRepository"/> with only the components the executor needs.
    /// </summary>
    public class EmbarkExecutorTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly EmbarkExecutor _executor;

        public EmbarkExecutorTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<ActorCapabilityState>();
            _world.RegisterComponent<PassengerBuffer>();
            _world.RegisterComponent<IsEmbarkedTag>();
            _world.RegisterComponent<InteractionChannel>();
            _world.RegisterEvent<Fdp.Toolkit.Behavior.Events.EmbarkEntityCommand>();
            _world.RegisterEvent<Fdp.Toolkit.Behavior.Events.DisembarkEntityCommand>();
            _world.RegisterEvent<Fdp.Toolkit.Behavior.Events.EjectPassengersCommand>();

            _executor = new EmbarkExecutor();
        }

        public void Dispose() => _world.Dispose();

        private readonly Fdp.Toolkit.Behavior.Systems.EmbarkationSystem _owner = new();

        /// <summary>⭐ <c>CE-3110</c> I-2a — the executor RAISES the command; the vehicle's owner (<c>EmbarkationSystem</c>, here the same
        /// world) applies it next frame; the executor then reads the answer from the state. (It used to write everything itself.)</summary>
        private void Drive(Entity e, ref InteractionChannel channel)
        {
            _executor.Execute(e, ref channel, _world, 0f);
            _world.Bus.SwapBuffers();
            _owner.Execute(_world, 0f);
            if (channel.Status == NodeStatus.Running) _executor.Execute(e, ref channel, _world, 0f);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private Entity CreateVehicle(Vector3 pos)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = pos, Rotation = Quaternion.Identity });
            _world.AddComponent(e, new PassengerBuffer());
            return e;
        }

        /// <summary>
        /// Creates a soldier entity with <see cref="ActorCapabilities.CanMove"/> and
        /// <see cref="ActorCapabilities.CanShoot"/> set, and an <see cref="InteractionChannel"/>
        /// whose Params are pre-filled with <paramref name="vehicleEntity"/> and
        /// <paramref name="maxBoardingRange"/>.
        /// Returns the entity and a local copy of the channel (channel.Status = Running).
        /// </summary>
        private unsafe (Entity soldier, InteractionChannel channel)
            CreateSoldier(Vector3 pos, Entity vehicleEntity, float maxBoardingRange)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = pos, Rotation = Quaternion.Identity });
            _world.AddComponent(e, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot
            });
            _world.AddComponent(e, new InteractionChannel { Status = NodeStatus.Running });

            var channel = _world.GetComponent<InteractionChannel>(e);
            var p = new EmbarkParams { VehicleEntity = vehicleEntity, MaxBoardingRange = maxBoardingRange };
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            _world.SetComponent(e, channel);
            channel = _world.GetComponent<InteractionChannel>(e);

            return (e, channel);
        }

        // ── Test 1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When the soldier is within <c>MaxBoardingRange</c>, Execute must add the soldier
        /// entity to the vehicle's <see cref="PassengerBuffer"/> and report
        /// <see cref="NodeStatus.Success"/>.
        /// </summary>
        [Fact]
        public void Embark_AddsSoldierToPassengerBuffer_WhenInRange()
        {
            var vehicle          = CreateVehicle(new Vector3(1f, 0f, 0f));
            var (soldier, channel) = CreateSoldier(Vector3.Zero, vehicle, 3f);

            _executor.OnEnter(soldier, ref channel, _world);
            Drive(soldier, ref channel);

            var buffer = _world.GetComponent<PassengerBuffer>(vehicle);
            Assert.Equal(1, buffer.Count);
            Assert.Equal(soldier, buffer.Passengers[0]);
            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When the soldier is farther than <c>MaxBoardingRange</c>, Execute must leave the
        /// <see cref="PassengerBuffer"/> empty and keep <see cref="NodeStatus.Running"/> so
        /// locomotion can continue closing the distance.
        /// </summary>
        [Fact]
        public void Embark_DoesNotEmbark_WhenDistanceTooFar()
        {
            var vehicle          = CreateVehicle(new Vector3(100f, 0f, 0f));
            var (soldier, channel) = CreateSoldier(Vector3.Zero, vehicle, 3f);

            _executor.OnEnter(soldier, ref channel, _world);
            Drive(soldier, ref channel);

            Assert.Equal(NodeStatus.Running, channel.Status);
            var buffer = _world.GetComponent<PassengerBuffer>(vehicle);
            Assert.Equal(0, buffer.Count);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// After a successful embark the soldier's <see cref="ActorCapabilityState"/>
        /// must have both <see cref="ActorCapabilities.CanMove"/> and
        /// <see cref="ActorCapabilities.CanShoot"/> stripped.
        /// </summary>
        [Fact]
        public void Embark_StripsCanMove_AndCanShoot_WhenBoarding()
        {
            var vehicle          = CreateVehicle(new Vector3(1f, 0f, 0f));
            var (soldier, channel) = CreateSoldier(Vector3.Zero, vehicle, 3f);

            _executor.OnEnter(soldier, ref channel, _world);
            Drive(soldier, ref channel);

            var caps = _world.GetComponent<ActorCapabilityState>(soldier).Capabilities;
            Assert.False(caps.HasFlag(ActorCapabilities.CanMove),  "CanMove should be stripped");
            Assert.False(caps.HasFlag(ActorCapabilities.CanShoot), "CanShoot should be stripped");
        }

        // ── Test 4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// After a successful embark the soldier must have an <see cref="IsEmbarkedTag"/>
        /// component whose <c>VehicleEntity</c> field points at the correct vehicle.
        /// </summary>
        [Fact]
        public void Embark_AddsIsEmbarkedTag()
        {
            var vehicle          = CreateVehicle(new Vector3(1f, 0f, 0f));
            var (soldier, channel) = CreateSoldier(Vector3.Zero, vehicle, 3f);

            _executor.OnEnter(soldier, ref channel, _world);
            Drive(soldier, ref channel);

            Assert.True(_world.HasComponent<IsEmbarkedTag>(soldier));
            var tag = _world.GetComponent<IsEmbarkedTag>(soldier);
            Assert.Equal(vehicle, tag.VehicleEntity);
        }

        // ── Test 5 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When the vehicle entity has already been destroyed, Execute must report
        /// <see cref="NodeStatus.Failure"/> and make no structural changes.
        /// </summary>
        [Fact]
        public void Embark_ReportsFailure_WhenVehicleNotAlive()
        {
            var vehicle          = CreateVehicle(new Vector3(1f, 0f, 0f));
            var (soldier, channel) = CreateSoldier(Vector3.Zero, vehicle, 3f);

            // Destroy the vehicle before the executor runs.
            _world.DestroyEntity(vehicle);

            _executor.OnEnter(soldier, ref channel, _world);
            Drive(soldier, ref channel);

            Assert.Equal(NodeStatus.Failure, channel.Status);
        }
    
        /// <summary>⭐ <c>CE-3110</c> I-2a — a vehicle with no free seat: Failure at once, and no command is raised.</summary>
        [Fact]
        public void CE3110_Embark_FailsAtOnce_WhenTheVehicleIsFull_AndRaisesNothing()
        {
            var vehicle = CreateVehicle(new Vector3(0f, 0f, 0f));
            _world.GetComponentRW<PassengerBuffer>(vehicle).Count = PassengerBuffer.Capacity;
            var (soldier, channel) = CreateSoldier(new Vector3(1f, 0f, 0f), vehicle, 3f);

            _executor.Execute(soldier, ref channel, _world, 0f);
            _world.Bus.SwapBuffers();

            Assert.Equal(NodeStatus.Failure, channel.Status);
            Assert.Equal(0, _world.Bus.Read<Fdp.Toolkit.Behavior.Events.EmbarkEntityCommand>().Length);
        }

        /// <summary>⭐ <c>CE-3110</c> I-2a — the command is raised ONCE; with no answer (the owner refused, or never got it) the soldier
        /// gives up after <see cref="EmbarkExecutor.ResultTimeoutSeconds"/>.</summary>
        [Fact]
        public void CE3110_Embark_RaisesTheCommandOnce_ThenFails_WhenNoAnswerComes()
        {
            var vehicle = CreateVehicle(new Vector3(0f, 0f, 0f));
            var (soldier, channel) = CreateSoldier(new Vector3(1f, 0f, 0f), vehicle, 3f);

            _executor.Execute(soldier, ref channel, _world, 0f);
            _world.Bus.SwapBuffers();
            Assert.Equal(1, _world.Bus.Read<Fdp.Toolkit.Behavior.Events.EmbarkEntityCommand>().Length);

            _executor.Execute(soldier, ref channel, _world, 1f);
            _world.Bus.SwapBuffers();
            Assert.Equal(0, _world.Bus.Read<Fdp.Toolkit.Behavior.Events.EmbarkEntityCommand>().Length);   // not raised again
            Assert.Equal(NodeStatus.Running, channel.Status);

            _executor.Execute(soldier, ref channel, _world, EmbarkExecutor.ResultTimeoutSeconds);
            Assert.Equal(NodeStatus.Failure, channel.Status);
        }
    }
}
