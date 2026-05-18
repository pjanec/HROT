using System.Collections.Generic;
using Xunit;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost.Tests
{
    public class ISimulationViewTests
    {
        [Fact]
        public void ReadManagedEvents_ReturnsEvents()
        {
            // Arrange
            var repo = new EntityRepository();
            ISimulationView view = repo;
            
            // Act: Publish managed event
            repo.Bus.PublishManaged(new TestManagedEvent { Message = "Test" });
            
            // CRITICAL: Swap buffers to make events available
            repo.Bus.SwapBuffers();
            
            // Consume via interface
            var events = view.ReadManagedEvents<TestManagedEvent>();
            
            // Assert
            Assert.NotNull(events);
            Assert.Single(events);
            Assert.Equal("Test", events[0].Message);
        }
    }
    
    class TestManagedEvent
    {
        public string Message { get; set; } = "";
    }
}
