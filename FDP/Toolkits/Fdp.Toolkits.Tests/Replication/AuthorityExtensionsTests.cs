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

        // ── S6: an instance key falls back to its descriptor type, then to the primary owner ─────────────────

        /// <summary>
        /// ⭐ S6 (Q79 §0.10 ①): the record of <c>(d, 0)</c> — a group grant — covers every instance <c>(d, i)</c> that has no
        /// entry of its own; an instance moved on its own keeps its entry; with neither, the primary owner answers.
        /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.6 S6.
        /// </summary>
        [Fact]
        public void HasAuthority_InstanceKey_FallsBackToTheDescriptorType_ThenToThePrimaryOwner()
        {
            _world.RegisterComponent<PartMetadata>();
            _world.RegisterManagedComponent<DescriptorOwnership>();
            const long d = 95, other = 96;

            var root = _world.CreateEntity();
            _world.AddComponent(root, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));   // root owned by node 2
            var record = new DescriptorOwnership();
            record.SetOwner(OwnershipExtensions.PackKey(d, 0), 1);                                // group (d) granted to us
            record.SetOwner(OwnershipExtensions.PackKey(d, 3), 2);                                // instance 3 moved back alone
            _world.SetManagedComponent(root, record);
            var part = _world.CreateEntity();
            _world.AddComponent(part, new PartMetadata { ParentEntity = root, InstanceId = 2 });

            var view = (ISimulationView)_world;
            Assert.True(view.HasAuthority(part, OwnershipExtensions.PackKey(d, 2)));       // (d,2) → (d,0) = us
            Assert.False(view.HasAuthority(part, OwnershipExtensions.PackKey(d, 3)));      // its own entry wins
            Assert.False(view.HasAuthority(part, OwnershipExtensions.PackKey(other, 2)));  // no entry at all → primary (2)
            Assert.False(view.HasAuthority(part, other));   // ⛔ CE-507: the raw ordinal is (0, d) and matches nothing
        }

        // ── Dead entity ───────────────────────────────────────────────────────

        /// <summary>
        /// A destroyed entity must never be treated as authoritative regardless of
        /// the absence of <see cref="NetworkAuthority"/>.
        /// </summary>
        /// <summary>⭐ S8 — the INGRESS form treats a descriptor this node is HANDING OVER as not owned: the creator's record
        /// stays "mine" until the grantee confirms (F7), but the grantee is already writing, and skipping its first sample
        /// loses it for good (nothing republishes an unchanged value). Once the handover settles the record decides again.
        /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.6 S8.</summary>
        [Fact]
        public void IsRecordedOwner_IsFalse_WhileThisNodeIsHandingTheDescriptorOver()
        {
            _world.RegisterManagedComponent<OutgoingGrantsPending>();
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));   // the creator
            long damage = OwnershipExtensions.PackKey(30, 0), info = OwnershipExtensions.PackKey(2, 0);

            Assert.True(_world.IsRecordedOwner(entity, damage));

            var pending = new OutgoingGrantsPending();
            pending.Descriptors[30] = 400;                                // granted to node 400, not yet confirmed
            _world.SetManagedComponent(entity, pending);

            Assert.False(_world.IsRecordedOwner(entity, damage));         // take the grantee's samples
            Assert.True(_world.IsRecordedOwner(entity, info));            // a descriptor it keeps is still its own
            Assert.True(((ISimulationView)_world).HasAuthority(entity, damage));   // the EGRESS gate is unchanged (F7)
        }

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
