using System.Numerics;
using System.Runtime.CompilerServices;
using CarKinem.Core;
using Fdp.Kernel;
using Fbt;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Navigation.Executors;
using Xunit;

namespace FDP.Toolkit.Navigation.Tests.ExecutorTests
{
    /// <summary>
    /// Unit tests for <see cref="FleeExecutor"/> (BCS-P3-T3).
    /// Covers safe-distance success, dead-threat generational guard (DEBT-009 propagation),
    /// and throttled flee-vector recalculation.
    /// </summary>
    public class FleeExecutorTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────────────────────

        private static unsafe (EntityRepository world, Entity self, Entity threat, LocomotionChannel channel)
            BuildWorld(Vector3 selfPos, Vector3 threatPos, float safeDistance, float speed)
        {
            var world  = NavigationTestWorldFactory.Create();

            var threat = world.CreateEntity();
            world.AddComponent(threat, new SimTransform { Position = threatPos, Rotation = Quaternion.Identity });
            world.AddComponent(threat, new SimVelocity());

            var self = world.CreateEntity();
            world.AddComponent(self, new SimTransform { Position = selfPos, Rotation = Quaternion.Identity });
            world.AddComponent(self, new SimVelocity());
            world.AddComponent(self, new NavState());
            world.AddComponent(self, new LocomotionChannel());

            var channel = world.GetComponent<LocomotionChannel>(self);
            channel.ActiveAction = NavigationConstants.ActionIdFlee;

            var p = new FleeParams
            {
                Threat       = threat,
                SafeDistance = safeDistance,
                Speed        = speed,
            };
            Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);

            world.SetComponent(self, channel);
            channel = world.GetComponent<LocomotionChannel>(self);
            return (world, self, threat, channel);
        }

        /// <summary>Advance GlobalTime.FrameNumber by one and update the singleton.</summary>
        private static void AdvanceTick(EntityRepository world)
        {
            var gt = world.GetSingletonUnmanaged<GlobalTime>();
            gt.FrameNumber++;
            world.SetSingletonUnmanaged(gt);
        }

        // ── Test 1 ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// When the entity is already beyond <see cref="FleeParams.SafeDistance"/> from the threat,
        /// <see cref="FleeExecutor.Execute"/> reports <see cref="NodeStatus.Success"/> on the next tick.
        /// </summary>
        [Fact]
        public void FleeExecutor_ReportsSuccess_WhenSafeDistanceReached()
        {
            // self at origin, threat at (10,0) — SafeDistance=5; distance=10 > 5 → Success
            var (world, self, threat, channel) = BuildWorld(
                selfPos:      Vector3.Zero,
                threatPos:    new Vector3(10f, 0f, 0f),
                safeDistance: 5f,
                speed:        5f);

            var executor = new FleeExecutor();
            executor.OnEnter(self, ref channel, world);
            AdvanceTick(world);
            executor.Execute(self, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// When the threat entity is destroyed between ticks,
        /// <see cref="FleeExecutor.Execute"/> detects the stale handle via the generational check
        /// (<c>world.IsAlive</c>) and reports <see cref="NodeStatus.Success"/> (threat eliminated).
        /// <para>
        /// This is the critical end-to-end proof that the DEBT-009 generational safety fix
        /// propagates through to live executor behaviour. The <see cref="Entity"/> stored in
        /// <see cref="FleeParams.Threat"/> retains the original generation value; after the
        /// entity is destroyed, <c>world.IsAlive</c> returns <c>false</c> because the stored
        /// generation no longer matches the world's generation table for that index.
        /// </para>
        /// </summary>
        [Fact]
        public void FleeExecutor_ReportsSuccess_WhenThreatEntityIsDead()
        {
            // self at origin, threat close (within safe distance) — would be Running normally.
            var (world, self, threat, channel) = BuildWorld(
                selfPos:      Vector3.Zero,
                threatPos:    new Vector3(2f, 0f, 0f),
                safeDistance: 20f,
                speed:        5f);

            var executor = new FleeExecutor();
            executor.OnEnter(self, ref channel, world);

            // Verify the executor is running while the threat is alive.
            AdvanceTick(world);
            executor.Execute(self, ref channel, world, 0.016f);
            Assert.Equal(NodeStatus.Running, channel.Status);

            // Destroy the threat — its generation is bumped in the world's entity index.
            world.DestroyEntity(threat);

            // The FleeParams.Threat still holds the old Entity handle (old generation).
            // IsAlive will return false → executor must report Success.
            AdvanceTick(world);
            executor.Execute(self, ref channel, world, 0.016f);
            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="FleeExecutor"/> recalculates the flee vector every
        /// <see cref="NavigationConstants.FleeReplanIntervalTicks"/> ticks.
        /// After the interval has elapsed, <see cref="NavState.FinalDestination"/> must be
        /// different from its value immediately after the initial plan.
        /// </summary>
        [Fact]
        public void FleeExecutor_RecalculatesFleeVector_AfterThrottlePeriod()
        {
            // self at origin, threat starts to the east → flee direction is west.
            var (world, self, threat, channel) = BuildWorld(
                selfPos:      Vector3.Zero,
                threatPos:    new Vector3(5f, 0f, 0f),   // threat east of self
                safeDistance: 50f,
                speed:        5f);

            var executor = new FleeExecutor();

            // Tick 0: OnEnter computes the initial flee destination (self flees west).
            executor.OnEnter(self, ref channel, world);
            var destAfterEnter = world.GetComponent<NavState>(self).FinalDestination;

            // Run FleeReplanIntervalTicks - 1 ticks without moving anything → no replan yet.
            for (int i = 1; i < NavigationConstants.FleeReplanIntervalTicks; i++)
            {
                AdvanceTick(world);
                executor.Execute(self, ref channel, world, 0.016f);
            }
            var destBeforeReplan = world.GetComponent<NavState>(self).FinalDestination;
            Assert.Equal(destAfterEnter, destBeforeReplan); // no replan yet

            // Move the threat to the north just before the replan tick.
            world.SetComponent(threat, new SimTransform
            {
                Position = new Vector3(0f, 5f, 0f),  // now north of self
                Rotation = Quaternion.Identity
            });

            // Tick FleeReplanIntervalTicks: replan fires with threat in the north → flee south.
            AdvanceTick(world);
            executor.Execute(self, ref channel, world, 0.016f);
            var destAfterReplan = world.GetComponent<NavState>(self).FinalDestination;

            // Destinations must differ because the threat moved.
            Assert.NotEqual(destBeforeReplan, destAfterReplan);
        }
    }
}
