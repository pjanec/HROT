using System;
using System.IO;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using MessagePack;
using Xunit;

namespace Fdp.Tests
{
    /// <summary>
    /// Tests to verify ComponentMask synchronization during Flight Recorder replay.
    /// This tests for the critical bug where managed component data is restored but 
    /// the EntityHeader.ComponentMask is not updated to reflect the component presence.
    /// </summary>
    public class ComponentMaskSynchronizationTests : IDisposable
    {
        private readonly string _testFilePath;
        
        // Test managed component
        [ComponentId(197)]
        public record TestManagedComponent
        {
            [MessagePack.Key(0)]
            public string Name { get; set; } = "";
            [MessagePack.Key(1)]
            public int Value { get; set; }
        }
        
        // Test unmanaged component  
        [ComponentId(198)]
        public struct TestUnmanagedComponent
        {
            public int X, Y, Z;
        }
        
        public ComponentMaskSynchronizationTests()
        {
            _testFilePath = Path.GetTempFileName();
        }
        
        public void Dispose()
        {
            try
            {
                if (File.Exists(_testFilePath))
                    File.Delete(_testFilePath);
            }
            catch { /* Best effort cleanup */ }
        }
        
        [Fact]
        public void ComponentMask_ManagedComponentReplay_BugReproduction()
        {
            // This test reproduces the bug where managed component data is restored
            // but ComponentMask is not updated, causing EntityQuery to miss the entity
            
            // === RECORD PHASE ===
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<TestManagedComponent>();
            recordRepo.RegisterComponent<TestUnmanagedComponent>();
            
            var entity = recordRepo.CreateEntity();
            
            // Add both managed and unmanaged components
            recordRepo.AddManagedComponent(entity, new TestManagedComponent 
            { 
                Name = "TestEntity", 
                Value = 42 
            });
            recordRepo.AddComponent(entity, new TestUnmanagedComponent 
            { 
                X = 10, Y = 20, Z = 30 
            });
            
            // Record
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo);
            }
            
