using System.Collections.Generic;
using Xunit;
using Fdp.Kernel;

namespace Fdp.Tests
{
    [Collection("ComponentTests")]
    public class TimeSlicedIteratorTests
    {
        public TimeSlicedIteratorTests()
        {
            ComponentTypeRegistry.Clear();
        }
        
        [Fact]
        public void CompletesInOneGo_IfBudgetHigh()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            // Create 100 entities
            for (int i = 0; i < 100; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i });
            }
            
            var state = new IteratorState();
            int count = 0;
            
            // Budget 1000ms (huge)
            repo.QueryTimeSliced(repo.Query().With<Position>().Build(), state, 1000.0, TimeSliceMetric.WallClockTime, e => 
            {
                count++;
            });
            
            Assert.True(state.IsComplete);
            Assert.Equal(100, count);
        }
        
        [Fact]
        public void PausesAndResumes_IfBudgetExceeded()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            // Create 200 entities. CheckInterval is 64.
            // If budget is -1 (immediate stop), it should stop after 64 (0..63 check).
            // Actually (i % 64 == 0).
            // i=0 -> Check. Elapsed ~0. If budget < 0, stops immediately?
            // i=0 processed.
            // Then check.
            
            for (int i = 0; i < 200; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i });
            }
            
            var state = new IteratorState();
            int totalProcessed = 0;
            
            // Pass 1: Budget negative to force stop
            // Note: i=0 check happens. Stops after 1st entity?
            // QueryTimeSliced implementation:
            // Action(i)
            // if (i % 64 == 0) Check.
            // i=0 -> 0%64==0. Check. Stop.
            // So processes 1 entity (Index 0).
            
            repo.QueryTimeSliced(repo.Query().With<Position>().Build(), state, -1.0, TimeSliceMetric.WallClockTime, e => totalProcessed++);
            
            Assert.False(state.IsComplete);
            Assert.Equal(1, totalProcessed); 
            Assert.Equal(1, state.NextEntityId);
            
            // Pass 2: Resume with huge budget
            repo.QueryTimeSliced(repo.Query().With<Position>().Build(), state, 1000.0, TimeSliceMetric.WallClockTime, e => totalProcessed++);
            
            Assert.True(state.IsComplete);
            Assert.Equal(200, totalProcessed);
        }
        
        [Fact]
        public void Resets_WhenCalledAfterComplete()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position());
            
            var state = new IteratorState();
            
            // Run 1
            repo.QueryTimeSliced(repo.Query().With<Position>().Build(), state, 1000, TimeSliceMetric.WallClockTime, x => { });
            Assert.True(state.IsComplete);
            
            // Run 2 (Should restart)
            int count = 0;
            repo.QueryTimeSliced(repo.Query().With<Position>().Build(), state, 1000, TimeSliceMetric.WallClockTime, x => count++);
            Assert.Equal(1, count);
            Assert.True(state.IsComplete);
        }
            
            [Fact]
        public void Deterministic_Slicing_Works()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            // Create 100 entities
            for (int i = 0; i < 100; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i });
            }
            
            var state = new IteratorState();
            int totalProcessed = 0;
            
            // Step 1: Limit to 30 entities.
            repo.QueryTimeSliced(repo.Query().With<Position>().Build(), state, 30.0, TimeSliceMetric.EntityCount, e => totalProcessed++);
            
            Assert.False(state.IsComplete);
            Assert.Equal(30, totalProcessed);
            
            // Step 2: Next 30
            repo.QueryTimeSliced(repo.Query().With<Position>().Build(), state, 30.0, TimeSliceMetric.EntityCount, e => totalProcessed++);
            
            Assert.False(state.IsComplete);
            Assert.Equal(60, totalProcessed);
            
            // Step 3: Finish rest (40 items)
            repo.QueryTimeSliced(repo.Query().With<Position>().Build(), state, 100.0, TimeSliceMetric.EntityCount, e => totalProcessed++);
            
            Assert.True(state.IsComplete);
            Assert.Equal(100, totalProcessed);
    
    }
        [Fact]
        public void DefaultMetric_Works()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.DefaultTimeSliceMetric = TimeSliceMetric.EntityCount;

            // Create 50 entities
            for (int i = 0; i < 50; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Position { X = i });
            }

            var state = new IteratorState();
            int count = 0;

            // Budget 10. Metric is Default (EntityCount).
            repo.QueryTimeSliced(repo.Query().With<Position>().Build(), state, 10.0, e => count++);
            
            Assert.False(state.IsComplete);
            Assert.Equal(10, count);
        }
    }
}
