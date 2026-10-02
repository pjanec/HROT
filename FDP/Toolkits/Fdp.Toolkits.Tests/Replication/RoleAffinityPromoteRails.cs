using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Replication.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Tkb;
using Xunit;

namespace Fdp.Toolkit.Replication.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b>The PROMOTE leg under push-only: an arrived ghost claims NOTHING by itself.</b>
    /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.6 S4; <c>Architect_Question_79</c> §0.7 ③ (R-164).
    ///
    /// <para>⛔ <b>SUPERSEDED, <c>2026-10-02</c> (S4):</b> this suite used to rail the role-affinity promote leg
    /// (<c>DESIGN_Role_Affinity_Ownership.md</c> P3 step 3), where a promoter claimed its role's components and the
    /// creator declined them. Q79 retired that: several nodes may hold one role (R-162), so each would claim the
    /// same components. A promoting node's claim now comes only from a grant or a transfer. The rails that asserted
    /// the role claim and the create/promote partition were removed with the mechanism; the two below are the
    /// invariants that replace them.</para>
    /// </summary>
    public class RoleAffinityPromoteRails
    {
        private const long TkbType = 8888L;

        private static int Id<T>() => ComponentTypeRegistry.GetOrRegisterManaged(typeof(T));

        // ⭐ Reuses this suite's own MockTkbDatabase (GhostProtocolTests.cs) rather than writing a
        //   parallel stub — R-142 ④ in miniature: the feature's suite already has the fixture.

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterEvent<ConstructionOrder>();
            return repo;
        }

        /// <summary>⭐ A ghost as <c>GhostCreationSystem</c> leaves it: identity, tracker, and the
        /// replicated components — owning NOTHING.</summary>
        private static Entity Ghost(EntityRepository repo)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new TkbIdentity { TkbType = TkbType });
            repo.AddComponent(e, new GhostStateTracker { FirstSeenFrame = 0 });
            repo.AddComponent(e, new NetworkIdentity(4242));
            repo.AddComponent(e, new SimTransform());
            repo.AddComponent(e, new SimVelocity());
            repo.SetLifecycleState(e, EntityLifecycle.Ghost);
            return e;
        }

        private static Entity Promote(EntityRepository repo)
        {
            // ⭐ SimTransform is birth-critical by DERIVATION ([BirthCritical] on the component type),
            //   so this template carries the exemption without declaring it — §6.6a.
            var template = new TkbTemplate("PromotionSubject", TkbType);

            var tkb = new MockTkbDatabase { TemplateToReturn = template };
            var sys = new GhostPromotionSystem(
                tkb, new EntityLifecycleModule(tkb, Array.Empty<int>()),
                translators: Array.Empty<ITkbEntityTranslator>());

            var e = Ghost(repo);
            sys.Execute(repo, 0f);
            Assert.Equal(EntityLifecycle.Constructing, repo.GetLifecycleState(e));
            return e;
        }

        private static bool Owns(EntityRepository repo, Entity e, int componentId)
            => repo.GetMetadata(e.Index).AuthorityMask.IsSet(componentId);

        /// <summary>
        /// ⭐⭐⭐ <b>A promoted ghost claims NOTHING — not its kinematics, not its identity, not a birth-critical
        /// component.</b> Before S4 a promoter claimed its role's components (measured on CGF: a SimHost-created
        /// tank's ghost claimed the master, info, kinematics and perception components it did not own).
        /// </summary>
        [Fact]
        public void APromotedGhostClaimsNothing()
        {
            var repo = CreateWorld();
            var e    = Promote(repo);

            Assert.False(Owns(repo, e, Id<SimTransform>()));
            Assert.False(Owns(repo, e, Id<SimVelocity>()));
            Assert.False(Owns(repo, e, Id<NetworkIdentity>()));
        }

        /// <summary>
        /// ⭐⭐ <b>An explicit grant already on the ghost SURVIVES promotion</b> — promotion leaves the authority
        /// mask alone, so a claim that came from a grant or a transfer is never revoked by it.
        /// </summary>
        [Fact]
        public void AnExplicitGrantAlreadyOnTheGhost_SurvivesPromotion()
        {
            var repo = CreateWorld();

            var template = new TkbTemplate("PromotionSubject", TkbType);
            var tkb      = new MockTkbDatabase { TemplateToReturn = template };
            var sys      = new GhostPromotionSystem(
                tkb, new EntityLifecycleModule(tkb, Array.Empty<int>()),
                translators: Array.Empty<ITkbEntityTranslator>());

            var e = Ghost(repo);
            repo.SetAuthority(e, Id<NetworkIdentity>(), true);   // as if a grant had already landed

            sys.Execute(repo, 0f);

            Assert.Equal(EntityLifecycle.Constructing, repo.GetLifecycleState(e));   // anti-vacuity: it was promoted
            Assert.True(Owns(repo, e, Id<NetworkIdentity>()), "promotion revoked a granted claim.");
            Assert.False(Owns(repo, e, Id<SimTransform>()));
        }
    }
}
