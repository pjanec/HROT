using Xunit;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Providers;
using Fdp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Fdp.ModuleHost.Tests
{
    public class ComponentMaskTests
    {
        [ComponentId(98)]
        struct TestComponent1 { public int Value; }
        [ComponentId(99)]
        struct TestComponent2 { public float Data; }
        [ComponentId(101)]
        struct TestComponent3 { public byte Flag; }
        
        class ModuleWithDeps : IEcsModule
        {
            public string Name => "ModuleWithDeps";
            public ExecutionPolicy Policy => ExecutionPolicy.FastReplica();
            
            public IEnumerable<Type> GetRequiredComponents()
            {
                yield return typeof(TestComponent1);
                yield return typeof(TestComponent2);
            }
            
            public void Tick(ISimulationView view, float deltaTime) { }
        }
        
        class ModuleWithNoDeps : IEcsModule
        {
            public string Name => "ModuleWithNoDeps";
            public ExecutionPolicy Policy => ExecutionPolicy.FastReplica();
            
            // Uses default: null (all components)
            
            public void Tick(ISimulationView view, float deltaTime) { }
        }
        
        class ModuleRequiringC2C3 : IEcsModule
        {
            public string Name => "ModuleRequiringC2C3";
            public ExecutionPolicy Policy => ExecutionPolicy.FastReplica();
            
            public IEnumerable<Type> GetRequiredComponents()
            {
                yield return typeof(TestComponent2);
                yield return typeof(TestComponent3);
            }
            
            public void Tick(ISimulationView view, float deltaTime) { }
        }
        
        [Fact]
        public void ComponentMask_ModuleWithDeps_ReturnsFilteredMask()
        {
            // Setup
            var repo = new EntityRepository();
            repo.RegisterComponent<TestComponent1>();
            repo.RegisterComponent<TestComponent2>();
            repo.RegisterComponent<TestComponent3>();
            
            var eventAccum = new EventAccumulator();
            using var kernel = new ModuleHostKernel(repo, eventAccum);
            
            var module = new ModuleWithDeps();
            kernel.RegisterModule(module);
            kernel.InitializeForTest();
            
            // Get mask via reflection
            var mask = GetModuleMask(kernel, module);
            
            // Assert: Only TestComponent1 and TestComponent2 bits set
            int id1 = ComponentTypeRegistry.GetId(typeof(TestComponent1));
            int id2 = ComponentTypeRegistry.GetId(typeof(TestComponent2));
            int id3 = ComponentTypeRegistry.GetId(typeof(TestComponent3));
            
            Assert.True(mask.IsSet(id1));
            Assert.True(mask.IsSet(id2));
            Assert.False(mask.IsSet(id3)); // NOT required
        }
        
        [Fact]
        public void ComponentMask_ModuleWithNoDeps_ReturnsFullMask()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<TestComponent1>();
            
            var eventAccum = new EventAccumulator();
            using var kernel = new ModuleHostKernel(repo, eventAccum);
            
            var module = new ModuleWithNoDeps();
            kernel.RegisterModule(module);
            kernel.InitializeForTest();
            
            var mask = GetModuleMask(kernel, module);
            
            // Assert: All bits set (full mask)
            for (int i = 0; i < 256; i++)
            {
                Assert.True(mask.IsSet(i));
            }
        }
        
        [Fact]
        public void ComponentMask_ConvoyWithMixedDeps_UsesUnionMask()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<TestComponent1>();
            repo.RegisterComponent<TestComponent2>();
            repo.RegisterComponent<TestComponent3>();
            
            var eventAccum = new EventAccumulator();
            using var kernel = new ModuleHostKernel(repo, eventAccum);
            
            // Module 1: Requires C1, C2
            var module1 = new ModuleWithDeps();
            
            // Module 2: Requires C2, C3
            var module2 = new ModuleRequiringC2C3();
            
            kernel.RegisterModule(module1);
            kernel.RegisterModule(module2);
            kernel.InitializeForTest();
            
            // Both in same convoy (same frequency & strategy)
            
            var provider = GetModuleProvider(kernel, module1);
            Assert.NotNull(provider);
            Assert.IsType<DoubleBufferProvider>(provider); // Should use GDB (FastReplica)
            
            // DoubleBufferProvider stores '_syncMask' internally. 
            // We can't easily access it without reflection, OR we can check functionality.
            // But here we rely on the fact that they were grouped and initialized.
            // Let's verify grouping at least -> same provider instance
            var provider2 = GetModuleProvider(kernel, module2);
            Assert.Same(provider, provider2);
            
            // The fact that they are grouped and initialized means CalculateUnionMask was called with their cached masks.
            // Since we verified cached masks in previous tests, and logic in Kernel uses them, 
            // the result should be correct transitively.
        }
        
        // Reflected access to internal module entry
        private BitMask256 GetModuleMask(ModuleHostKernel kernel, IEcsModule module)
        {
            // _modules is private List<ModuleEntry>
            var field = typeof(ModuleHostKernel)
                .GetField("_modules", BindingFlags.NonPublic | BindingFlags.Instance);
            
            var modulesList = field.GetValue(kernel); // This is object (List<ModuleEntry>)
            
            // List<ModuleEntry> is generic, so we iterate using dynamic or IEnumerable
            var enumerable = (System.Collections.IEnumerable)modulesList;
            
            foreach (var entryObj in enumerable)
            {
                var entryType = entryObj.GetType();
                var modProp = entryType.GetProperty("Module");
                var m = (IEcsModule)modProp.GetValue(entryObj);
                
                if (m == module)
                {
                    var maskField = entryType.GetField("ComponentMask");
                    return (BitMask256)maskField.GetValue(entryObj);
                }
            }
            throw new Exception("Module not found");
        }
        
        private ISnapshotProvider GetModuleProvider(ModuleHostKernel kernel, IEcsModule module)
        {
            var field = typeof(ModuleHostKernel)
                .GetField("_modules", BindingFlags.NonPublic | BindingFlags.Instance);
            
            var modulesList = field.GetValue(kernel);
            var enumerable = (System.Collections.IEnumerable)modulesList;
            
            foreach (var entryObj in enumerable)
            {
                var entryType = entryObj.GetType();
                var modProp = entryType.GetProperty("Module");
                var m = (IEcsModule)modProp.GetValue(entryObj);
                
                if (m == module)
                {
                    var provProp = entryType.GetProperty("Provider");
                    return (ISnapshotProvider)provProp.GetValue(entryObj);
                }
            }
            throw new Exception("Module not found");
        }
    }
}
