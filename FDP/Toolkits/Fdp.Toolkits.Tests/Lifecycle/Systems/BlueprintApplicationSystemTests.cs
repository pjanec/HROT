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

