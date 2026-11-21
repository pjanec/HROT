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
    public class ProviderAssignmentTests
    {
        [TickMode(TickPolicy.EveryFrame)]
        private class TestModule : IEcsModule
        {
            public string Name => "TestModule-" + Guid.NewGuid();
            public ExecutionPolicy Policy { get; set; } = ExecutionPolicy.Synchronous();
            public void Tick(ISimulationView view, float deltaTime) { }
        }

        private ModuleHostKernel CreateKernel()
        {
            var liveWorld = new EntityRepository();
            var eventAccum = new EventAccumulator();
            return new ModuleHostKernel(liveWorld, eventAccum);
        }

        private ModuleHostKernel.ModuleEntry GetModuleEntry(ModuleHostKernel kernel, IEcsModule module)
        {
            var field = typeof(ModuleHostKernel).GetField("_modules", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = (List<ModuleHostKernel.ModuleEntry>)field.GetValue(kernel);
            return list.First(e => e.Module == module);
        }

        [Fact]
        public void ProviderAssignment_SynchronousDirect_NoProvider()
        {
            using var kernel = CreateKernel();
            var module = new TestModule
            {
                Policy = ExecutionPolicy.Synchronous()
            };
            
            kernel.RegisterModule(module);
            kernel.InitializeForTest();
            
            var entry = GetModuleEntry(kernel, module);
            // Direct access means Provider can be null or special.
            // In my implementation I explicitly set it to null in AutoAssignProviders for Direct strategy.
            Assert.Null(entry.Provider); 
        }

        [Fact]
        public void ProviderAssignment_FrameSyncedGDB_SharedReplica()
        {
            using var kernel = CreateKernel();
            var module1 = new TestModule { Policy = ExecutionPolicy.FastReplica() };
            var module2 = new TestModule { Policy = ExecutionPolicy.FastReplica() };
            
            kernel.RegisterModule(module1);
            kernel.RegisterModule(module2);
            kernel.InitializeForTest();
            
            var provider1 = GetModuleEntry(kernel, module1).Provider;
            var provider2 = GetModuleEntry(kernel, module2).Provider;
            
            Assert.NotNull(provider1);
            Assert.Same(provider1, provider2); // Share GDB replica
            Assert.IsType<DoubleBufferProvider>(provider1);
        }

        [Fact]
        public void ProviderAssignment_AsyncSoD_SingleModule_OnDemand()
        {
            using var kernel = CreateKernel();
            var module = new TestModule { Policy = ExecutionPolicy.SlowBackground(10) };
            
            kernel.RegisterModule(module);
            kernel.InitializeForTest();
            
            var provider = GetModuleEntry(kernel, module).Provider;
            Assert.IsType<OnDemandProvider>(provider);
        }

        [Fact]
        public void ProviderAssignment_FrameSyncedSoD_MultipleModules_Convoy()
        {
            // ⭐ TM-023 — the convoy is for modules the main thread waits for; ProviderAssignment_AsyncSoD_MultipleModules_EachOwnSnapshot is the async half.
            using var kernel = CreateKernel();
            var module1 = new TestModule { Policy = ExecutionPolicy.SlowBackground(10).WithMode(RunMode.FrameSynced) };
            var module2 = new TestModule { Policy = ExecutionPolicy.SlowBackground(10).WithMode(RunMode.FrameSynced) };
            var module3 = new TestModule { Policy = ExecutionPolicy.SlowBackground(10).WithMode(RunMode.FrameSynced) };
            
            kernel.RegisterModule(module1);
            kernel.RegisterModule(module2);
            kernel.RegisterModule(module3);
            kernel.InitializeForTest();
            
            var provider = GetModuleEntry(kernel, module1).Provider;
            
            Assert.IsType<SharedSnapshotProvider>(provider);
            Assert.Same(provider, GetModuleEntry(kernel, module2).Provider);
            Assert.Same(provider, GetModuleEntry(kernel, module3).Provider);
        }

        [Fact]
        public void ProviderAssignment_AsyncSoD_MultipleModules_EachOwnSnapshot()
        {
            // ⭐ TM-023 — Asynchronous SoD modules never convoy: an early finisher would re-acquire a snapshot a slower member still
            //   holds (stale, its events read twice). Each keeps an OnDemandProvider — a fresh snapshot at dispatch.
            using var kernel = CreateKernel();
            var module1 = new TestModule { Policy = ExecutionPolicy.SlowBackground(10) };
            var module2 = new TestModule { Policy = ExecutionPolicy.SlowBackground(10) };

            kernel.RegisterModule(module1);
            kernel.RegisterModule(module2);
            kernel.InitializeForTest();

            var p1 = GetModuleEntry(kernel, module1).Provider;
            var p2 = GetModuleEntry(kernel, module2).Provider;
            Assert.IsType<OnDemandProvider>(p1);
            Assert.IsType<OnDemandProvider>(p2);
            Assert.NotSame(p1, p2);
        }
        
        [Fact]
        public void ProviderAssignment_AsyncSoD_DifferentFrequencies_SeparateConvoys()
        {
            using var kernel = CreateKernel();
            var module10Hz = new TestModule { Policy = ExecutionPolicy.SlowBackground(10) };
            var module5Hz = new TestModule { Policy = ExecutionPolicy.SlowBackground(5) };
            
            kernel.RegisterModule(module10Hz);
            kernel.RegisterModule(module5Hz);
            kernel.InitializeForTest();
            
            var provider1 = GetModuleEntry(kernel, module10Hz).Provider;
            var provider2 = GetModuleEntry(kernel, module5Hz).Provider;
            
            Assert.NotNull(provider1);
            Assert.NotNull(provider2);
            Assert.NotSame(provider1, provider2); // Different frequencies -> Different providers
        }

        [Fact]
        public void ProviderAssignment_InvalidPolicy_ThrowsClearError()
        {
            using var kernel = CreateKernel();
            var module = new TestModule
            {
                // Invalid: Async mode but Direct strategy
                Policy = new ExecutionPolicy
                {
                    Mode = RunMode.Asynchronous,
                    Strategy = DataStrategy.Direct,
                    TargetFrequencyHz = 60,
                    MaxExpectedRuntimeMs = 100
                }
            };
            
            kernel.RegisterModule(module);
            
            var ex = Assert.Throws<InvalidOperationException>(() => kernel.InitializeForTest());
            Assert.Contains("invalid execution policy", ex.Message);
        }
    }
}

