using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Hrot.AI.Doctrines.Mappers;
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
        /// MilitaryApc entity must map to the "ConvoyEscort" doctrine.
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
            Assert.Equal("ConvoyEscort", assignment.DoctrineName);
            Assert.Equal(entity, assignment.Entity);
        }

        /// <summary>
        /// InfantrySoldier entity must map to the "InfantryCombat" doctrine.
        /// </summary>
        [Fact]
        public void TryMap_InfantrySoldier_ReturnsInfantryCombat()
        {
            using var repo = CreateTestWorld();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new TkbIdentity { TkbType = TkbEntityTypes.InfantrySoldier });

            var result = new DefendAreaMapper().TryMap(entity, repo, "{}", out var assignment);

            Assert.True(result);
            Assert.NotNull(assignment);
            Assert.Equal("InfantryCombat", assignment.DoctrineName);
            Assert.Equal(entity, assignment.Entity);
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
