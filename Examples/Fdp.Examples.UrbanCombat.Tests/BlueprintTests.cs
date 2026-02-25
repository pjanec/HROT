using Fdp.Examples.UrbanCombat;
using Fdp.Examples.UrbanCombat.Blueprints;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics.Components;
using Xunit;

namespace Fdp.Examples.UrbanCombat.Tests
{
    /// <summary>
    /// BCS-P7-T2 blueprint component assertion tests.
    /// Uses <see cref="HeadlessDemoApp"/> to provide a fully-registered ECS world.
    /// </summary>
    public class BlueprintTests : System.IDisposable
    {
        private readonly HeadlessDemoApp _app;

        public BlueprintTests()
        {
            _app = new HeadlessDemoApp();
            _app.Initialize();
        }

        public void Dispose() => _app.Dispose();

        // ── Test 1: CivilianPedestrian ──────────────────────────────────────────────

        [Fact]
        public void Blueprint_CivilianPedestrian_HasAllRequiredComponents()
        {
            var e = EntityBlueprints.CivilianPedestrian(_app.World);

            // Core movement + identity
            Assert.True(_app.World.HasComponent<SimTransform>(e));
            Assert.True(_app.World.HasComponent<SimVelocity>(e));
            Assert.True(_app.World.HasComponent<SimTier>(e));
            Assert.True(_app.World.HasComponent<DoctrineState>(e));

            // Capability + locomotion
            Assert.True(_app.World.HasComponent<ActorCapabilityState>(e));
            var caps = _app.World.GetComponent<ActorCapabilityState>(e);
            Assert.True((caps.Capabilities & ActorCapabilities.CanMove) != 0);

            Assert.True(_app.World.HasComponent<LocomotionChannel>(e));

            // Vehicle
            Assert.True(_app.World.HasComponent<CarKinem.Core.VehicleState>(e));
            Assert.True(_app.World.HasComponent<CarKinem.Core.VehicleParams>(e));
            Assert.True(_app.World.HasComponent<CarKinem.Core.NavState>(e));

            // Perception
            Assert.True(_app.World.HasComponent<PerceptionReceptor>(e));
            var receptor = _app.World.GetComponent<PerceptionReceptor>(e);
            Assert.Equal(30f, receptor.VisionRange);
            Assert.Equal(100f, receptor.HearingRange);

            Assert.True(_app.World.HasComponent<TargetMemory>(e));

            // Physics
            Assert.True(_app.World.HasComponent<PhysicsCollider>(e));
            var collider = _app.World.GetComponent<PhysicsCollider>(e);
            Assert.Equal(0.4f, collider.Radius);
            Assert.Equal(1, collider.CollisionLayer);

            // Civilians have NO brain components (TrafficBrainSystem handles them)
            Assert.False(_app.World.HasComponent<BrainBlackboard>(e));
            Assert.False(_app.World.HasComponent<BrainBTreeState>(e));
            Assert.False(_app.World.HasComponent<BrainHsm128>(e));
        }

        // ── Test 2: MilitaryAPC ─────────────────────────────────────────────────────

        [Fact]
        public void Blueprint_MilitaryAPC_HasAllRequiredComponents()
        {
            var e = EntityBlueprints.MilitaryAPC(_app.World);

            // Core
            Assert.True(_app.World.HasComponent<SimTransform>(e));
            Assert.True(_app.World.HasComponent<SimVelocity>(e));

            // Tier 2 tactical entity
            var tier = _app.World.GetComponent<SimTier>(e);
            Assert.Equal((byte)2, tier.Value);

            // DoctrineState with BrainTier = 2
            Assert.True(_app.World.HasComponent<DoctrineState>(e));
            var doctrine = _app.World.GetComponent<DoctrineState>(e);
            Assert.Equal((byte)2, doctrine.BrainTier);

            // HSM brain
            Assert.True(_app.World.HasComponent<BrainHsm128>(e));
            Assert.True(_app.World.HasComponent<BrainBlackboard>(e));
            Assert.True(_app.World.HasComponent<PreviousCapabilities>(e));   // Required by HsmDamageBridgeSystem

            // Capability flags
            var caps = _app.World.GetComponent<ActorCapabilityState>(e);
            Assert.True((caps.Capabilities & ActorCapabilities.CanMove) != 0);
            Assert.True((caps.Capabilities & ActorCapabilities.CanInteract) != 0);

            // Channels
            Assert.True(_app.World.HasComponent<LocomotionChannel>(e));
            Assert.True(_app.World.HasComponent<InteractionChannel>(e));

            // Vehicle — Tank preset
            Assert.True(_app.World.HasComponent<CarKinem.Core.VehicleState>(e));
            Assert.True(_app.World.HasComponent<CarKinem.Core.VehicleParams>(e));
            Assert.True(_app.World.HasComponent<CarKinem.Core.NavState>(e));

            // Health (damageable)
            Assert.True(_app.World.HasComponent<Health>(e));
            Assert.True(_app.World.HasComponent<HealthData>(e));
            var health = _app.World.GetComponent<Health>(e);
            Assert.Equal(500f, health.Current);
            Assert.Equal(500f, health.Max);

            // Physics
            Assert.True(_app.World.HasComponent<PhysicsCollider>(e));
            var collider = _app.World.GetComponent<PhysicsCollider>(e);
            Assert.Equal(3.5f, collider.Radius);

            // Passenger capacity + Faction
            Assert.True(_app.World.HasComponent<PassengerBuffer>(e));
            Assert.True(_app.World.HasComponent<Faction>(e));
            var faction = _app.World.GetComponent<Faction>(e);
            Assert.Equal(1, faction.FactionId);   // FactionBlue = 1
        }

        // ── Test 3 (bonus): InfantrySoldier ────────────────────────────────────────

        [Fact]
        public void Blueprint_InfantrySoldier_HasAllRequiredComponents()
        {
            var e = EntityBlueprints.InfantrySoldier(_app.World);

            Assert.True(_app.World.HasComponent<BrainBTreeState>(e));
            Assert.True(_app.World.HasComponent<BrainBlackboard>(e));
            Assert.True(_app.World.HasComponent<PreviousCapabilities>(e));

            var caps = _app.World.GetComponent<ActorCapabilityState>(e);
            Assert.True((caps.Capabilities & ActorCapabilities.CanMove)  != 0);
            Assert.True((caps.Capabilities & ActorCapabilities.CanShoot) != 0);

            Assert.True(_app.World.HasComponent<WeaponChannel>(e));
            Assert.True(_app.World.HasComponent<Health>(e));
            Assert.True(_app.World.HasComponent<HealthData>(e));
            Assert.True(_app.World.HasComponent<WeaponState>(e));
            Assert.True(_app.World.HasComponent<PerceptionReceptor>(e));
            Assert.True(_app.World.HasComponent<TargetMemory>(e));

            var faction = _app.World.GetComponent<Faction>(e);
            Assert.Equal(1, faction.FactionId);  // FactionBlue
        }

        // ── Test 4 (bonus): Insurgent ────────────────────────────────────────────────

        [Fact]
        public void Blueprint_Insurgent_HasAllRequiredComponents()
        {
            var e = EntityBlueprints.Insurgent(_app.World);

            Assert.True(_app.World.HasComponent<BrainBTreeState>(e));
            Assert.True(_app.World.HasComponent<PreviousCapabilities>(e));

            var weaponState = _app.World.GetComponent<WeaponState>(e);
            Assert.Equal(1, weaponState.Ammo);  // RPG: 1 round

            var faction = _app.World.GetComponent<Faction>(e);
            Assert.Equal(2, faction.FactionId);  // FactionRed
        }
    }
}
