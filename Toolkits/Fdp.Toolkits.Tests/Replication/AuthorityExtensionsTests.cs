using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Toolkit.Replication.Tests
{
    /// <summary>
    /// Unit tests that pin the <see cref="AuthorityExtensions.HasAuthority"/> contract.
    ///
    /// <b>TD-3 purpose:</b> The old implementation returned <c>false</c> when
    /// <see cref="NetworkAuthority"/> was absent, which was inconsistent with the
    /// "AllInOne / no-network" intent expressed in the comment.  These tests document
    /// and enforce the corrected contract.
    /// </summary>
    public class AuthorityExtensionsTests : IDisposable
    {
        private readonly EntityRepository _world;

        public AuthorityExtensionsTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<NetworkAuthority>();
        }

        public void Dispose()
        {
            _world.Dispose();
        }

        // ── Absent NetworkAuthority ───────────────────────────────────────────

        /// <summary>
        /// When no <see cref="NetworkAuthority"/> component is present the entity is
        /// treated as locally authoritative (AllInOne / unit-test topology).
        /// </summary>
        [Fact]
        public void HasAuthority_ReturnsTrueWhenNetworkAuthorityAbsent()
        {
            var entity = _world.CreateEntity();

            bool result = ((ISimulationView)_world).HasAuthority(entity);

            Assert.True(result,
                "HasAuthority must return true when NetworkAuthority component is absent " +
                "(AllInOne / unit-test topology — assume local authority).");
        }

        // ── NetworkAuthority present, local owner ─────────────────────────────

        /// <summary>
        /// When <see cref="NetworkAuthority.PrimaryOwnerId"/> equals
        /// <see cref="NetworkAuthority.LocalNodeId"/> the entity is locally owned.
        /// </summary>
        [Fact]
        public void HasAuthority_ReturnsTrueWhenLocallyOwned()
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));

            bool result = ((ISimulationView)_world).HasAuthority(entity);

            Assert.True(result);
        }

        // ── NetworkAuthority present, remote owner ────────────────────────────

        /// <summary>
        /// When <see cref="NetworkAuthority.PrimaryOwnerId"/> differs from
        /// <see cref="NetworkAuthority.LocalNodeId"/> the entity is remotely owned.
        /// </summary>
        [Fact]
        public void HasAuthority_ReturnsFalseWhenRemotelyOwned()
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));

            bool result = ((ISimulationView)_world).HasAuthority(entity);

            Assert.False(result,
                "HasAuthority must return false when PrimaryOwnerId != LocalNodeId.");
        }

        // ── Dead entity ───────────────────────────────────────────────────────

        /// <summary>
        /// A destroyed entity must never be treated as authoritative regardless of
        /// the absence of <see cref="NetworkAuthority"/>.
        /// </summary>
        [Fact]
        public void HasAuthority_ReturnsFalseForDeadEntity()
        {
            var entity = _world.CreateEntity();
            _world.DestroyEntity(entity);

            bool result = ((ISimulationView)_world).HasAuthority(entity);

            Assert.False(result,
                "HasAuthority must return false for a destroyed entity.");
        }
    }
}
