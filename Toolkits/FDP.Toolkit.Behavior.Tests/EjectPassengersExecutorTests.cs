using System;
using System.Numerics;
using Fdp.Kernel;
using Fbt;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;
using Xunit;

namespace FDP.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Unit tests for <see cref="EjectPassengersExecutor"/> (BCS-P6-T4).
    /// Each test drives the executor directly — no dispatcher system — using a real
    /// <see cref="EntityRepository"/> with only the components the executor needs.
    /// </summary>
    public class EjectPassengersExecutorTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly EjectPassengersExecutor _executor;

        public EjectPassengersExecutorTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<ActorCapabilityState>();
            _world.RegisterComponent<PassengerBuffer>();
            _world.RegisterComponent<IsEmbarkedTag>();
            _world.RegisterComponent<InteractionChannel>();

            _executor = new EjectPassengersExecutor();
        }

        public void Dispose() => _world.Dispose();

        // ── Helpers ───────────────────────────────────────────────────────────

        private Entity CreateVehicle(Vector3 pos)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = pos, Rotation = Quaternion.Identity });
            _world.AddComponent(e, new PassengerBuffer());
            _world.AddComponent(e, new InteractionChannel { Status = NodeStatus.Running });
            return e;
        }

        /// <summary>
        /// Creates a soldier that is already "embarked": capabilities stripped,
        /// <see cref="IsEmbarkedTag"/> present, placed at the vehicle position.
        /// The soldier is also added to the vehicle's <see cref="PassengerBuffer"/>.
        /// </summary>
        private Entity CreateEmbarkedSoldier(Entity vehicle)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _world.AddComponent(e, new ActorCapabilityState { Capabilities = ActorCapabilities.None });
            _world.AddComponent(e, new IsEmbarkedTag { VehicleEntity = vehicle });

            // Add to the vehicle buffer.
            ref var buffer = ref _world.GetComponentRW<PassengerBuffer>(vehicle);
            buffer.Passengers[buffer.Count] = e;
            buffer.Count++;

            return e;
        }

        // ── Test 1 ────────────────────────────────────────────────────────────

        /// <summary>
        /// After ejection each live passenger must have both
        /// <see cref="ActorCapabilities.CanMove"/> and <see cref="ActorCapabilities.CanShoot"/>
        /// restored in their <see cref="ActorCapabilityState"/>.
        /// </summary>
        [Fact]
        public void Eject_RestoresCanMove_AndCanShoot()
        {
            var vehicle = CreateVehicle(new Vector3(10f, 0f, 0f));
            var soldier = CreateEmbarkedSoldier(vehicle);

            var channel = _world.GetComponent<InteractionChannel>(vehicle);
            _executor.OnEnter(vehicle, ref channel, _world);
            _executor.Execute(vehicle, ref channel, _world, 0f);

            var caps = _world.GetComponent<ActorCapabilityState>(soldier).Capabilities;
            Assert.True(caps.HasFlag(ActorCapabilities.CanMove),  "CanMove should be restored");
            Assert.True(caps.HasFlag(ActorCapabilities.CanShoot), "CanShoot should be restored");
            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────

        /// <summary>
        /// After ejection the <see cref="IsEmbarkedTag"/> must be removed from every
        /// live passenger.
        /// </summary>
        [Fact]
        public void Eject_RemovesIsEmbarkedTag()
        {
            var vehicle = CreateVehicle(new Vector3(10f, 0f, 0f));
            var soldier = CreateEmbarkedSoldier(vehicle);

            var channel = _world.GetComponent<InteractionChannel>(vehicle);
            _executor.OnEnter(vehicle, ref channel, _world);
            _executor.Execute(vehicle, ref channel, _world, 0f);

            Assert.False(_world.HasComponent<IsEmbarkedTag>(soldier));
        }

        // ── Test 3 ────────────────────────────────────────────────────────────

        /// <summary>
        /// After ejecting two passengers the vehicle's <see cref="PassengerBuffer.Count"/>
        /// must be zero, confirming the buffer was fully cleared.
        /// </summary>
        [Fact]
        public void Eject_ClearsPassengerBuffer()
        {
            var vehicle  = CreateVehicle(new Vector3(10f, 0f, 0f));
            var soldier1 = CreateEmbarkedSoldier(vehicle);
            var soldier2 = CreateEmbarkedSoldier(vehicle);

            // Sanity: two passengers before ejection.
            Assert.Equal(2, _world.GetComponent<PassengerBuffer>(vehicle).Count);

            var channel = _world.GetComponent<InteractionChannel>(vehicle);
            _executor.OnEnter(vehicle, ref channel, _world);
            _executor.Execute(vehicle, ref channel, _world, 0f);

            Assert.Equal(0, _world.GetComponent<PassengerBuffer>(vehicle).Count);

            // Suppress unused variable warnings — both soldiers were added intentionally.
            _ = soldier1;
            _ = soldier2;
        }

        // ── Test 4 ────────────────────────────────────────────────────────────

        /// <summary>
        /// When one passenger in the buffer has been destroyed before ejection, the
        /// executor must not throw and must still process all remaining live passengers.
        /// </summary>
        [Fact]
        public void Eject_SkipsDeadPassengers_Gracefully()
        {
            var vehicle      = CreateVehicle(new Vector3(10f, 0f, 0f));
            var deadSoldier  = CreateEmbarkedSoldier(vehicle);
            var liveSoldier  = CreateEmbarkedSoldier(vehicle);

            // Destroy the first passenger before the executor runs.
            _world.DestroyEntity(deadSoldier);

            var channel = _world.GetComponent<InteractionChannel>(vehicle);

            // Must not throw.
            _executor.OnEnter(vehicle, ref channel, _world);
            _executor.Execute(vehicle, ref channel, _world, 0f);

            // Buffer must be cleared and live passenger must have capabilities restored.
            Assert.Equal(0, _world.GetComponent<PassengerBuffer>(vehicle).Count);

            var caps = _world.GetComponent<ActorCapabilityState>(liveSoldier).Capabilities;
            Assert.True(caps.HasFlag(ActorCapabilities.CanMove),  "Live soldier CanMove should be restored");
            Assert.True(caps.HasFlag(ActorCapabilities.CanShoot), "Live soldier CanShoot should be restored");
        }
    }
}
