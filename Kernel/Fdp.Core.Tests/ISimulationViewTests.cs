using Xunit;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using System;
using System.Reflection;

namespace Fdp.Tests
{
    public class ISimulationViewTests
    {
        [Fact]
        public void Interface_HasAllRequiredMembers()
        {
            var type = typeof(ISimulationView);
            
            Assert.NotNull(type.GetProperty("Tick"));
            Assert.NotNull(type.GetProperty("Time"));
            Assert.NotNull(type.GetMethod("GetComponentRO"));
            Assert.NotNull(type.GetMethod("GetManagedComponentRO"));
            Assert.NotNull(type.GetMethod("IsAlive"));
            Assert.NotNull(type.GetMethod("ConsumeEvents"));
            Assert.NotNull(type.GetMethod("Query"));
        }

        [Fact]
        public void Interface_NoDisposable()
        {
            var type = typeof(ISimulationView);
            Assert.False(typeof(IDisposable).IsAssignableFrom(type));
        }
    }
}
