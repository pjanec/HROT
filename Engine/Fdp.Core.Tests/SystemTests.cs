using System;
using System.Collections.Generic;
using Xunit;

namespace Fdp.Tests
{
    // Test systems for verification
    public class TestSystemA : Fdp.Kernel.ComponentSystem
    {
        public int UpdateCount { get; private set; }
        public int CreateCount { get; private set; }
        public int DestroyCount { get; private set; }
        
        protected override void OnCreate()
        {
            CreateCount++;
        }
        
        protected override void OnUpdate()
        {
            UpdateCount++;
        }
        
        protected override void OnDestroy()
        {
            DestroyCount++;
        }
    }
    
    public class TestSystemB : Fdp.Kernel.ComponentSystem
    {
        public int UpdateCount { get; private set; }
        
        protected override void OnUpdate()
        {
            UpdateCount++;
        }
    }
    
    [Fdp.Kernel.UpdateBefore(typeof(TestSystemB))]
    public class TestSystemC : Fdp.Kernel.ComponentSystem
    {
        public int UpdateCount { get; private set; }
        
        protected override void OnUpdate()
        {
            UpdateCount++;
        }
    }
    
    [Fdp.Kernel.UpdateAfter(typeof(TestSystemA))]
    public class TestSystemD : Fdp.Kernel.ComponentSystem
    {
        public int UpdateCount { get; private set; }
        
        protected override void OnUpdate()
        {
            UpdateCount++;
        }
    }
    
    // System with execution tracking
    public class TrackingSystem : Fdp.Kernel.ComponentSystem
    {
        public static List<string> ExecutionOrder = new List<string>();
        public string? Name { get; set; } = null!;
        
        protected override void OnUpdate()
        {
            ExecutionOrder.Add(Name!);
        }
    }
    
    [Fdp.Kernel.UpdateBefore(typeof(PhysicsTrackingSystem))]
    public class InputTrackingSystem : TrackingSystem
    {
        public InputTrackingSystem()
        {
            Name = "Input";
        }
    }
    
    public class PhysicsTrackingSystem : TrackingSystem
    {
        public PhysicsTrackingSystem()
        {
            Name = "Physics";
        }
    }
    
    [Fdp.Kernel.UpdateAfter(typeof(PhysicsTrackingSystem))]
    public class RenderTrackingSystem : TrackingSystem
    {
        public RenderTrackingSystem()
        {
            Name = "Render";
        }
    }
    
    // System that throws exception
    public class FaultySystem : Fdp.Kernel.ComponentSystem
    {
        protected override void OnUpdate()
        {
            throw new InvalidOperationException("Test exception");
        }
    }
    
    // Circular dependency tests
    [Fdp.Kernel.UpdateBefore(typeof(CircularSystemB))]
    public class CircularSystemA : Fdp.Kernel.ComponentSystem
    {
        protected override void OnUpdate() { }
    }
    
    [Fdp.Kernel.UpdateBefore(typeof(CircularSystemA))]
    public class CircularSystemB : Fdp.Kernel.ComponentSystem
    {
        protected override void OnUpdate() { }
    }
    
    public class SystemTests
    {
        [Fact]
        public void ComponentSystem_OnCreate_CalledOnce()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            // SystemGroup inherits from ComponentSystem, so use its InternalCreate
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            var system = new TestSystemA();
            group.AddSystem(system);
            
            Assert.Equal(1, system.CreateCount);
        }
        
        [Fact]
        public void ComponentSystem_OnUpdate_CalledEachFrame()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            var system = new TestSystemA();
            group.AddSystem(system);
            
            ((Fdp.Kernel.ComponentSystem)group).InternalUpdate();
            Assert.Equal(1, system.UpdateCount);
            
            ((Fdp.Kernel.ComponentSystem)group).InternalUpdate();
            Assert.Equal(2, system.UpdateCount);
            
