using System;
using System.Collections.Generic;
using Xunit;
using FDP.Toolkit.Lifecycle.Systems;
using FDP.Toolkit.Lifecycle.Events;
using Fdp.Kernel;
using Fdp.Interfaces;
using ModuleHost.Core.Abstractions;
using Moq;

namespace FDP.Toolkit.Lifecycle.Tests.Systems
{
    [ComponentId(244)]
    public struct TestComponentA
    {
        public int Value;
    }

    public class BlueprintApplicationSystemTests
    {
        [Fact]
        public void Execute_PreservesExistingComponents()
        {
            // Register template with Component A (Value=10).
            var template = new TkbTemplate("TestTemplate", 1);
            template.AddComponent(new TestComponentA { Value = 10 });
            
            var mockTkb = new Mock<ITkbDatabase>();
            TkbTemplate outTemplate = template;
            mockTkb.Setup(x => x.TryGetByType(1, out outTemplate)).Returns(true);

            // Create entity with Component A (Value=20).
            var repo = new EntityRepository();
            repo.RegisterComponent<TestComponentA>();
            
            var system = new BlueprintApplicationSystem(mockTkb.Object);
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new TestComponentA { Value = 20 });
            
            // Add ConstructionOrder event
            repo.Bus.Publish(new ConstructionOrder 
            { 
                Entity = entity, 
                BlueprintId = 1 
            });
            
            // Swap buffers to make event visible for consumption
            repo.Bus.SwapBuffers();

            // Run system.
            system.Execute(repo, 0.1f);

            // Assert Value is 20 (Preserved).
            var comp = repo.GetComponent<TestComponentA>(entity);
            Assert.Equal(20, comp.Value);
        }

        [Fact]
        public void Execute_AppliesMissingComponents()
        {
            // Same template.
            var template = new TkbTemplate("TestTemplate", 1);
            template.AddComponent(new TestComponentA { Value = 10 });
            
            var mockTkb = new Mock<ITkbDatabase>();
            TkbTemplate outTemplate = template;
            mockTkb.Setup(x => x.TryGetByType(1, out outTemplate)).Returns(true);

            // Empty entity.
            var repo = new EntityRepository();
            repo.RegisterComponent<TestComponentA>();
            var system = new BlueprintApplicationSystem(mockTkb.Object);
            var entity = repo.CreateEntity();

            // Add ConstructionOrder event
            repo.Bus.Publish(new ConstructionOrder 
            { 
                Entity = entity, 
                BlueprintId = 1 
            });
            
            // Swap buffers
            repo.Bus.SwapBuffers();

            // Run system.
            system.Execute(repo, 0.1f);

            // Assert Value is 10 (Applied from template).
            var comp = repo.GetComponent<TestComponentA>(entity);
            Assert.Equal(10, comp.Value);
        }
    }
}
