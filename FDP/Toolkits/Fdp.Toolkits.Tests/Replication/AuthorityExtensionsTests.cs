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

        // ── Child part follows its parent (CE-275 ② / OQ7) ────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b>CE-275 OQ7 — a child <see cref="PartMetadata"/> entity's authority (and thus its
        /// scenario save-eligibility) follows its ROOT parent's ownership.</b> The distributed save gate
        /// (<c>ScenarioSerializer.CollectSaveableEntities</c>) calls <c>HasAuthority</c>, so this is what
        /// makes a part ride the same gate as its parent rather than being saved/skipped independently.
        /// 📄 <c>docs/DESIGN_Distributed_Scenario_Persistence.md</c> §6.
        /// </summary>
        [Fact]
        public void HasAuthority_ChildPart_FollowsParentOwnership()
        {
            _world.RegisterComponent<PartMetadata>();

            // Remotely-owned parent ⇒ its child part is NOT authoritative (excluded from our save).
            var foreignParent = _world.CreateEntity();
            _world.AddComponent(foreignParent, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            var foreignChild = _world.CreateEntity();
            _world.AddComponent(foreignChild, new PartMetadata { ParentEntity = foreignParent });

            Assert.False(((ISimulationView)_world).HasAuthority(foreignChild),
                "a child part of a remotely-owned parent must not be authoritative — the save gate " +
                "follows the parent, so the part is not written by a non-owning host.");

            // Locally-owned parent ⇒ its child part IS authoritative (saved with the parent).
            var ownedParent = _world.CreateEntity();
            _world.AddComponent(ownedParent, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            var ownedChild = _world.CreateEntity();
            _world.AddComponent(ownedChild, new PartMetadata { ParentEntity = ownedParent });

            Assert.True(((ISimulationView)_world).HasAuthority(ownedChild),
                "a child part of a locally-owned parent must be authoritative.");
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