            // === REPLAY PHASE ===
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<TestManagedComponent>();
            replayRepo.RegisterComponent<TestUnmanagedComponent>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo);
            }
            
            // === VERIFICATION PHASE ===
            
            // Check that entity was restored
            Assert.Equal(1, replayRepo.EntityCount);
            
            // Verify ComponentMask is correct - this is the core of the bug
            ref var header = ref replayRepo.GetHeader(0);
            var managedTypeId = ManagedComponentType<TestManagedComponent>.ID;
            var unmanagedTable = replayRepo.GetComponentTable<TestUnmanagedComponent>();
            var unmanagedTypeId = unmanagedTable.ComponentTypeId;
            
            Assert.True(header.ComponentMask.IsSet(unmanagedTypeId), 
                "Unmanaged component mask bit should be set after replay");
            Assert.True(header.ComponentMask.IsSet(managedTypeId), 
                "Managed component mask bit should be set after replay");
        }
        
        [Fact]
        public void ComponentMask_QueryAfterReplay_BugImpactDemonstration()
        {
            // This test demonstrates the impact of the ComponentMask sync bug:
            // EntityQuery fails to find entities with managed components after replay
            
            // === RECORD PHASE ===
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<TestManagedComponent>();
            
            var entity = recordRepo.CreateEntity();
            recordRepo.AddManagedComponent(entity, new TestManagedComponent 
            { 
                Name = "QueryTest", 
                Value = 123 
            });
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo);
            }
            
            // === REPLAY PHASE ===
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<TestManagedComponent>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo);
            }
            
            // === BUG DEMONSTRATION: EntityQuery fails ===
            
            // Data exists but ComponentMask is wrong - this is the core issue
            var restoredEntity = new Entity(0, 1);
            var comp = replayRepo.GetComponentRW<TestManagedComponent>(restoredEntity);
            Assert.NotNull(comp);
            Assert.Equal("QueryTest", comp.Name);
            Assert.Equal(123, comp.Value);
            
            // The ComponentMask should reflect that the entity has the managed component
            ref var header = ref replayRepo.GetHeader(0);
            var managedTypeId = ManagedComponentType<TestManagedComponent>.ID;
            
            // This will FAIL with current bug - managed component mask not updated
            Assert.True(header.ComponentMask.IsSet(managedTypeId), 
                "Managed component mask should be set after replay");
        }
        
        [Fact]
        public void ComponentMask_DeltaFrame_ManagedComponentAddition()
        {
            // This test checks ComponentMask sync for managed components added in delta frames
            
            // === INITIAL RECORD PHASE ===
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<TestManagedComponent>();
            recordRepo.RegisterComponent<TestUnmanagedComponent>();
            
            var entity = recordRepo.CreateEntity();
            // Start with only unmanaged component
            recordRepo.AddComponent(entity, new TestUnmanagedComponent { X = 1, Y = 2, Z = 3 });
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo); // Keyframe with only unmanaged
                
                recordRepo.Tick();
                // Add managed component in next frame
                recordRepo.AddManagedComponent(entity, new TestManagedComponent 
                { 
                    Name = "DeltaAdded", 
                    Value = 999 
                });
                
                recorder.CaptureFrame(recordRepo, recordRepo.GlobalVersion - 1, blocking: true); // Delta frame
            }
            
            // === REPLAY PHASE ===
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<TestManagedComponent>();
            replayRepo.RegisterComponent<TestUnmanagedComponent>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo); // Keyframe
                reader.ReadNextFrame(replayRepo); // Delta
            }
            
            // === VERIFICATION ===
            var restoredEntity = new Entity(0, 1);
            Assert.True(replayRepo.IsAlive(restoredEntity));
            
            // Component data should be correct
            var managedComp = replayRepo.GetComponentRW<TestManagedComponent>(restoredEntity);
            Assert.NotNull(managedComp);
            Assert.Equal("DeltaAdded", managedComp.Name);
            Assert.Equal(999, managedComp.Value);
            
            // ComponentMask should have both bits set
            ref var header = ref replayRepo.GetHeader(0);
            var managedTypeId = ManagedComponentType<TestManagedComponent>.ID;
            var unmanagedTable = replayRepo.GetComponentTable<TestUnmanagedComponent>();
            var unmanagedTypeId = unmanagedTable.ComponentTypeId;
            
            Assert.True(header.ComponentMask.IsSet(unmanagedTypeId), 
                "Unmanaged component mask should remain set");
            Assert.True(header.ComponentMask.IsSet(managedTypeId), 
                "Managed component mask should be set after delta replay");
                
            // Unmanaged query should work
            var query = replayRepo.Query().With<TestUnmanagedComponent>().Build();
            int count = 0;
            foreach (var e in query)
            {
                count++;
            }
            Assert.Equal(1, count);
        }
        
        [Fact]
        public void ComponentMask_UnmanagedComponents_WorksCorrectly()
        {
            // Control test: verify unmanaged components work correctly (they should)
            
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<TestUnmanagedComponent>();
            
            var entity = recordRepo.CreateEntity();
            recordRepo.AddComponent(entity, new TestUnmanagedComponent { X = 5, Y = 10, Z = 15 });
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo);
            }
            
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<TestUnmanagedComponent>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo);
            }
            
            // Verify everything works for unmanaged
            var restoredEntity = new Entity(0, 1);
            Assert.True(replayRepo.IsAlive(restoredEntity));
            
            var comp = replayRepo.GetComponentRW<TestUnmanagedComponent>(restoredEntity);
            Assert.Equal(5, comp.X);
            Assert.Equal(10, comp.Y);
            Assert.Equal(15, comp.Z);
            
            // ComponentMask should be correct
            ref var header = ref replayRepo.GetHeader(0);
            var unmanagedTable = replayRepo.GetComponentTable<TestUnmanagedComponent>();
            var typeId = unmanagedTable.ComponentTypeId;
            Assert.True(header.ComponentMask.IsSet(typeId));
            
            // Query should work
            var query = replayRepo.Query().With<TestUnmanagedComponent>().Build();
            int count = 0;
            foreach (var e in query)
            {
                count++;
            }
            Assert.Equal(1, count);
        }
        
        [Fact]
        public void ComponentMask_MixedComponents_PartialBugReproduction()
        {
            // Test that shows unmanaged masks work but managed masks fail
            
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<TestManagedComponent>();
            recordRepo.RegisterComponent<TestUnmanagedComponent>();
            
            var entity = recordRepo.CreateEntity();
            recordRepo.AddManagedComponent(entity, new TestManagedComponent { Name = "Mixed", Value = 50 });
            recordRepo.AddComponent(entity, new TestUnmanagedComponent { X = 1, Y = 2, Z = 3 });
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo);
            }
            
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<TestManagedComponent>();
            replayRepo.RegisterComponent<TestUnmanagedComponent>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo);
            }
            
            // Both component data should be restored
            var restoredEntity = new Entity(0, 1);
            var managedComp = replayRepo.GetComponentRW<TestManagedComponent>(restoredEntity);
            var unmanagedComp = replayRepo.GetComponentRW<TestUnmanagedComponent>(restoredEntity);
            
            Assert.NotNull(managedComp);
            Assert.Equal("Mixed", managedComp.Name);
            Assert.Equal(1, unmanagedComp.X);
            
            // Unmanaged query works
            var unmanagedQuery = replayRepo.Query().With<TestUnmanagedComponent>().Build();
            int unmanagedCount = 0;
            foreach (var e in unmanagedQuery)
            {
                unmanagedCount++;
            }
            Assert.Equal(1, unmanagedCount);
            
            // Managed component should exist in data but mask should be wrong
            // (using same variables as above)
            Assert.NotNull(managedComp);
            Assert.Equal("Mixed", managedComp.Name);
            
            // ComponentMask should have managed bit set but currently doesn't (the bug)
            ref var header = ref replayRepo.GetHeader(0);
            var managedTypeId = ManagedComponentType<TestManagedComponent>.ID;
            Assert.True(header.ComponentMask.IsSet(managedTypeId), 
                "Managed component mask should be set after replay - this will FAIL due to bug");
        }
    }
}