using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Scenario;
using Hrot.IG.Components;
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
                var candidate = new Entity(i, freshRepo.GetHeader(i).Generation);
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
    }
}
