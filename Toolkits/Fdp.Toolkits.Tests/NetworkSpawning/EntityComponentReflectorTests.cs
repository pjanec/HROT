using System;
using System.Collections.Generic;
using Xunit;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning;

namespace FDP.Toolkit.NetworkSpawning.Tests
{
    // Simple test components
    [ComponentId(239)]
    public struct TestComponentA
    {
        public int Value;
    }

    [ComponentId(240)]
    public class TestComponentB
    {
        public string Name;
    }

    public class EntityComponentReflectorTests
    {
        [Fact]
        public void SetComponent_NewComponent_AddsSuccessfully()
        {
            // Arrange
            using var world = new EntityRepository();
            world.RegisterComponent<TestComponentA>();
            var entity = world.CreateEntity();
            var comp = new TestComponentA { Value = 42 };

            // Act
            EntityComponentReflector.SetComponent(world, entity, comp);

            // Assert
            Assert.True(world.HasComponent<TestComponentA>(entity));
            Assert.Equal(42, world.GetComponent<TestComponentA>(entity).Value);
        }

        [Fact]
        public void SetComponent_ExistingComponent_Overwrites()
        {
            // Arrange
            using var world = new EntityRepository();
            world.RegisterComponent<TestComponentA>();
            var entity = world.CreateEntity();
            world.AddComponent(entity, new TestComponentA { Value = 1 });

            // Act
            EntityComponentReflector.SetComponent(world, entity, new TestComponentA { Value = 99 });

            // Assert
            Assert.Equal(99, world.GetComponent<TestComponentA>(entity).Value);
        }

        [Fact]
        public void SetComponent_NullComponent_DoesNotThrow()
        {
            // Arrange
            using var world = new EntityRepository();
            // No registration needed if component is null
            var entity = world.CreateEntity();

            // Act + Assert (no exception)
            EntityComponentReflector.SetComponent(world, entity, null);
        }

        [Fact]
        public void SetComponent_MultipleTypes_AllApplied()
        {
            // Arrange
            using var world = new EntityRepository();
            world.RegisterComponent<TestComponentA>();
            world.RegisterComponent<TestComponentB>();
            var entity = world.CreateEntity();
            var items = new List<object>
            {
                new TestComponentA { Value = 10 },
                new TestComponentB { Name  = "alpha" }
            };

            // Act
            foreach (var c in items)
                EntityComponentReflector.SetComponent(world, entity, c);

            // Assert
            Assert.Equal(10,      world.GetComponent<TestComponentA>(entity).Value);
            Assert.Equal("alpha", world.GetComponent<TestComponentB>(entity).Name);
        }
    }
}
