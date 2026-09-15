using System;
using System.Collections.Generic;
using Xunit;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle.Events;

namespace Fdp.Toolkit.Replication.Tests
{
    // Mocks
    class MockTkbDatabase : ITkbDatabase
    {
        public TkbTemplate TemplateToReturn;
        
        public IEnumerable<TkbTemplate> GetAll() => throw new NotImplementedException();
        public TkbTemplate GetByName(string name) => throw new NotImplementedException();
        public TkbTemplate GetByType(long tkbType) => TemplateToReturn;
        public TkbTemplate GetTemplateByEntityType(Fdp.Core.DISEntityType entityType) => null;
        public TkbTemplate GetTemplateByName(string templateName) => null;
        public void Register(TkbTemplate template) {}
        public bool TryGetByName(string name, out TkbTemplate template) => throw new NotImplementedException();
        public bool TryGetByType(long tkbType, out TkbTemplate template)
        {
            template = TemplateToReturn;
            return template != null;
        }
        public string? ActiveTkbName { get; set; }
        public void Clear() { }
        public IEnumerable<TkbTemplate> GetEntitiesByCategory(string categoryPath)
            => throw new NotImplementedException();
    }

    class SlowMockTkbDatabase : ITkbDatabase
    {
        public TkbTemplate TemplateToReturn;
        public int CallCount = 0;

        public IEnumerable<TkbTemplate> GetAll() => throw new NotImplementedException();
        public TkbTemplate GetByName(string name) => throw new NotImplementedException();
        public TkbTemplate GetByType(long tkbType)
        {
            CallCount++;
            System.Threading.Thread.Sleep(5); // 5ms per entity, exceeds 2ms budget
            return TemplateToReturn;
        }
        public TkbTemplate GetTemplateByEntityType(Fdp.Core.DISEntityType entityType) => null;
        public TkbTemplate GetTemplateByName(string templateName) => null;
        public void Register(TkbTemplate template) {}
        public bool TryGetByName(string name, out TkbTemplate template) => throw new NotImplementedException();
        public bool TryGetByType(long tkbType, out TkbTemplate template)
        {
            template = GetByType(tkbType);
            return template != null;
        }
        public string? ActiveTkbName { get; set; }
        public void Clear() { }
        public IEnumerable<TkbTemplate> GetEntitiesByCategory(string categoryPath)
            => throw new NotImplementedException();
    }

    public class GhostProtocolTests
    {
        [Fact]
        public void PromotionSystem_Promotes_WhenRequirementsMet()
        {
            using var repo = new EntityRepository();

            var template = new TkbTemplate("Test", 123);
            var mockTkb = new MockTkbDatabase { TemplateToReturn = template };
            var elm = new Fdp.Toolkit.Lifecycle.EntityLifecycleModule(mockTkb, Array.Empty<int>());
            var sys = new GhostPromotionSystem(mockTkb, elm);

            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterEvent<ConstructionOrder>();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new TkbIdentity { TkbType = 123 });
            repo.AddComponent(entity, new GhostStateTracker { FirstSeenFrame = 0 });
            repo.SetLifecycleState(entity, EntityLifecycle.Ghost);

            sys.Execute(repo, 0f);

            Assert.Equal(EntityLifecycle.Constructing, repo.GetLifecycleState(entity));
            Assert.True(repo.HasComponent<TkbIdentity>(entity),
                "TkbIdentity must remain permanently attached after promotion.");
            Assert.False(repo.HasComponent<GhostStateTracker>(entity),
                "GhostStateTracker must be removed on promotion.");
        }

