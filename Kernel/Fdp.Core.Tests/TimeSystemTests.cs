using System;
using System.Threading;
using Xunit;
using Fdp.Kernel;

namespace Fdp.Tests
{
    public class TimeSystemTests : IDisposable
    {
        private EntityRepository _repo;

        public TimeSystemTests()
        {
            _repo = new EntityRepository();
            // Register expected component
            _repo.RegisterComponent<GlobalTime>();
        }

        public void Dispose()
        {
            _repo.Dispose();
        }

        [Fact]
        public void Step_DeterministicMode_AdvancesTimeCorrectly()
        {
            var system = new TimeSystem(_repo);
            
            system.Step(0.1f);
            
            ref var time = ref _repo.GetSingletonUnmanaged<GlobalTime>();
            Assert.Equal(0.1f, time.DeltaTime);
            Assert.Equal(0.1, time.TotalTime, precision: 6);
            Assert.Equal(1, time.FrameNumber);
            
            system.Step(0.2f);
            
            time = ref _repo.GetSingletonUnmanaged<GlobalTime>();
            Assert.Equal(0.2f, time.DeltaTime);
            Assert.Equal(0.3, time.TotalTime, precision: 6);
            Assert.Equal(2, time.FrameNumber);
        }
        
        [Fact]
        public void Update_RealTimeMode_ReadingClock()
        {
            var clock = new ManualTimeProvider();
            var system = new TimeSystem(_repo, clock);
            
            // Initial state (created at clock=0)
            
            // Advance clock by 100ms
            clock.Advance(TimeSpan.FromMilliseconds(100));
            
            system.Update();
            
            ref var time = ref _repo.GetSingletonUnmanaged<GlobalTime>();
            Assert.Equal(0.1f, time.DeltaTime);
            Assert.Equal(0.1, time.TotalTime, precision: 4);
            Assert.Equal(1, time.FrameNumber);
            
            // Advance clock by 50ms
            clock.Advance(TimeSpan.FromMilliseconds(50));
            system.Update();
            
            time = ref _repo.GetSingletonUnmanaged<GlobalTime>();
            Assert.Equal(0.05f, time.DeltaTime);
            Assert.Equal(0.15, time.TotalTime, precision: 4);
            Assert.Equal(2, time.FrameNumber);
        }
        
        [Fact]
        public void HasTimeRemaining_ChecksBudget()
        {
            var clock = new ManualTimeProvider();
            var system = new TimeSystem(_repo, clock);
            
            // Start frame with 10ms budget
            // Simulate 16ms timestamp delta so logic runs
            clock.Advance(TimeSpan.FromMilliseconds(16));
            system.Update(budgetMs: 10.0);
            
            // 0ms elapsed locally since start of frame logic, requires 5ms
            Assert.True(system.HasTimeRemaining(5.0));
            
            // Advance clock by 6ms
            clock.Advance(TimeSpan.FromMilliseconds(6));
            
            // 6ms elapsed. 10 - 6 = 4ms remaining.
            
            // Try to use 5ms (should fail)
            Assert.False(system.HasTimeRemaining(5.0));
            
            // Try to use 3ms (should pass)
            Assert.True(system.HasTimeRemaining(3.0));
            
            // Advance by 5ms (total 11ms elapsed)
            clock.Advance(TimeSpan.FromMilliseconds(5));
            
            // Budget exceeded
            Assert.False(system.HasTimeRemaining(1.0));
        }
        
        [Fact]
        public void Step_ResetsBudgetTimer()
        {
            var clock = new ManualTimeProvider();
            var system = new TimeSystem(_repo, clock);
            
            clock.Advance(TimeSpan.FromSeconds(1)); // Some time passed before frame
            
            // Start deterministic frame (Step) -> mocks infinite budget internally
            system.Step(0.016f);
            
            // Step sets budget to Infinity
            Assert.True(system.HasTimeRemaining(99999.0));
        }
    }
    
    // Simple mock for TimeProvider
    public class ManualTimeProvider : TimeProvider
    {
        private long _ticks;
        private readonly long _frequency = TimeSpan.TicksPerSecond; // 10,000,000 ticks per sec
        
        public ManualTimeProvider()
        {
            _ticks = 0;
        }
        
        public override long GetTimestamp() => _ticks;
        
        public override long TimestampFrequency => _frequency;
        
        public void Advance(TimeSpan span)
        {
            _ticks += span.Ticks;
        }
        
        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(_ticks, TimeSpan.Zero);
        }
    }
}
