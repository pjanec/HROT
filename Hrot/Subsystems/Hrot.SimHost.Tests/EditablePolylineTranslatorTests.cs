using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Common.Components;
using Hrot.SimHost.Serializers;
using Xunit;

namespace Hrot.SimHost.Tests
{
    public sealed class EditablePolylineTranslatorTests
    {
        private const string SubsystemType = "Test.Scenario";

        [Fact]
        public void RoundTrip_ViaScenarioSerializer_PreservesPolylinePointsAndVersion()
        {
            using var repo = new EntityRepository();
            repo.RegisterManagedComponent<EditablePolyline>();

            var entity = repo.CreateEntity();
            repo.SetManagedComponent(entity, new EditablePolyline
            {
                Points = new List<Vector2>
                {
                    new(10f, 20f),
                    new(30.5f, -4.25f),
                    new(-8f, 0f),
                },
                Version = 7,
            });

            var serializer = new ScenarioSerializerBuilder(SubsystemType)
                .RegisterTranslator(new EditablePolylineTranslator())
                .Build();

            var dom = serializer.Serialize(repo, new ScenarioHeader(SubsystemType));

            using var freshRepo = new EntityRepository();
            freshRepo.RegisterManagedComponent<EditablePolyline>();
            serializer.Deserialize(freshRepo, dom);

            Entity loaded = Entity.Null;
            for (int i = 0; i <= freshRepo.MaxEntityIndex; i++)
            {
                var candidate = new Entity(i, freshRepo.GetEntityIndex().GetMetadata(i).Generation);
                if (freshRepo.IsAlive(candidate))
                {
                    loaded = candidate;
                    break;
                }
            }

            Assert.NotEqual(Entity.Null, loaded);
            Assert.True(freshRepo.HasManagedComponent<EditablePolyline>(loaded));

            var polyline = ((ISimulationView)freshRepo).GetManagedComponentRO<EditablePolyline>(loaded)!;
            Assert.Equal(7, polyline.Version);
            Assert.Equal(3, polyline.Points.Count);
            Assert.Equal(10f, polyline.Points[0].X, 3);
            Assert.Equal(20f, polyline.Points[0].Y, 3);
            Assert.Equal(30.5f, polyline.Points[1].X, 3);
            Assert.Equal(-4.25f, polyline.Points[1].Y, 3);
            Assert.Equal(-8f, polyline.Points[2].X, 3);
            Assert.Equal(0f, polyline.Points[2].Y, 3);
        }

        /// <summary>
        /// ⭐ T-2 (CE-4429 G-1) — a terrain FEATURE entity survives the scenario save / load: its unmanaged <c>TerrainFeature</c> (every field, the new <c>Seed</c> and the widened <c>Kind</c> included) and the
        /// <c>EditablePolyline</c> that carries its shape. The component stays far below the 1 024-byte cap. 🔴 Red-proof: drop <c>Seed</c> from the component ⇒ the round trip reads 0 (and this does not compile —
        /// so the mutation is: have the serializer skip the component ⇒ nothing is loaded).
        /// </summary>
        [Fact]
        public void T2_ATerrainFeature_WithItsShape_RoundTripsThroughTheScenarioSerializer()
        {
            using var repo = new EntityRepository();
            repo.RegisterManagedComponent<EditablePolyline>();
            repo.RegisterComponent<Fdp.Toolkit.Terrain.TerrainFeature>();
            repo.RegisterComponent<SimTransform>();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = new Vector3(40f, 50f, 0f), Rotation = Quaternion.Identity });
            repo.AddComponent(entity, new Fdp.Toolkit.Terrain.TerrainFeature { Kind = Fdp.Toolkit.Terrain.TerrainFeature.Scatter, Template = new FixedString32("litter"), HeadingDeg = 12.5f, Pad = 3.9f, Seed = 4_000_000_007u });
            repo.SetManagedComponent(entity, new EditablePolyline { Points = new List<Vector2> { new(-30, -30), new(30, -30), new(0, 30) } });

            var serializer = new ScenarioSerializerBuilder(SubsystemType).RegisterTranslator(new EditablePolylineTranslator()).Build();
            var dom = serializer.Serialize(repo, new ScenarioHeader(SubsystemType));
            var saved = System.Text.Json.Nodes.JsonNode.Parse(dom.ToJsonString())!.ToJsonString();
            Assert.Contains("\"Seed\"", saved);
            Assert.Contains("4000000007", saved);

            using var fresh = new EntityRepository();
            fresh.RegisterManagedComponent<EditablePolyline>();
            fresh.RegisterComponent<Fdp.Toolkit.Terrain.TerrainFeature>();
            fresh.RegisterComponent<SimTransform>();
            serializer.Deserialize(fresh, dom);

            Entity loaded = Entity.Null;
            for (int i = 0; i <= fresh.MaxEntityIndex; i++)
            {
                var c = new Entity(i, fresh.GetEntityIndex().GetMetadata(i).Generation);
                if (fresh.IsAlive(c)) { loaded = c; break; }
            }
            Assert.NotEqual(Entity.Null, loaded);
            var f = fresh.GetComponentRO<Fdp.Toolkit.Terrain.TerrainFeature>(loaded);
            Assert.Equal(Fdp.Toolkit.Terrain.TerrainFeature.Scatter, f.Kind);
            Assert.Equal("litter", f.Template.ToString());
            Assert.Equal(12.5f, f.HeadingDeg, 3);
            Assert.Equal(3.9f, f.Pad, 3);
            Assert.Equal(4_000_000_007u, f.Seed);
            Assert.Equal(3, ((ISimulationView)fresh).GetManagedComponentRO<EditablePolyline>(loaded)!.Points.Count);
            Assert.InRange(System.Runtime.CompilerServices.Unsafe.SizeOf<Fdp.Toolkit.Terrain.TerrainFeature>(), 1, 1024);
        }
    }
}
