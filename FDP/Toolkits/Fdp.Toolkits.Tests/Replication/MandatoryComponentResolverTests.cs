using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Replication.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-265</c> — the DERIVED promotion gate.</b>
    /// 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §6.6a (design) · §6.6b (as-built).
    ///
    /// <para>⛔⛔ <b>Why this suite is unusually paranoid: the two failure modes are SILENT and OPPOSITE.</b>
    /// Deriving TOO MUCH gives a ghost that <b>never promotes</b> — an entity that simply never appears, with
    /// no error anywhere. Deriving TOO LITTLE promotes a ghost before its real position arrived — an origin
    /// flash and a bogus first spatial-hash cell. ⇒ every intersection below is pinned in BOTH directions, and
    /// the "excluded for the right reason" rails matter as much as the inclusion ones.</para>
    /// </summary>
    public class MandatoryComponentResolverTests
    {
        private const long WorldPosOrdinal   = 10L;
        private const long EntityInfoOrdinal = 11L;

        // ── translator doubles, shaped after the real ones ───────────────────────────────────────────

        /// <summary>Mirrors <c>SpatialCoreTkbTranslator</c>: consumes <c>TkbMasterDto</c>, produces both
        /// spatial components.</summary>
        private sealed class SpatialLike : ITkbEntityTranslator
        {
            public IEnumerable<Type> GetConsumedDescriptors() { yield return typeof(TkbMasterDto); }
            public IEnumerable<Type> GetProducedComponents()
            {
                yield return typeof(SimTransform);
                yield return typeof(SimVelocity);
            }
            public void Inject(EntityRepository repo, Entity entity, TkbTemplate template) { }
        }

        /// <summary>Mirrors <c>BehaviorTkbTranslator</c>'s relevant half: consumes
        /// <c>BehaviorProfileDto</c>, produces <c>EntityInfo</c>.</summary>
        private sealed class BehaviorLike : ITkbEntityTranslator
        {
            public IEnumerable<Type> GetConsumedDescriptors() { yield return typeof(BehaviorProfileDto); }
            public IEnumerable<Type> GetProducedComponents() { yield return typeof(EntityInfo); }
            public void Inject(EntityRepository repo, Entity entity, TkbTemplate template) { }
        }

        /// <summary>Mirrors <c>AiDiagnosticsTkbTranslator</c>: consumes NOTHING, so it runs for every
        /// template.</summary>
        private sealed class ObserverLike : ITkbEntityTranslator
        {
            public IEnumerable<Type> GetConsumedDescriptors() => Array.Empty<Type>();
            public IEnumerable<Type> GetProducedComponents() { yield return typeof(EntityInfo); }
            public void Inject(EntityRepository repo, Entity entity, TkbTemplate template) { }
        }

        /// <summary>Mirrors <c>InfantryVehicleStateStripTkbTranslator</c>: runs for the template but only
        /// REMOVES, so it produces nothing.</summary>
        private sealed class StripLike : ITkbEntityTranslator
        {
            public IEnumerable<Type> GetConsumedDescriptors() { yield return typeof(TkbMasterDto); }
            public IEnumerable<Type> GetProducedComponents() => Array.Empty<Type>();
            public void Inject(EntityRepository repo, Entity entity, TkbTemplate template) { }
        }

        // ── fixtures ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>The NED-vehicle shape: the two descriptor families every real vehicle template carries.</summary>
        private static TkbTemplate VehicleShapedTemplate(long tkbType = 7001)
        {
            var t = new TkbTemplate("VehicleShaped", tkbType);
            t.AddDescriptor(new TkbMasterDto { CustomName = "VehicleShaped" });
            t.AddDescriptor(new BehaviorProfileDto());
            return t;
        }

        /// <summary>⭐ The map as a real NED node fills it: <c>GeoSpatialEgressTranslator</c> pairs
        /// <c>SimTransform</c> with <c>dtWorldPos</c>, <c>EntityInfoEgressTranslator</c> pairs
        /// <c>EntityInfo</c>. ⛔ <c>SimVelocity</c> is deliberately absent — in production it reaches the map
        /// only through <c>RegisterMapping</c>, which records no descriptor pairing.</summary>
        private static DescriptorOwnershipMap NedLikeIngressMap()
        {
            var map = new DescriptorOwnershipMap();
            map.RegisterFromTranslator(WorldPosOrdinal,   new[] { ComponentType<SimTransform>.ID });
            map.RegisterFromTranslator(EntityInfoOrdinal, new[] { ComponentType<EntityInfo>.ID });
            return map;
        }

        private static EntityRepository RepoRegistering(bool simTransform = true,
                                                        bool simVelocity  = true,
                                                        bool entityInfo   = true)
        {
            var repo = new EntityRepository();
            if (simTransform) repo.RegisterComponent<SimTransform>();
            if (simVelocity)  repo.RegisterComponent<SimVelocity>();
            if (entityInfo)   repo.RegisterComponent<EntityInfo>();
            return repo;
        }

        private static IReadOnlyList<ITkbEntityTranslator> NedLikeTranslators()
            => new ITkbEntityTranslator[] { new SpatialLike(), new BehaviorLike() };

        // ── ① THE HEADLINE ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b>The design's central measured claim, as a rail.</b> 📄 §6.6a: a NED-vehicle-shaped
        /// template derives exactly <c>{SimTransform, EntityInfo}</c> — <i>"precisely the hand-written list on
        /// NED vehicles, and precisely what UrbanCombat drifted out of"</i>.
        ///
        /// <para>⛔ This is the rail that proves the derivation may REPLACE the two
        /// <c>AddMandatoryComponent</c> calls deleted from <c>NedTkbBuilder.DefineVehicle</c>, rather than
        /// merely coexist with them.</para>
        /// </summary>
        [Fact]
        public void AVehicleShapedTemplate_DerivesExactlySimTransformAndEntityInfo()
        {
            using var repo = RepoRegistering();

            var derived = new MandatoryComponentResolver()
                .Resolve(VehicleShapedTemplate(), repo, NedLikeTranslators(), NedLikeIngressMap());

            Assert.Equal(
                new[] { ComponentType<SimTransform>.ID, ComponentType<EntityInfo>.ID },
                SortedCopy(derived));
        }

        // ── ② THE FOUR INTERSECTIONS, EACH PINNED BY REMOVING ONE INPUT ──────────────────────────────

        /// <summary>
        /// 🔴🔴 <b>THE FOURTH INTERSECTION, and the case that forced it to exist.</b> <c>SimVelocity</c>
        /// carries <c>[PerInstanceValue]</c>, IS produced by the spatial translator, and IS registered — it is
        /// excluded solely because <b>nothing ingresses it</b>: the wire carries <c>WorldPos</c> and the
        /// ingress writes <c>NetworkVelocity</c>, never <c>SimVelocity</c>.
        ///
        /// <para>⛔ A hard requirement on it would be a ghost that never promotes, forever. ⚠ And it must be
        /// excluded for THIS reason — 🔒 §6.6a: <i>"not by mislabelling the component as 'not
        /// per-instance'"</i>, which is why <c>SimVelocity</c> keeps its attribute.</para>
        /// </summary>
        [Fact]
        public void SimVelocity_IsPerInstanceAndProduced_YetExcluded_BecauseNothingIngressesIt()
        {
            using var repo = RepoRegistering();

            var derived = new MandatoryComponentResolver()
                .Resolve(VehicleShapedTemplate(), repo, NedLikeTranslators(), NedLikeIngressMap());

            Assert.DoesNotContain(ComponentType<SimVelocity>.ID, derived);

            // …and the premise of the exclusion is real, not incidental to the fixture.
            Assert.Contains(ComponentType<SimVelocity>.ID, ComponentAttributeSets.PerInstanceValue);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>THE DEADLOCK GUARD.</b> <c>GhostPromotionSystem</c> has no registration check of its own,
        /// so a requirement for a component this host never registers would abort promotion every frame,
        /// forever, in silence. ⇒ the resolver must drop it.
        /// </summary>
        [Fact]
        public void AComponentThisHostDoesNotRegister_IsNeverRequired()
        {
            using var repo = RepoRegistering(entityInfo: false);

            var derived = new MandatoryComponentResolver()
                .Resolve(VehicleShapedTemplate(), repo, NedLikeTranslators(), NedLikeIngressMap());

            Assert.DoesNotContain(ComponentType<EntityInfo>.ID, derived);
            Assert.Contains(ComponentType<SimTransform>.ID, derived);
        }

        /// <summary>
        /// ⭐⭐ <b>THE TEMPLATE FILTER.</b> 📐 §6.6a measured <c>TacGraphic_Area</c> / <c>_Route</c>: they carry
        /// NO descriptors at all, so no translator runs and the correct answer is ∅ — which is also what
        /// those templates declare by hand today.
        /// </summary>
        [Fact]
        public void ATemplateWithNoDescriptors_DerivesNothing()
        {
            using var repo = RepoRegistering();

            var derived = new MandatoryComponentResolver()
                .Resolve(new TkbTemplate("TacGraphicShaped", 7002), repo,
                         NedLikeTranslators(), NedLikeIngressMap());

            Assert.Empty(derived);
        }

        /// <summary>
        /// ⭐⭐ <b>…and the filter is keyed on DESCRIPTORS, not on the host's translator list.</b> Drop the
        /// behaviour translator and <c>EntityInfo</c> goes with it, while <c>SimTransform</c> stays — so a
        /// host that projects less requires less, which is §6.5b's <i>"the registration set is the narrowing
        /// lever"</i> reaching the promotion gate too.
        /// </summary>
        [Fact]
        public void ATranslatorThisHostDoesNotCompose_ContributesNoRequirement()
        {
            using var repo = RepoRegistering();

            var derived = new MandatoryComponentResolver().Resolve(
                VehicleShapedTemplate(), repo,
                new ITkbEntityTranslator[] { new SpatialLike() }, NedLikeIngressMap());

            Assert.Equal(new[] { ComponentType<SimTransform>.ID }, SortedCopy(derived));
        }

        // ── ③ THE TWO TRANSLATOR SHAPES THE INTERFACE CALLS OUT ──────────────────────────────────────

        /// <summary>
        /// ⭐⭐ <b>The OBSERVER shape runs for EVERY template.</b> A translator declaring no consumed
        /// descriptors is not "matches nothing" — <c>AiDiagnosticsTkbTranslator</c>'s <c>Inject</c> is gated on
        /// world state and runs unconditionally. ⛔ Treating an empty consumed set as a non-match would
        /// silently drop its contribution.
        /// </summary>
        [Fact]
        public void AnObserverTranslator_ContributesEvenToADescriptorlessTemplate()
        {
            using var repo = RepoRegistering();

            var derived = new MandatoryComponentResolver().Resolve(
                new TkbTemplate("NoDescriptors", 7003), repo,
                new ITkbEntityTranslator[] { new ObserverLike() }, NedLikeIngressMap());

            Assert.Equal(new[] { ComponentType<EntityInfo>.ID }, SortedCopy(derived));
        }

        /// <summary>
        /// ⛔⛔ <b>The STRIP shape must contribute NOTHING</b> — it only removes components. ⚠ If a future
        /// edit "helpfully" made <c>GetProducedComponents</c> report what a strip translator touches, this
        /// rail reddens: requiring a component that is deliberately deleted is the purest form of the
        /// never-promotes deadlock.
        /// </summary>
        [Fact]
        public void AStripTranslator_AddsNoRequirement()
        {
            using var repo = RepoRegistering();

            var derived = new MandatoryComponentResolver().Resolve(
                VehicleShapedTemplate(), repo,
                new ITkbEntityTranslator[] { new StripLike() }, NedLikeIngressMap());

            Assert.Empty(derived);
        }

        // ── ④ THE ORDERING HAZARD ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 🔴🔴🔴 <b>THE RACE, AND THE REASON THE CACHE IS CONDITIONAL.</b> The world's
        /// <c>DescriptorOwnershipMap</c> is filled on the network module's FIRST TICK
        /// (<c>NedReplicationModule.ContributeDescriptorPairings</c>), and <c>GhostPromotionSystem</c> may run
        /// before it. An empty ingress set derives ∅ — <b>no gate at all</b> — so caching that answer would
        /// permanently disable the mechanism on a node that merely scheduled unluckily.
        ///
        /// <para>⭐ The resolver therefore keys its cache on the ingress map's SIZE as well as the template,
        /// so the answer self-corrects on the tick the pairings arrive. ⛔ Nothing is lost on a genuinely
        /// networkless host: no ingress means no ghosts, so the gate is never consulted there.</para>
        ///
        /// <para>⚠ <b>An earlier cut ALSO refused to memoise a result derived from an empty map.</b> 📐 Its
        /// inverse-edit red-proof stayed GREEN — the generation check already subsumed it — so the guard was
        /// removed rather than kept as belt-and-braces. ⭐ This rail reddens on the mechanism that actually
        /// does the work.</para>
        /// </summary>
        [Fact]
        public void AnEmptyIngressMap_DerivesNothing_AndDoesNotPoisonTheCache()
        {
            using var repo = RepoRegistering();
            var resolver = new MandatoryComponentResolver();
            var template = VehicleShapedTemplate();

            var early = resolver.Resolve(template, repo, NedLikeTranslators(), new DescriptorOwnershipMap());
            Assert.Empty(early);

            // The very same resolver, same template, once the module has contributed its pairings.
            var later = resolver.Resolve(template, repo, NedLikeTranslators(), NedLikeIngressMap());

            Assert.Equal(
                new[] { ComponentType<SimTransform>.ID, ComponentType<EntityInfo>.ID },
                SortedCopy(later));
        }

        /// <summary>
        /// ⭐ <b>A GROWING ingress map re-derives too.</b> The pairings arrive from several contributors
        /// (<c>NedReplicationModule</c>, each egress system), in any order and any number of times. ⚠ A cache
        /// keyed on the template alone would freeze whichever partial set happened to be first.
        /// </summary>
        [Fact]
        public void AGrowingIngressMap_InvalidatesTheCache()
        {
            using var repo = RepoRegistering();
            var resolver = new MandatoryComponentResolver();
            var template = VehicleShapedTemplate();

            var partial = new DescriptorOwnershipMap();
            partial.RegisterFromTranslator(WorldPosOrdinal, new[] { ComponentType<SimTransform>.ID });

            Assert.Equal(new[] { ComponentType<SimTransform>.ID },
                         SortedCopy(resolver.Resolve(template, repo, NedLikeTranslators(), partial)));

            partial.RegisterFromTranslator(EntityInfoOrdinal, new[] { ComponentType<EntityInfo>.ID });

            Assert.Equal(
                new[] { ComponentType<SimTransform>.ID, ComponentType<EntityInfo>.ID },
                SortedCopy(resolver.Resolve(template, repo, NedLikeTranslators(), partial)));
        }

        /// <summary>⭐ Stable inputs ⇒ the same cached instance, so the derivation stays off the per-ghost
        /// hot path. ⛔ Re-deriving per promotion would put a reflection-backed set scan and two hash builds
        /// inside a 2 ms frame budget shared by every arriving ghost.</summary>
        [Fact]
        public void AStableHost_ResolvesFromCache()
        {
            using var repo = RepoRegistering();
            var resolver = new MandatoryComponentResolver();
            var template = VehicleShapedTemplate();
            var map = NedLikeIngressMap();

            var first  = resolver.Resolve(template, repo, NedLikeTranslators(), map);
            var second = resolver.Resolve(template, repo, NedLikeTranslators(), map);

            Assert.Same(first, second);
        }

        private static int[] SortedCopy(IReadOnlyList<int> ids)
        {
            var copy = new int[ids.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = ids[i];
            Array.Sort(copy);
            return copy;
        }
    }
}