        [Fact]
        public void Execute_RespectsTimeBudget()
        {
            using var repo = new EntityRepository();

            var template = new TkbTemplate("Test", 123);
            var slowTkb = new SlowMockTkbDatabase { TemplateToReturn = template };
            var elm = new Fdp.Toolkit.Lifecycle.EntityLifecycleModule(slowTkb, Array.Empty<int>());
            var sys = new GhostPromotionSystem(slowTkb, elm);

            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterEvent<ConstructionOrder>();

            // Create 10 ghost entities; each GetByType call sleeps 5ms, well over the 2ms budget
            for (int i = 0; i < 10; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new TkbIdentity { TkbType = 123 });
                repo.AddComponent(e, new GhostStateTracker { FirstSeenFrame = 0 });
                repo.SetLifecycleState(e, EntityLifecycle.Ghost);
            }

            sys.Execute(repo, 0f);

            Assert.True(slowTkb.CallCount < 10,
                $"Processed too many ghosts: {slowTkb.CallCount}. Should be limited by 2ms time budget.");
            Assert.True(slowTkb.CallCount > 0, "Should have processed at least one ghost.");
        }
        
        [Fact]
        public void Execute_DoesNotPromote_EntityNotInGhostLifecycle()
        {
            // Entity has TkbIdentity but is NOT in Ghost lifecycle;
            // the promotion query filters by Ghost lifecycle, so it must be skipped.
            using var repo = new EntityRepository();

            var template = new TkbTemplate("Test", 123);
            var mockTkb = new MockTkbDatabase { TemplateToReturn = template };
            var elm = new Fdp.Toolkit.Lifecycle.EntityLifecycleModule(mockTkb, Array.Empty<int>());
            var sys = new GhostPromotionSystem(mockTkb, elm);

            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterEvent<ConstructionOrder>();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new TkbIdentity { TkbType = 123 });
            // Deliberately do NOT set lifecycle to Ghost (stays at default Active)

            sys.Execute(repo, 0f);

            // Entity should remain unpromoted — TkbIdentity still present, not Constructing
            Assert.True(repo.HasComponent<TkbIdentity>(entity));
            Assert.NotEqual(EntityLifecycle.Constructing, repo.GetLifecycleState(entity));
        }

        // ═══ CE-265 — THE DERIVED GATE ══════════════════════════════════════════════════════════════
        //  📄 docs/designs/tkb-1/DESIGN.md §6.6a (design) · §6.6b (as-built).
        //  ⭐ These live HERE, in this system's own suite, rather than in a new class: the unit-level
        //    derivation has its own rails in MandatoryComponentResolverTests; what is asserted below is
        //    that THIS SYSTEM actually gates on them.

        private sealed class SpatialLikeTranslator : ITkbEntityTranslator
        {
            public IEnumerable<Type> GetConsumedDescriptors()
            { yield return typeof(Fdp.Toolkit.Tkb.Domain.TkbMasterDto); }
            public IEnumerable<Type> GetProducedComponents() { yield return typeof(SimTransform); }
            public void Inject(EntityRepository repo, Entity entity, TkbTemplate template) { }
        }

        /// <summary>The vehicle shape plus the one descriptor pairing that makes <c>SimTransform</c>
        /// ingressible, mirroring <c>GeoSpatialEgressTranslator.TargetComponentIds</c>.</summary>
        private static (EntityRepository repo, GhostPromotionSystem sys, Entity ghost) GatedGhost()
        {
            var template = new TkbTemplate("VehicleShaped", 4242);
            template.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.TkbMasterDto { CustomName = "VehicleShaped" });

            var mockTkb = new MockTkbDatabase { TemplateToReturn = template };
            var elm = new Fdp.Toolkit.Lifecycle.EntityLifecycleModule(mockTkb, Array.Empty<int>());
            var sys = new GhostPromotionSystem(
                mockTkb, elm, new ITkbEntityTranslator[] { new SpatialLikeTranslator() });

            var repo = new EntityRepository();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterEvent<ConstructionOrder>();

            Fdp.Toolkit.Replication.Attributes.AttributeInterpreterProvider
                .GetDescriptorMap(repo)
                .RegisterFromTranslator(10L, new[] { ComponentType<SimTransform>.ID });

            var ghost = repo.CreateEntity();
            repo.AddComponent(ghost, new TkbIdentity { TkbType = 4242 });
            repo.AddComponent(ghost, new GhostStateTracker { FirstSeenFrame = 0 });
            repo.SetLifecycleState(ghost, EntityLifecycle.Ghost);

            return (repo, sys, ghost);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The gate now exists WITHOUT anyone authoring it.</b> This template's
        /// <c>MandatoryComponents</c> list is EMPTY — exactly what <c>TkbDeserializer</c> produces for a
        /// file-loaded template — and the ghost is still held back until its position arrives.
        ///
        /// <para>🔴 Before <c>CE-265</c> this promoted immediately: the file path could author no
        /// requirements, so every file-loaded entity promoted at the origin and then jumped. ⛔ The failure
        /// was SILENT, which is why the assertion message names it.</para>
        /// </summary>
        [Fact]
        public void AGhostMissingADerivedComponent_IsHeldBack_EvenWithNoAuthoredRequirements()
        {
            var (repo, sys, ghost) = GatedGhost();
            using (repo)
            {
                sys.Execute(repo, 0f);

                Assert.Equal(EntityLifecycle.Ghost, repo.GetLifecycleState(ghost));
            }
        }

        /// <summary>
        /// ⭐⭐ <b>…and it is a GATE, not a block.</b> The same ghost promotes the moment the component the
        /// derivation asked for actually arrives. ⛔ Without this half the rail above would also pass on a
        /// resolver that simply never promotes anything.
        /// </summary>
        [Fact]
        public void TheSameGhost_PromotesOnceTheDerivedComponentArrives()
        {
            var (repo, sys, ghost) = GatedGhost();
            using (repo)
            {
                sys.Execute(repo, 0f);
                Assert.Equal(EntityLifecycle.Ghost, repo.GetLifecycleState(ghost));

                repo.AddComponent(ghost, new SimTransform());
                sys.Execute(repo, 0f);

                Assert.Equal(EntityLifecycle.Constructing, repo.GetLifecycleState(ghost));
            }
        }

        /// <summary>
        /// ⛔⛔ <b>A host that does not REGISTER the component must not be gated on it</b> —
        /// <c>GhostPromotionSystem</c> has no registration guard of its own, so a derived requirement it
        /// could never satisfy would abort promotion every frame, forever. ⚠ This is the same repository
        /// and template as the rail above, differing only in the registration.
        /// </summary>
        [Fact]
        public void AHostThatDoesNotRegisterTheComponent_PromotesImmediately()
        {
            var template = new TkbTemplate("VehicleShaped", 4243);
            template.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.TkbMasterDto { CustomName = "VehicleShaped" });

            var mockTkb = new MockTkbDatabase { TemplateToReturn = template };
            var elm = new Fdp.Toolkit.Lifecycle.EntityLifecycleModule(mockTkb, Array.Empty<int>());
            var sys = new GhostPromotionSystem(
                mockTkb, elm, new ITkbEntityTranslator[] { new SpatialLikeTranslator() });

            using var repo = new EntityRepository();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterEvent<ConstructionOrder>();
            // ⛔ SimTransform deliberately NOT registered.

            Fdp.Toolkit.Replication.Attributes.AttributeInterpreterProvider
                .GetDescriptorMap(repo)
                .RegisterFromTranslator(10L, new[] { ComponentType<SimTransform>.ID });

            var ghost = repo.CreateEntity();
            repo.AddComponent(ghost, new TkbIdentity { TkbType = 4243 });
            repo.AddComponent(ghost, new GhostStateTracker { FirstSeenFrame = 0 });
            repo.SetLifecycleState(ghost, EntityLifecycle.Ghost);

            sys.Execute(repo, 0f);

            Assert.Equal(EntityLifecycle.Constructing, repo.GetLifecycleState(ghost));
        }
    }
}
