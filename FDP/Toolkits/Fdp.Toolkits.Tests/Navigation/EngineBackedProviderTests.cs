using System;
using System.Numerics;
using Fdp.Toolkit.Navigation.EngineBacked;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    public class EngineBackedNavmeshProviderTests
    {
        [Fact]
        public void IsWalkable_AnyPoint_ReturnsTrue()
        {
            var p = new EngineBackedNavmeshProvider();
            Assert.True(p.IsWalkable(new Vector3(999f, 0f, 999f)));
        }

        [Fact]
        public void ProjectToNavmesh_PreservesInputPosition()
        {
            var p = new EngineBackedNavmeshProvider();
            Assert.True(p.ProjectToNavmesh(new Vector3(5f, 10f, 3f), out var snapped));
            Assert.Equal(new Vector3(5f, 10f, 3f), snapped);
        }

        [Fact]
        public void PathCost_ReturnsEuclideanDistance()
        {
            var p    = new EngineBackedNavmeshProvider();
            var from = new Vector3(0f, 0f, 0f);
            var to   = new Vector3(3f, 0f, 4f); // 5 metres away
            Assert.Equal(5f, p.PathCost(from, to), precision: 4);
        }

        [Fact]
        public void QueryVersion_ReturnsOne()
        {
            var p = new EngineBackedNavmeshProvider();
            Assert.Equal(1u, p.QueryVersion());
        }

        [Fact]
        public void PlanPath_ReturnsTwoWaypoints_StartAndEnd()
        {
            var p    = new EngineBackedNavmeshProvider();
            var from = new Vector3(0f, 0f, 0f);
            var to   = new Vector3(10f, 0f, 0f);
            var buf  = new NavWaypoint[4];

            int count = p.PlanPath(from, to, buf);

            Assert.Equal(2, count);
            Assert.Equal(from, buf[0].Position);
            Assert.Equal(to, buf[1].Position);
        }

        [Fact]
        public void PlanPath_SmallBuffer_ReturnsZero()
        {
            var p   = new EngineBackedNavmeshProvider();
            var buf = new NavWaypoint[1];
            Assert.Equal(0, p.PlanPath(Vector3.Zero, Vector3.One, buf));
        }
    }

    public class EngineBackedDtCrowdProviderTests
    {
        [Fact]
        public void GetAgentVelocity_ReturnsZero()
        {
            var p = new EngineBackedDtCrowdProvider();
            Assert.Equal(Vector3.Zero, p.GetAgentVelocity(new Fdp.Core.Entity(1, 0)));
        }

        [Fact]
        public void RegisterAgent_ReturnsTrue()
        {
            var p = new EngineBackedDtCrowdProvider();
            var result = p.RegisterAgent(
                new Fdp.Core.Entity(1, 0),
                new CrowdAgentParams { Radius = 0.5f, MaxSpeed = 5f });
            Assert.True(result);
        }

        [Fact]
        public void TryGetAgentSnapshot_ReturnsFalse()
        {
            var p = new EngineBackedDtCrowdProvider();
            Assert.False(p.TryGetAgentSnapshot(new Fdp.Core.Entity(1, 0), out _));
        }
    }

    public class EngineBackedVolumetricPathProviderTests
    {
        [Fact]
        public void IsFlyable_AnyPoint_ReturnsTrue()
        {
            var p = new EngineBackedVolumetricPathProvider();
            Assert.True(p.IsFlyable(new Vector3(0f, 100f, 0f)));
        }

        [Fact]
        public void PlanPath_ReturnsTwoWaypoints()
        {
            var p   = new EngineBackedVolumetricPathProvider();
            var buf = new NavWaypoint[4];
            int cnt = p.PlanPath(Vector3.Zero, new Vector3(0f, 10f, 0f), buf);
            Assert.Equal(2, cnt);
        }

        [Fact]
        public void PlanPath_SmallBuffer_ReturnsZero()
        {
            var p   = new EngineBackedVolumetricPathProvider();
            var buf = new NavWaypoint[1];
            Assert.Equal(0, p.PlanPath(Vector3.Zero, Vector3.One, buf));
        }
    }
}
