using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Executors;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests.ExecutorTests
{
    /// <summary>
    /// Unit tests for <see cref="PlanRouteExecutor"/> (DD-Tests-Nav §4.5, rows 7-9).
    /// </summary>
    public class PlanRouteExecutorTests
    {
        private static (EntityRepository world, Entity entity, LocomotionChannel channel)
            BuildWorld(Vector2 destination, float arrivalRadius, float speed, uint existingIntentId = 0)
        {
            var world  = NavigationTestWorldFactory.Create();
            var entity = world.CreateEntity();

            world.AddComponent(entity, new NavigationIntent { IntentId = existingIntentId });
            world.AddComponent(entity, new NavigationStatus());
            world.AddComponent(entity, new LocomotionChannel());

            var channel = world.GetComponent<LocomotionChannel>(entity);
            channel.ActiveAction = NavigationConstants.ActionIdPlanRoute;

            unsafe
            {
                var p = new PlanRouteParams
                {
                    Destination   = destination,
                    ArrivalRadius = arrivalRadius,
                    Speed         = speed,
                };
                Unsafe.Write(Unsafe.AsPointer(ref channel.Params[0]), p);
            }

            world.SetComponent(entity, channel);
            channel = world.GetComponent<LocomotionChannel>(entity);
            return (world, entity, channel);
        }

        // ── Test 1: OnEnter writes intent ────────────────────────────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 7: PlanRoute_WritesNavigationIntent_ActiveActionPlanRoute.
        /// OnEnter must increment IntentId, set Mode=None, copy Destination, and set Running.
        /// </summary>
        [Fact]
        public void PlanRouteExecutor_OnEnter_WritesNavigationIntent()
        {
            var destination = new Vector2(500f, 200f);
            var (world, entity, channel) = BuildWorld(destination, arrivalRadius: 10f, speed: 0f,
                                                      existingIntentId: 3);

            var executor = new PlanRouteExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);

            Assert.Equal(4u,                        intent.IntentId);
            Assert.Equal(NavigationMode.None,       intent.Mode);
            Assert.Equal(destination,               intent.FinalDestination);
            Assert.Equal(NodeStatus.Running,        channel.Status);
        }

        // ── Test 2: PathFound → Success ────────────────────────────────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 8: PlanRoute_StatusPathFound_ReturnsBTreeSuccess.
        /// </summary>
        [Fact]
        public void PlanRouteExecutor_Execute_PathFound_ReturnsSuccess()
        {
            var (world, entity, channel) = BuildWorld(
                new Vector2(100f, 0f), arrivalRadius: 5f, speed: 0f);

            var executor = new PlanRouteExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = intent.IntentId,
                Result   = NavigationResult.PathFound,
            });

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Success, channel.Status);
        }

        // ── Test 3: NoPath → Failure ────────────────────────────────────────────────────────────────

        /// <summary>
        /// DD-Tests-Nav §4.5 row 9: PlanRoute_StatusNoPath_ReturnsBTreeFailure.
        /// </summary>
        [Fact]
        public void PlanRouteExecutor_Execute_NoPath_ReturnsFailure()
        {
            var (world, entity, channel) = BuildWorld(
                new Vector2(100f, 0f), arrivalRadius: 5f, speed: 0f);

            var executor = new PlanRouteExecutor();
            executor.OnEnter(entity, ref channel, world);

            var intent = world.GetComponent<NavigationIntent>(entity);
            world.SetComponent(entity, new NavigationStatus
            {
                IntentId = intent.IntentId,
                Result   = NavigationResult.NoPath,
            });

            executor.Execute(entity, ref channel, world, 0.016f);

            Assert.Equal(NodeStatus.Failure, channel.Status);
        }
    }
}
