using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.SimHost.Serializers;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// B4 — a ZONE is an ordinary authored entity and rides the ORDINARY save gate.
    ///
    /// <para>⭐ The point of the whole model: a zone is a <c>TkbIdentity</c> + a <c>SimTransform</c> + an
    /// <c>EditablePolyline</c>, so it needs no bespoke persistence, no <c>Zones</c> section and no
    /// zone DTO — the entity IS the definition. These rails assert that claim instead of assuming it,
    /// and pin the one thing that must NOT ride along: the node-local load marker.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2, §2.1; DESIGN_Distributed_Scenario_Persistence §6.
    /// </summary>
    // ⚠⚠ QA-008 — this class calls ComponentTypeRegistry.Clear(), which wipes a PROCESS-GLOBAL
    //    dictionary. This assembly's collections run in PARALLEL, so without joining the serial
    //    collection the clear deletes component registrations that other classes have already made and
    //    are about to look up by name — and the failure lands on THEM, not here.
    //    📐 Measured on this very batch: omitting this attribute reddened
    //    EditLoadClusterOpHandlerTests and ReplayLoadClusterOpHandlerTests (which this batch never
    //    touched) plus the rail that polices the rule.
    [Collection(ComponentTypeRegistryMutatorCollection.Name)]
    public sealed class ZoneEntityPersistenceTests
    {
        private const string SubsystemType = "Test.Scenario";

        private static ScenarioSerializer BuildSerializer()
            => new ScenarioSerializerBuilder(SubsystemType)
                .RegisterTranslator(new EditablePolylineTranslator())
                .Build();

        private static void RegisterZoneComponents(EntityRepository repo)
        {
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<TerrainAssetLoadState>();
            repo.RegisterManagedComponent<EditablePolyline>();
        }

        private static Entity FirstZone(EntityRepository repo)
        {
            for (int i = 0; i <= repo.MaxEntityIndex; i++)
            {
                var candidate = new Entity(i, repo.GetEntityIndex().GetMetadata(i).Generation);
                if (repo.IsAlive(candidate) && repo.HasComponent<TkbIdentity>(candidate))
                    return candidate;
            }
            return Entity.Null;
        }

        [Fact]
        public void AZoneEntity_RoundTrips_WithItsFootprintAndTkbType_AndWithoutTheLoadMarker()
        {
            ComponentTypeRegistry.Clear();
            using var repo = new EntityRepository();
            RegisterZoneComponents(repo);

            var origin = new Vector3(670f, 473.5f, 0f);
            var points = new List<Vector2>
            {
                new(-53f, -88.5f),
                new(47f, -88.5f),
                new(47f, 11.5f),
            };

            var zone = repo.CreateEntity();
            repo.AddComponent(zone, new TkbIdentity { TkbType = TkbEntityTypes.TerrainZone });
            repo.AddComponent(zone, new SimTransform { Position = origin });
            repo.SetManagedComponent(zone, new EditablePolyline { Points = points });

            // A node that has already loaded this zone carries the marker. It must NOT be saved.
            repo.AddComponent(zone, new TerrainAssetLoadState
            {
                Phase      = LoadPhase.Loaded,
                SourceHash = ZoneFootprint.Compute(origin, points),
            });

            var expectedFootprint = ZoneFootprint.Compute(origin, points);

            var serializer = BuildSerializer();
            var dom  = serializer.Serialize(repo, new ScenarioHeader(SubsystemType));
            var json = dom.ToJsonString();

            // ⛔ The node-local marker is not part of the entity's definition.
            Assert.DoesNotContain("TerrainAssetLoadState", json);

            using var fresh = new EntityRepository();
            RegisterZoneComponents(fresh);
            serializer.Deserialize(fresh, dom);

            var loaded = FirstZone(fresh);
            Assert.NotEqual(Entity.Null, loaded);

            // ⭐ Same kind …
            Assert.Equal(TkbEntityTypes.TerrainZone,
                fresh.GetComponent<TkbIdentity>(loaded).TkbType);

            // ⭐ … and the SAME FOOTPRINT, which is the property the loader actually keys on.
            var loadedOrigin   = fresh.GetComponent<SimTransform>(loaded).Position;
            var loadedPolyline = ((ISimulationView)fresh).GetManagedComponentRO<EditablePolyline>(loaded)!;
            Assert.Equal(expectedFootprint,
                ZoneFootprint.Compute(loadedOrigin, loadedPolyline.Points));

            // ⭐ And the marker did not come back — a freshly loaded scenario has loaded nothing yet.
            Assert.False(fresh.HasComponent<TerrainAssetLoadState>(loaded));
        }

        [Fact]
        public void AZoneThatMoved_DoesNotMatchItsOldFootprint_AcrossASaveLoad()
        {
            // ⭐ Pins that the round trip preserves the transform precisely enough for staleness to be
            //   decidable — a save that rounded the origin would make every reloaded zone read stale.
            ComponentTypeRegistry.Clear();
            using var repo = new EntityRepository();
            RegisterZoneComponents(repo);

            var points = new List<Vector2> { new(-53f, -88.5f), new(47f, -88.5f), new(47f, 11.5f) };
            var before = ZoneFootprint.Compute(new Vector3(670f, 473.5f, 0f), points);

            var zone = repo.CreateEntity();
            repo.AddComponent(zone, new TkbIdentity { TkbType = TkbEntityTypes.TerrainZone });
            repo.AddComponent(zone, new SimTransform { Position = new Vector3(770f, 473.5f, 0f) }); // moved
            repo.SetManagedComponent(zone, new EditablePolyline { Points = points });

            var serializer = BuildSerializer();
            var dom = serializer.Serialize(repo, new ScenarioHeader(SubsystemType));

            using var fresh = new EntityRepository();
            RegisterZoneComponents(fresh);
            serializer.Deserialize(fresh, dom);

            var loaded         = FirstZone(fresh);
            var loadedOrigin   = fresh.GetComponent<SimTransform>(loaded).Position;
            var loadedPolyline = ((ISimulationView)fresh).GetManagedComponentRO<EditablePolyline>(loaded)!;
            var after          = ZoneFootprint.Compute(loadedOrigin, loadedPolyline.Points);

            Assert.NotEqual(before, after);
        }
    }
}
