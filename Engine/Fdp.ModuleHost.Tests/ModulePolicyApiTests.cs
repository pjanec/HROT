using Xunit;
using Fdp.ModuleHost.Abstractions;
using Fdp.Core;
using System;
using System.Collections.Generic;

namespace Fdp.ModuleHost.Tests
{
    public class ModulePolicyApiTests
    {
        // Mock module implementing the new Policy API explicitly
        class ModernTestModule : IEcsModule
        {
            public string Name => "ModernModule";
            
            // Explicitly set policy
            public ExecutionPolicy Policy { get; set; } = ExecutionPolicy.Synchronous();

            public void Tick(ISimulationView view, float deltaTime) { }
        }
        
        // Mock module implementing the OLD Tier API
        class LegacyTestModule : IEcsModule
        {
            public string Name => "LegacyModule";
            
            public ModuleTier Tier { get; set; } = ModuleTier.Fast;
            public int UpdateFrequency { get; set; } = 1;

            public void Tick(ISimulationView view, float deltaTime) { }
        }

        [Fact]
        public void IEcsModule_Policy_ReplacesOldAPI()
        {
            var module = new ModernTestModule
            {
                Policy = ExecutionPolicy.SlowBackground(10)
            };
            
            Assert.Equal(10, module.Policy.TargetFrequencyHz);
            Assert.Equal(RunMode.Asynchronous, module.Policy.Mode);
        }
        
        [Fact]
        public void IEcsModule_BackwardCompat_TierReturnsCorrectValue()
        {
            var fastModule = new ModernTestModule
            {
                Policy = ExecutionPolicy.FastReplica()
            };
            
            #pragma warning disable CS0618
            // Must cast to IEcsModule to access default interface implementation
            Assert.Equal(ModuleTier.Fast, ((IEcsModule)fastModule).Tier);
            #pragma warning restore CS0618
        }
        
        [Fact]
        public void IEcsModule_BackwardCompat_UpdateFrequencyComputed()
        {
            var module = new ModernTestModule
            {
                Policy = ExecutionPolicy.SlowBackground(10)
            };
            
            #pragma warning disable CS0618
            // Must cast to IEcsModule
            Assert.Equal(6, ((IEcsModule)module).UpdateFrequency);
            #pragma warning restore CS0618
        }
        
        [Fact]
        public void IEcsModule_NewModule_UsesPolicy()
        {
            var module = new ModernTestModule(); 
            
            Assert.Equal(ExecutionPolicy.Synchronous().Mode, module.Policy.Mode);
        }
        
        [Fact]
        public void IEcsModule_LegacyModule_PolicyReturnsCorrectValue()
        {
            var module = new LegacyTestModule
            {
                Tier = ModuleTier.Slow,
                UpdateFrequency = 6 // 10Hz
            };
            
            // Check that the default implementation of Policy works.
            // Must cast to IEcsModule because LegacyTestModule does not implement Policy property itself.
            var iModule = (IEcsModule)module;
            
            Assert.Equal(RunMode.Asynchronous, iModule.Policy.Mode);
            Assert.Equal(10, iModule.Policy.TargetFrequencyHz);
        }
        
        [Fact]
        public void IEcsModule_LegacyModule_FastTier_PolicyReturnsFastReplica()
        {
            var module = new LegacyTestModule
            {
                Tier = ModuleTier.Fast
            };
            
            var iModule = (IEcsModule)module;
            
            Assert.Equal(RunMode.FrameSynced, iModule.Policy.Mode);
            Assert.Equal(DataStrategy.GDB, iModule.Policy.Strategy);
            Assert.Equal(60, iModule.Policy.TargetFrequencyHz);
        }
    }
}
