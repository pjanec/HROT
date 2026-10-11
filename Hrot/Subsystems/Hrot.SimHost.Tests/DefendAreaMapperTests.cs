using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Hrot.AI.Behaviors.Mappers;
using Hrot.Map.Common;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="DefendAreaMapper"/> (TASK-TI011).
    /// </summary>
    public class DefendAreaMapperTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        private static EntityRepository CreateTestWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<TkbIdentity>();
            return repo;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// TargetIntentId must be "DefendArea".
        /// </summary>
        [Fact]
        public void TargetIntentId_IsDefendArea()
        {
            Assert.Equal("DefendArea", new DefendAreaMapper().TargetIntentId);
        }

        /// <summary>
        /// MilitaryApc entity must map to the "ConvoyEscort" behavior.
        /// </summary>
        [Fact]
        public void TryMap_MilitaryApc_ReturnsConvoyEscort()
        {
            using var repo = CreateTestWorld();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new TkbIdentity { TkbType = TkbEntityTypes.MilitaryApc });

            var result = new DefendAreaMapper().TryMap(entity, repo, "{}", out var assignment);

            Assert.True(result);
            Assert.NotNull(assignment);
            Assert.Equal("ConvoyEscort", assignment.BehaviorName);
            Assert.Equal(entity, assignment.Entity);
        }

        /// <summary>
        /// ⭐ <c>CE-3160</c> (L4, R-266) — an infantry soldier (and an insurgent) defends an area with the LIBRARY's
        /// <c>CombatPosture</c>, advancing to the area's centre and holding within its radius. ⛔ It mapped to <c>InfantryCombat</c>,
        /// the UrbanCombat example's tree registered on the editor only, so the order was silently dropped on CGF.
        /// </summary>
        [Theory]
        [InlineData(TkbEntityTypes.InfantrySoldier)]
        [InlineData(TkbEntityTypes.Insurgent)]
        public void TryMap_Infantry_DefendsWithCombatPosture_AtTheAreaCentre(long tkbType)
        {
            using var repo = CreateTestWorld();
            var geo = new Fdp.Modules.Geographic.Transforms.WGS84Transform();
            geo.SetOrigin(50.0, 14.0, 0.0);
            repo.SetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>(geo);
            var centre = geo.ToCartesian(50.001, 14.001, 0.0);
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new TkbIdentity { TkbType = tkbType });

            var result = new DefendAreaMapper().TryMap(entity, repo,
                "{\"CenterLat\":50.001,\"CenterLon\":14.001,\"RadiusMeters\":25}", out var assignment);

            Assert.True(result);
            Assert.Equal("CombatPosture", assignment.BehaviorName);
            var dto = System.Text.Json.JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.CombatPostureParamsJsonDto>(
                assignment.JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            Assert.Equal(centre.X, dto.Advance.Objective.X, 2);
            Assert.Equal(centre.Y, dto.Advance.Objective.Y, 2);
            Assert.Equal(25f, dto.Advance.ArrivalRadius);
        }

        /// <summary>
        /// An entity with an unknown TkbType must cause TryMap to return false.
        /// </summary>
        [Fact]
        public void TryMap_UnknownTkbType_ReturnsFalse()
        {
            using var repo = CreateTestWorld();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new TkbIdentity { TkbType = 999L });

            var result = new DefendAreaMapper().TryMap(entity, repo, "{}", out var assignment);

            Assert.False(result);
        }

        /// <summary>
        /// An entity without a <see cref="TkbIdentity"/> component must cause TryMap to return false.
        /// </summary>
        [Fact]
        public void TryMap_NoTkbIdentity_ReturnsFalse()
        {
            using var repo = CreateTestWorld();
            var entity = repo.CreateEntity();
            // TkbIdentity is NOT added

            var result = new DefendAreaMapper().TryMap(entity, repo, "{}", out var assignment);

            Assert.False(result);
        }
    }
}
