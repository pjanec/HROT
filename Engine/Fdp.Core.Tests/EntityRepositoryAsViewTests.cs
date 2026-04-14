using Xunit;
using Fdp.Kernel;
using Fdp.Kernel.Internal; // For UnsafeShim in tests
using Fdp.ModuleHost_Core.Abstractions;
using System;

namespace Fdp.Tests
{
    public class EntityRepositoryAsViewTests
    {
        [ComponentId(190)]
        public struct TestPosition { public float X, Y; }
        [ComponentId(191)]
        public record TestManagedData { public string Name { get; set; } = string.Empty; }

        [Fact]
        public void EntityRepository_ImplementsISimulationView()
        {
            using var repo = new EntityRepository();
            Assert.True(repo is ISimulationView);
        }

        [Fact]
        public void Tick_ReturnsGlobalVersion()
        {
             using var repo = new EntityRepository();
             repo.SetGlobalVersion(42);
             var view = (ISimulationView)repo;
             Assert.Equal(42u, view.Tick);
        }
        
        [Fact]
        public void Time_ReturnsSimulationTime()
        {
             using var repo = new EntityRepository();
             repo.SetSimulationTime(123.45f);
             var view = (ISimulationView)repo;
             Assert.Equal(123.45f, view.Time);
        }

        [Fact]
        public void GetComponentRO_WorksThroughInterface()
        {
             using var repo = new EntityRepository();
             repo.RegisterUnmanagedComponent<TestPosition>();
             var e = repo.CreateEntity();
             
             var pos = new TestPosition { X = 10, Y = 20 };
             // Call internal method directly (accessible via InternalsVisibleTo)
             repo.AddUnmanagedComponent(e, pos);
             
             var view = (ISimulationView)repo;
             ref readonly var p = ref view.GetComponentRO<TestPosition>(e);
             
             Assert.Equal(10, p.X);
             Assert.Equal(20, p.Y);
        }
        
        [Fact]
        public void GetManagedComponentRO_WorksThroughInterface()
        {
             using var repo = new EntityRepository();
             repo.RegisterManagedComponent<TestManagedData>();
             var e = repo.CreateEntity();
             
             var data = new TestManagedData { Name = "Test" };
             repo.AddManagedComponent(e, data);
             
             var view = (ISimulationView)repo;
             var retrieved = view.GetManagedComponentRO<TestManagedData>(e);
             
             Assert.Same(data, retrieved);
             Assert.Equal("Test", retrieved.Name);
        }
        
        [Fact]
        public void IsAlive_WorksThroughInterface()
        {
             using var repo = new EntityRepository();
             var e = repo.CreateEntity();
             
             var view = (ISimulationView)repo;
             Assert.True(view.IsAlive(e));
             
             repo.DestroyEntity(e);
             Assert.False(view.IsAlive(e));
        }
    }
}
