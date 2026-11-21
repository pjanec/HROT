using System;
using System.Collections.Generic;
using Xunit;
using Fdp.Toolkit.Lifecycle.Systems;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Tkb.Domain;
using Moq;

namespace Fdp.Toolkit.Lifecycle.Tests.Systems
{
    public class BlueprintApplicationSystemTests
    {
        [Fact]
        public void Execute_UnknownTkbType_DoesNotThrow()
        {
            var mockTkb = new Mock<ITkbDatabase>();
            TkbTemplate? outTemplate = null;
            mockTkb.Setup(x => x.TryGetByType(It.IsAny<long>(), out outTemplate)).Returns(false);

            var repo = new EntityRepository();
            var system = new BlueprintApplicationSystem(mockTkb.Object);

            repo.Bus.Publish(new ConstructionOrder { Entity = default, BlueprintId = 99 });
            repo.Bus.SwapBuffers();

            // Must not throw even when template is not found.
            system.Execute(repo, 0.1f);
        }

        /// <summary>
        /// ⭐ CE-2419 — an order whose entity was destroyed before the system consumed it is SKIPPED (nothing to construct). 📐 Before, a translator's
        /// injection threw "Entity … is not alive" and the unhandled exception killed the node (SimHost + Traffic, after the brain was ejected mid-load).
        /// 🔴 Red-proof: the alive guard removed ⇒ InvalidOperationException.
        /// </summary>
        [Fact]
        public void CE2419_AnOrderForAnEntityDestroyedMeanwhile_IsSkipped()
        {
            var template = new TkbTemplate("TestTemplate", 1);
            var mockTkb = new Mock<ITkbDatabase>();
            TkbTemplate outTemplate = template;
            mockTkb.Setup(x => x.TryGetByType(1, out outTemplate)).Returns(true);
            var injector = new Mock<ITkbEntityTranslator>();
            injector.Setup(x => x.Inject(It.IsAny<EntityRepository>(), It.IsAny<Entity>(), It.IsAny<TkbTemplate>()))
                    .Callback<EntityRepository, Entity, TkbTemplate>((r, e, _) => { if (!r.IsAlive(e)) throw new InvalidOperationException($"Entity {e} is not alive"); });

            var repo = new EntityRepository();
            repo.RegisterEvent<ConstructionOrder>();
            var system = new BlueprintApplicationSystem(mockTkb.Object, new[] { injector.Object });
            var gone = repo.CreateEntity();
            var alive = repo.CreateEntity();
            repo.Bus.Publish(new ConstructionOrder { Entity = gone, BlueprintId = 1 });
            repo.Bus.Publish(new ConstructionOrder { Entity = alive, BlueprintId = 1 });
            repo.Bus.SwapBuffers();
            repo.DestroyEntity(gone);                  // torn down before the order is consumed

            system.Execute(repo, 0.1f);                // ⛔ must not throw

            injector.Verify(x => x.Inject(repo, alive, template), Times.Once);   // the live one is still constructed
            injector.Verify(x => x.Inject(repo, gone, It.IsAny<TkbTemplate>()), Times.Never);
        }

        [Fact]
        public void Execute_KnownTkbType_ConsumesOrderWithoutThrowing()
        {
            var template = new TkbTemplate("TestTemplate", 1);
            template.AddDescriptor(new TkbMasterDto { CustomName = "TestTemplate" });

            var mockTkb = new Mock<ITkbDatabase>();
            TkbTemplate outTemplate = template;
            mockTkb.Setup(x => x.TryGetByType(1, out outTemplate)).Returns(true);

            var repo = new EntityRepository();
            repo.RegisterEvent<ConstructionOrder>();
            var system = new BlueprintApplicationSystem(mockTkb.Object);
            var entity = repo.CreateEntity();

            repo.Bus.Publish(new ConstructionOrder { Entity = entity, BlueprintId = 1 });
            repo.Bus.SwapBuffers();

            // No exception — template found, no translators yet (Phase 6).
            system.Execute(repo, 0.1f);
        }
    }
}

