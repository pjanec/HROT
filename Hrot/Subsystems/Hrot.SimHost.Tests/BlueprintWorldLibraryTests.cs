using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Combat.Components;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Services;
using Hrot.AI.Behaviors.Brains;
using Hrot.AI.Behaviors.StandardLibrary;
using Hrot.Core.Mission;
using Hrot.Editor.AiShared;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-469</c> rails — the built-in blueprint function library (<see cref="BlueprintWorldLibrary"/>).
    /// 📄 <c>docs/blueprints/Architect_Question_78_Hill_Attack_The_Blueprint_Node_Way.md</c> §6.2, §7 row 4.
    /// </summary>
    public class BlueprintWorldLibraryTests
    {
        private static EntityRepository World()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<Health>();
            return repo;
        }

        /// <summary>🔒 User <c>2026-09-30</c>: "is alive" in a doctrine means health &gt; 0; <i>Entity Exists</i> is the ECS
        /// question. A knocked-out body answers them differently — that difference is the point of having two.</summary>
        [Fact]
        public void CE469_EntityExists_And_IsAliveInCombat_DisagreeOnAKnockedOutBody()
        {
            using var repo = World();
            var body = repo.CreateEntity();
            repo.AddComponent(body, new Health { Current = 0f, Max = 100f });
            var healthy = repo.CreateEntity();
            repo.AddComponent(healthy, new Health { Current = 50f, Max = 100f });
            var noHealth = repo.CreateEntity();

            Assert.True(BlueprintWorldLibrary.EntityExists(body, repo));
            Assert.False(BlueprintWorldLibrary.IsAliveInCombat(body, repo));
            Assert.True(BlueprintWorldLibrary.IsAliveInCombat(healthy, repo));
            Assert.True(BlueprintWorldLibrary.IsAliveInCombat(noHealth, repo));   // no Health ⇒ existence decides

            repo.DestroyEntity(healthy);
            Assert.False(BlueprintWorldLibrary.EntityExists(healthy, repo));
            Assert.False(BlueprintWorldLibrary.IsAliveInCombat(healthy, repo));
        }

        /// <summary>The routed legacy helpers answer exactly like the built-ins (one implementation, <c>CE-469</c>).</summary>
        [Fact]
        public void CE469_LegacyHelpers_RouteToTheBuiltIns()
        {
            using var repo = World();
            var e = repo.CreateEntity();
            var map = new NetworkEntityMap();
            repo.SetSingletonManaged<NetworkEntityMap>(map);
            map.Register(77L, e);

            Assert.Equal(e, BlueprintWorldLibrary.EntityFromNetworkId(77L, repo));
            Assert.Equal(e, NetworkEntityMapOps.ResolveTarget(77L, repo));
            Assert.Equal(Entity.Null, BlueprintWorldLibrary.EntityFromNetworkId(78L, repo));
        }

        [Fact]
        public void CE469_EntityFromNetworkId_WithoutAMap_IsNull()
        {
            using var repo = World();
            Assert.Equal(Entity.Null, BlueprintWorldLibrary.EntityFromNetworkId(1L, repo));
        }

        /// <summary>Deterministic: same self/salt/time ⇒ same value; in range; salt separates two draws in one tick.</summary>
        [Fact]
        public void CE469_Random_IsDeterministic_InRange_AndSaltSeparates()
        {
            using var repo = World();
            var self = repo.CreateEntity();
            repo.SetSimulationTime(12.5f);

            for (int salt = 0; salt < 200; salt++)
            {
                int a = BlueprintWorldLibrary.RandomInt(3, 9, salt, self, repo);
                Assert.Equal(a, BlueprintWorldLibrary.RandomInt(3, 9, salt, self, repo));
                Assert.InRange(a, 3, 8);
                float f = BlueprintWorldLibrary.RandomFloat(-2f, 2f, salt, self, repo);
                Assert.Equal(f, BlueprintWorldLibrary.RandomFloat(-2f, 2f, salt, self, repo));
                Assert.InRange(f, -2f, 2f);
            }

            var distinct = Enumerable.Range(0, 50).Select(s => BlueprintWorldLibrary.RandomInt(0, 1_000_000, s, self, repo)).Distinct().Count();
            Assert.True(distinct > 40, $"salt must separate draws in one tick; got {distinct}/50 distinct");
            Assert.Equal(5, BlueprintWorldLibrary.RandomInt(5, 5, 0, self, repo));   // empty range ⇒ min
        }

        [Fact]
        public void CE469_Geo_RoundTripsThroughTheWorldTransform()
        {
            using var repo = World();
            var geo = new WGS84Transform();
            geo.SetOrigin(50.0, 14.0, 200.0);
            repo.SetSingletonManaged<IGeographicTransform>(geo);

            var p = new GeoPoint(50.001, 14.002, 210.0);
            Vector3 local = BlueprintWorldLibrary.GeoToCartesian(p, repo);
            Assert.True(local.Length() > 100f, $"a point ~150 m from the origin must not map to zero; got {local}");
            var back = BlueprintWorldLibrary.CartesianToGeo(local, repo);
            Assert.Equal(p.Latitude, back.Latitude, 5);
            Assert.Equal(p.Longitude, back.Longitude, 5);
            Assert.Equal(p.Altitude, back.Altitude, 0);
        }

        [Fact]
        public void CE469_Geo_WithoutATransform_IsNeutral()
        {
            using var repo = World();
            Assert.Equal(Vector3.Zero, BlueprintWorldLibrary.GeoToCartesian(new GeoPoint(1, 2, 3), repo));
            Assert.Equal(default(GeoPoint), BlueprintWorldLibrary.CartesianToGeo(new Vector3(1, 2, 3), repo));
        }

        /// <summary>Every public function is palette-visible, and ends with the P7 trailing context Stage5 hides.</summary>
        [Fact]
        public void CE469_EveryFunction_IsBlueprintCallable_WithTrailingContext()
        {
            var methods = typeof(BlueprintWorldLibrary).GetMethods(BindingFlags.Public | BindingFlags.Static);
            // CE-469's 7 + CE-464's RandomIntSeeded, EntityIndex, BehaviorHashOf, HasGeographicTransform, LatLonToCartesian
            Assert.Equal(12, methods.Length);
            foreach (var m in methods)
            {
                Assert.NotNull(m.GetCustomAttribute<BlueprintCallableAttribute>());
                // a world-reading function takes the view LAST (the P7 trailing context); a pure value function takes none
                var ps = m.GetParameters();
                Assert.True(ps.Length > 0);
                Assert.DoesNotContain(ps.Take(ps.Length - 1), p => p.ParameterType == typeof(Fdp.ModuleHost.Abstractions.ISimulationView));
            }
        }
    }
}