            ((Fdp.Kernel.ComponentSystem)group).InternalUpdate();
            Assert.Equal(3, system.UpdateCount);
        }
        
        [Fact]
        public void ComponentSystem_OnDestroy_CalledOnDispose()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            var system = new TestSystemA();
            group.AddSystem(system);
            
            Assert.Equal(0, system.DestroyCount);
            
            system.Dispose();
            
            Assert.Equal(1, system.DestroyCount);
        }
        
        [Fact]
        public void ComponentSystem_Enabled_ControlsExecution()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            var system = new TestSystemA();
            group.AddSystem(system);
            
            system.Enabled = true;
            ((Fdp.Kernel.ComponentSystem)group).InternalUpdate();
            Assert.Equal(1, system.UpdateCount);
            
            system.Enabled = false;
            ((Fdp.Kernel.ComponentSystem)group).InternalUpdate();
            Assert.Equal(1, system.UpdateCount); // Should not increase
            
            system.Enabled = true;
            ((Fdp.Kernel.ComponentSystem)group).InternalUpdate();
            Assert.Equal(2, system.UpdateCount);
        }
        
        [Fact]
        public void ComponentSystem_World_SetAutomatically()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            var system = new TestSystemA();
            Assert.Null(system.World);
            
            group.AddSystem(system);
            Assert.NotNull(system.World);
            Assert.Same(repo, system.World);
        }
        
        [Fact]
        public void SystemGroup_AddSystem_InitializesSystem()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            var systemA = new TestSystemA();
            var systemB = new TestSystemB();
            
            group.AddSystem(systemA);
            group.AddSystem(systemB);
            
            Assert.Equal(2, group.SystemCount);
            Assert.Equal(1, systemA.CreateCount);
        }
        
        [Fact]
        public void SystemGroup_Update_ExecutesAllSystems()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            var systemA = new TestSystemA();
            var systemB = new TestSystemB();
            
            group.AddSystem(systemA);
            group.AddSystem(systemB);
            
            ((Fdp.Kernel.ComponentSystem)group).InternalUpdate();
            
            Assert.Equal(1, systemA.UpdateCount);
            Assert.Equal(1, systemB.UpdateCount);
        }
        
        [Fact]
        public void SystemGroup_UpdateBefore_SortsCorrectly()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            // Add in wrong order
            var systemB = new TestSystemB();
            var systemC = new TestSystemC(); // Has [UpdateBefore(TestSystemB)]
            
            group.AddSystem(systemB);
            group.AddSystem(systemC);
            
            var systems = group.GetSystems();
            
            // C should come before B
            Assert.Equal(2, systems.Count);
            Assert.IsType<TestSystemC>(systems[0]);
            Assert.IsType<TestSystemB>(systems[1]);
        }
        
        [Fact]
        public void SystemGroup_UpdateAfter_SortsCorrectly()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            // Add in wrong order
            var systemD = new TestSystemD(); // Has [UpdateAfter(TestSystemA)]
            var systemA = new TestSystemA();
            
            group.AddSystem(systemD);
            group.AddSystem(systemA);
            
            var systems = group.GetSystems();
            
            // A should come before D
            Assert.Equal(2, systems.Count);
            Assert.IsType<TestSystemA>(systems[0]);
            Assert.IsType<TestSystemD>(systems[1]);
        }
        
        [Fact]
        public void SystemGroup_ComplexDependencies_SortsCorrectly()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            TrackingSystem.ExecutionOrder.Clear();
            
            // Add in wrong order
            var render = new RenderTrackingSystem();  // UpdateAfter(Physics)
            var physics = new PhysicsTrackingSystem();
            var input = new InputTrackingSystem();    // UpdateBefore(Physics)
            
            group.AddSystem(render);
            group.AddSystem(physics);
            group.AddSystem(input);
            
            // Execute
            ((Fdp.Kernel.ComponentSystem)group).InternalUpdate();
            
            // Should be: Input -> Physics -> Render
            Assert.Equal(3, TrackingSystem.ExecutionOrder.Count);
            Assert.Equal("Input", TrackingSystem.ExecutionOrder[0]);
            Assert.Equal("Physics", TrackingSystem.ExecutionOrder[1]);
            Assert.Equal("Render", TrackingSystem.ExecutionOrder[2]);
        }
        
        [Fact]
        public void SystemGroup_FaultySystems_ContinuesExecution()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            var systemA = new TestSystemA();
            var faulty = new FaultySystem();
            var systemB = new TestSystemB();
            
            group.AddSystem(systemA);
            group.AddSystem(faulty);
            group.AddSystem(systemB);
            
            // Should not throw, should continue executing
            ((Fdp.Kernel.ComponentSystem)group).InternalUpdate();
            
            Assert.Equal(1, systemA.UpdateCount);
            Assert.Equal(1, systemB.UpdateCount);
        }
        
        [Fact]
        public void SystemGroup_CircularDependency_ThrowsException()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            var systemA = new CircularSystemA();
            var systemB = new CircularSystemB();
            
            group.AddSystem(systemA);
            group.AddSystem(systemB);
            
            // Should throw when trying to sort
            Assert.Throws<InvalidOperationException>(() => group.SortSystems());
        }
        
        [Fact]
        public void SystemGroup_Dispose_DisposesAllSystems()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            var systemA = new TestSystemA();
            var systemB = new TestSystemA();
            
            group.AddSystem(systemA);
            group.AddSystem(systemB);
            
            Assert.Equal(0, systemA.DestroyCount);
            Assert.Equal(0, systemB.DestroyCount);
            
            group.Dispose();
            
            Assert.Equal(1, systemA.DestroyCount);
            Assert.Equal(1, systemB.DestroyCount);
        }
        
        [Fact]
        public void SystemGroup_EmptyGroup_Works()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            // Should not throw
            ((Fdp.Kernel.ComponentSystem)group).InternalUpdate();
            
            Assert.Equal(0, group.SystemCount);
        }
        
        [Fact]
        public void SystemGroup_SingleSystem_NoSortNeeded()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            var system = new TestSystemA();
            group.AddSystem(system);
            
            // Should not throw, no dependencies to sort
            ((Fdp.Kernel.ComponentSystem)group).InternalUpdate();
            
            Assert.Equal(1, system.UpdateCount);
        }
        
        [Fact]
        public void SystemGroup_NestedGroups_Works()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var rootGroup = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)rootGroup).InternalCreate(repo);
            
            var childGroup = new Fdp.Kernel.SystemGroup();
            var system = new TestSystemA();
            
            rootGroup.AddSystem(childGroup);
            childGroup.AddSystem(system);
            
            ((Fdp.Kernel.ComponentSystem)rootGroup).InternalUpdate();
            
            Assert.Equal(1, system.UpdateCount);
        }
        
        // commented out, as the behavior was relaxed to allow usage mu modulehost modules that do not inherit from ComponentSystem
        //[Fact]
        //public void SystemAttributes_UpdateBefore_InvalidType_ThrowsException()
        //{
        //    Assert.Throws<ArgumentException>(() =>
        //    {
        //        var attr = new Fdp.Kernel.UpdateBeforeAttribute(typeof(string));
        //    });
        //}

		// commented out, as the behavior was relaxed to allow usage mu modulehost modules that do not inherit from ComponentSystem
		//[Fact]
        //public void SystemAttributes_UpdateAfter_InvalidType_ThrowsException()
        //{
        //    Assert.Throws<ArgumentException>(() =>
        //    {
        //        var attr = new Fdp.Kernel.UpdateAfterAttribute(typeof(int));
        //    });
        //}
        
		// commented out, as the behavior was relaxed to allow usage mu modulehost modules that do not inherit from ComponentSystem
        //[Fact]
        //public void SystemAttributes_UpdateInGroup_InvalidType_ThrowsException()
        //{
        //    Assert.Throws<ArgumentException>(() =>
        //    {
        //        var attr = new Fdp.Kernel.UpdateInGroupAttribute(typeof(object));
        //    });
        //}
        
        [Fact]
        public void ComponentSystem_CanAccessRepository()
        {
            using var repo = new Fdp.Kernel.EntityRepository();
            var group = new Fdp.Kernel.SystemGroup();
            ((Fdp.Kernel.ComponentSystem)group).InternalCreate(repo);
            
            var system = new TestSystemWithQuery();
            group.AddSystem(system);
            
            // System should be able to use World
            ((Fdp.Kernel.ComponentSystem)group).InternalUpdate();
            
            Assert.True(system.DidAccessWorld);
        }
        
        private class TestSystemWithQuery : Fdp.Kernel.ComponentSystem
        {
            public bool DidAccessWorld { get; private set; }
            
            protected override void OnCreate()
            {
                // Register component during system creation
                World.RegisterComponent<Fdp.Tests.Position>();
            }
            
            protected override void OnUpdate()
            {
                // Access the World to create entity
                var entity = World.CreateEntity();
                World.AddComponent(entity, new Fdp.Tests.Position { X = 1, Y = 2, Z = 3 });
                
                DidAccessWorld = World.IsAlive(entity);
            }
        }
    }
}
