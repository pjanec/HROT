using System;
using System.Linq;
using System.Collections.Generic;
using Fdp.Kernel;
using Xunit;

namespace Fdp.Tests
{
    public class EventInspectorTests
    {
        [EventId(9001)]
        public struct TestInspectorEvent
        {
            public int Value;
        }

        public class TestManagedInspectorEvent
        {
            public string Message { get; set; } = "";
        }

        [Fact]
        public void InspectNativeEvents_ReturnsPublishedEvents_AfterSwap()
        {
            // Arrange
            using var eventBus = new FdpEventBus();
            
            // Act - Publish
            eventBus.Publish(new TestInspectorEvent { Value = 42 });
            eventBus.Publish(new TestInspectorEvent { Value = 100 });
            
            // Assert Pre-Swap (Should be in Write Buffer)
            var inspectors = eventBus.GetDebugInspectors().ToList();
            Assert.Single(inspectors);
            var inspector = inspectors.First();
            Assert.Equal(typeof(TestInspectorEvent), inspector.EventType);
            
            // Read buffer should be empty initially
            Assert.Equal(0, inspector.Count); 
            Assert.Empty(inspector.InspectReadBuffer());
            
            // Write buffer contains pending events
            var pending = inspector.InspectWriteBuffer().Cast<TestInspectorEvent>().ToList();
            Assert.Equal(2, pending.Count);
            Assert.Equal(42, pending[0].Value);
            Assert.Equal(100, pending[1].Value);
            
            // Act - Swap
            eventBus.SwapBuffers();
            
            // Assert Post-Swap (Should be in Read Buffer)
            Assert.Equal(2, inspector.Count);
            var read = inspector.InspectReadBuffer().Cast<TestInspectorEvent>().ToList();
            Assert.Equal(2, read.Count);
            Assert.Equal(42, read[0].Value);
            Assert.Equal(100, read[1].Value);
            
            // Write buffer should be empty after swap
            Assert.Empty(inspector.InspectWriteBuffer());
        }

        [Fact]
        public void InspectManagedEvents_ReturnsPublishedEvents_AfterSwap()
        {
            // Arrange
            using var eventBus = new FdpEventBus();
            
            // Act - Publish
            eventBus.PublishManaged(new TestManagedInspectorEvent { Message = "Hello" });
            
            // Assert Pre-Swap
            var inspectors = eventBus.GetDebugInspectors().ToList();
            Assert.Single(inspectors);
            var inspector = inspectors.First();
            Assert.Equal(typeof(TestManagedInspectorEvent), inspector.EventType);
            
            Assert.Equal(0, inspector.Count);
            Assert.Empty(inspector.InspectReadBuffer());
            
            var pending = inspector.InspectWriteBuffer().Cast<TestManagedInspectorEvent>().ToList();
            Assert.Single(pending);
            Assert.Equal("Hello", pending[0].Message);
            
            // Act - Swap
            eventBus.SwapBuffers();
            
            // Assert Post-Swap
            Assert.Equal(1, inspector.Count);
            var read = inspector.InspectReadBuffer().Cast<TestManagedInspectorEvent>().ToList();
            Assert.Single(read);
            Assert.Equal("Hello", read[0].Message);
        }
        
        [Fact]
        public void GetDebugInspectors_ReturnsAllActiveStreams()
        {
             using var eventBus = new FdpEventBus();
             eventBus.Publish(new TestInspectorEvent()); // Native
             eventBus.PublishManaged(new TestManagedInspectorEvent()); // Managed
             
             var inspectors = eventBus.GetDebugInspectors().ToList();
             Assert.Equal(2, inspectors.Count);
             
             Assert.Contains(inspectors, i => i.EventType == typeof(TestInspectorEvent));
             Assert.Contains(inspectors, i => i.EventType == typeof(TestManagedInspectorEvent));
        }

        [Fact]
        public void Inspectors_Survive_InjectIntoCurrent()
        {
             // Test Replay scenario
             using var eventBus = new FdpEventBus();
             
             // 1. Manually create by publishing
             eventBus.Publish(new TestInspectorEvent { Value = 0 });
             eventBus.SwapBuffers(); 
             
             var inspector = eventBus.GetDebugInspectors().First();
             Assert.Equal(1, inspector.Count); 
             
             // 2. Inject data (Simulation of Replay)
             var events = new TestInspectorEvent[] { new TestInspectorEvent { Value = 999 } };
             var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<TestInspectorEvent>(events));
             
             int typeId = Fdp.Kernel.EventType<TestInspectorEvent>.Id;
             eventBus.InjectIntoCurrent(typeId, bytes);
             
             // 3. Inspect immediately (Inspector should see injected data in Read buffer)
             var read = inspector.InspectReadBuffer().Cast<TestInspectorEvent>().ToList();
             
             Assert.Equal(2, read.Count);
             Assert.Equal(0, read[0].Value);
             Assert.Equal(999, read[1].Value);
        }

        [Fact]
        public void ClearCurrentBuffers_ClearsManagedEvents()
        {
             // Arrange
             using var eventBus = new FdpEventBus();
             
             // Inject managed event (via Publish+Swap or Inject)
             eventBus.PublishManaged(new TestManagedInspectorEvent { Message = "Persist?" });
             eventBus.SwapBuffers();
             
             // Verify it exists in Read buffer
             var inspector = eventBus.GetDebugInspectors().First();
             Assert.Equal(1, inspector.Count);
             
             // Act
             eventBus.ClearCurrentBuffers();
             
             // Assert
             Assert.Equal(0, inspector.Count);
             Assert.Empty(inspector.InspectReadBuffer());
        }
    }
}
