using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Common.Components;
using Hrot.SimHost.Serializers;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// G1 / BP-518 — route GEOMETRY must survive a scenario save.
    ///
    /// <para>⭐ A new class rather than a fold-in: <c>RoutePlanTranslator</c> is a new translator and had
    /// no suite. <c>EditablePolylineTranslatorTests</c> is the AREA translator's suite — a sibling, not
    /// this feature — and routes and areas are deliberately NOT unified (they are semantically
    /// different: relative points vs absolute waypoints). The fixture shape is mirrored from it.</para>
    ///
    /// <para>⛔ THE DEFECT: <c>PersonalRouteRefTranslator</c> existed and this one did not, so the LINK
    /// persisted while the GEOMETRY did not — a saved scenario reloaded as a vehicle pointing at a route
    /// with zero waypoints, silently.</para>
    ///
    /// 📄 docs/designs/routes-1/ROUTES1-DESIGN.md §16, §4.
    /// </summary>
    public sealed class RoutePlanTranslatorTests
    {
        private const string SubsystemType = "Test.Scenario";

        private static (ScenarioSerializer serializer, EntityRepository fresh) RoundTrip(RoutePlan plan)
        {
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<RoutePlan>();

            var entity = repo.CreateEntity();
            repo.SetManagedComponent(entity, plan);

            var serializer = new ScenarioSerializerBuilder(SubsystemType)
                .RegisterTranslator(new RoutePlanTranslator())
                .Build();

            var dom = serializer.Serialize(repo, new ScenarioHeader(SubsystemType));
            repo.Dispose();

            var fresh = new EntityRepository();
            fresh.RegisterManagedComponent<RoutePlan>();
            serializer.Deserialize(fresh, dom);
            return (serializer, fresh);
        }

        private static RoutePlan LoadedPlan(EntityRepository fresh)
        {
            for (int i = 0; i <= fresh.MaxEntityIndex; i++)
            {
                var candidate = new Entity(i, fresh.GetEntityIndex().GetMetadata(i).Generation);
                if (fresh.IsAlive(candidate) && fresh.HasManagedComponent<RoutePlan>(candidate))
                    return ((ISimulationView)fresh).GetManagedComponentRO<RoutePlan>(candidate)!;
            }

            Assert.Fail("no entity carrying a RoutePlan survived the round trip");
            return null!;
        }

        [Fact]
        public void RoundTrip_PreservesWaypointsAndLoopFlag()
        {
            var plan = new RoutePlan { IsLoop = true };
            plan.Mutate(w =>
            {
                w.Add(new RouteWaypoint { Position = new Vector3(10f, 20f, 1.5f),   TargetSpeed = 8f });
                w.Add(new RouteWaypoint { Position = new Vector3(30.5f, -4.25f, 0f), TargetSpeed = 0f });
                w.Add(new RouteWaypoint { Position = new Vector3(-8f, 0f, -2f),      TargetSpeed = 12.25f });
            });

            var (_, fresh) = RoundTrip(plan);
            using (fresh)
            {
                var loaded = LoadedPlan(fresh);

                // ⭐ THE REQUIREMENT: the route is NOT EMPTY on the far side.
                Assert.Equal(3, loaded.Waypoints.Count);
                Assert.True(loaded.IsLoop);

                Assert.Equal(10f,   loaded.Waypoints[0].Position.X, 3);
                Assert.Equal(20f,   loaded.Waypoints[0].Position.Y, 3);
                Assert.Equal(1.5f,  loaded.Waypoints[0].Position.Z, 3);
                Assert.Equal(8f,    loaded.Waypoints[0].TargetSpeed, 3);

                Assert.Equal(30.5f,  loaded.Waypoints[1].Position.X, 3);
                Assert.Equal(-4.25f, loaded.Waypoints[1].Position.Y, 3);

                Assert.Equal(-2f,     loaded.Waypoints[2].Position.Z, 3);
                Assert.Equal(12.25f,  loaded.Waypoints[2].TargetSpeed, 3);
            }
        }

        [Fact]
        public void RoundTrip_PreservesPerWaypointExtensionJson_AndKeepsNullNull()
        {
            var plan = new RoutePlan();
            plan.Mutate(w =>
            {
                w.Add(new RouteWaypoint
                {
                    Position      = new Vector3(1f, 2f, 0f),
                    ExtensionJson = "{\"dangerLevel\":2,\"tacticalStance\":\"cautious\"}",
                });
                w.Add(new RouteWaypoint { Position = new Vector3(3f, 4f, 0f), ExtensionJson = null });
            });

            var (_, fresh) = RoundTrip(plan);
            using (fresh)
            {
                var loaded = LoadedPlan(fresh);

                Assert.Equal("{\"dangerLevel\":2,\"tacticalStance\":\"cautious\"}",
                    loaded.Waypoints[0].ExtensionJson);

                // ⚠ null must stay null — not "", not "null", not an empty object.
                Assert.Null(loaded.Waypoints[1].ExtensionJson);
            }
        }

        [Fact]
        public void Inject_GoesThroughMutate_SoVersionIsAdvancedNotLeftAtZero()
        {
            // ⭐ Waypoints is an IReadOnlyList over a private list: the ONLY way to fill it is Mutate,
            //   which is also the mutation contract that keeps Version meaningful for reactive systems
            //   such as RouteTrajectorySyncSystem.
            var plan = new RoutePlan();
            plan.Mutate(w => w.Add(new RouteWaypoint { Position = Vector3.Zero }));

            var (_, fresh) = RoundTrip(plan);
            using (fresh)
            {
                var loaded = LoadedPlan(fresh);

                Assert.NotEmpty(loaded.Waypoints);
                Assert.True(loaded.Version > 0,
                    "a loaded route must have been filled through Mutate(); Version still 0 means the "
                  + "translator wrote the backing list some other way and reactive systems would miss it");
            }
        }

        [Fact]
        public void RoundTrip_OfAnEmptyRoute_IsStillARouteAndNotAFailure()
        {
            var (_, fresh) = RoundTrip(new RoutePlan());
            using (fresh)
            {
                var loaded = LoadedPlan(fresh);
                Assert.Empty(loaded.Waypoints);
                Assert.False(loaded.IsLoop);
            }
        }
    }
}
