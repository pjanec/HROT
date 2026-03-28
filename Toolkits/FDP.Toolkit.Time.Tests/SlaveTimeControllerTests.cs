using System;
using System.Diagnostics;
using System.Collections.Generic;
using ModuleHost.Core.Time;
using Fdp.Kernel;
using Xunit;

using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Messages;

namespace FDP.Toolkit.Time.Tests
{
    public class SlaveTimeControllerTests
    {
        private long _currentTicks = 0;
        private readonly long _freq = Stopwatch.Frequency;
        
        private long GetTicks() => _currentTicks;
        
        private void AdvanceTime(double seconds)
        {
            _currentTicks += (long)(seconds * _freq);
        }
        
        [Fact]
        public void Update_AdvancesTimeUsingLocalClock()
        {
            var bus = new FdpEventBus();
            var controller = new SlaveTimeController(bus, TimeConfig.Default, GetTicks);
            
            AdvanceTime(0.1);
            var t = controller.Update();
            float dt = t.DeltaTime;
            double total = t.TotalTime;
            
            Assert.Equal(0.1f, dt, precision: 4);
            Assert.Equal(0.1, total, precision: 4);
        }
        
        [Fact]
        public void Update_AdjustsDtWhenBehindMaster()
        {
            var config = new TimeConfig 
            { 
               PLLGain = 1.0, // Aggressive for test
               MaxSlew = 0.5f, // Was 0.5 double, float required? TimeConfig def uses float.
               AverageLatencyTicks = 0
            };

            var bus = new FdpEventBus();
            var controller = new SlaveTimeController(bus, config, GetTicks);
            
            AdvanceTime(0.1);
            
            // Change config to simulate latency expectation
            config.AverageLatencyTicks = (long)(0.010 * _freq);
            
            AdvanceTime(0.1); 
            
            // Pulse suggests we should be ahead (due to latency expectation)
            bus.Publish(new TimePulseDescriptor { MasterWallTicks = 0, TimeScale = 1.0f });
            bus.SwapBuffers();
            
            float dt = controller.Update().DeltaTime;
            
            // Expected dt > 0.1 because we speed up
            Assert.True(dt > 0.1f, $"dt {dt} should be > 0.1");
        }
        
        [Fact]
        public void Update_CalculatesTotalTimeRespectingScale()
        {
            var bus = new FdpEventBus();
            var controller = new SlaveTimeController(bus, TimeConfig.Default, GetTicks);
            
            AdvanceTime(0.1);
            double total = controller.Update().TotalTime;
            Assert.Equal(0.1, total, precision: 2);
            
            bus.Publish(new TimePulseDescriptor { TimeScale = 2.0f });
            bus.SwapBuffers();
            
            AdvanceTime(0.1);
            total = controller.Update().TotalTime;
            
            // 0.1 (first part) + 0.1 * 2.0 (second part) = 0.3
            Assert.Equal(0.3, total, precision: 2);
        }
        
        [Fact]
        public void OnTimePulse_HardSnap_ResetVirtualClock()
        {
            var config = new TimeConfig { SnapThresholdMs = 100 };
            var bus = new FdpEventBus();
            var controller = new SlaveTimeController(bus, config, GetTicks);
            
            AdvanceTime(1.0);
            controller.Update();
            
            AdvanceTime(5.0);
            // Trigger Hard Snap
            bus.Publish(new TimePulseDescriptor { MasterWallTicks = 0, TimeScale = 1.0f });
            bus.SwapBuffers();
            
            AdvanceTime(0.1);
            var t = controller.Update();
            float dt = t.DeltaTime;
            double total = t.TotalTime;
            
            // With fix: dt should be 0.0 (snap consumes delta to 'Now')
            // Without fix: dt would include the gap (5.1).
            Assert.Equal(0.0f, dt, precision: 2);
            Assert.Equal(6.1, total, precision: 1); 
        }

        /// <summary>
        /// CGF1-S0203: <see cref="SlaveTimeController.SeedState"/> must bypass the
        /// JitterFilter PLL and set <c>_virtualWallTicks</c> directly, so that the very
        /// next <c>Update()</c> returns the seeded <c>TotalTime</c> without any slew.
        /// </summary>
        [Fact]
        public void SeedState_BypassesJitterFilter()
        {
            var bus = new FdpEventBus();
            var controller = new SlaveTimeController(bus, TimeConfig.Default, GetTicks);

            // Advance to TotalTime ≈ 1.0
            AdvanceTime(1.0);
            var initial = controller.Update();
            Assert.Equal(1.0, initial.TotalTime, precision: 2);

            // Seed directly to 900 s — bypasses PLL; no gradual slew from 1.0
            controller.SeedState(new GlobalTime { TotalTime = 900.0, TotalWallTicks = 0L });

            // Tiny advance so rawDelta ≈ 0
            AdvanceTime(0.001);
            var seeded = controller.Update();

            // Must be ≈ 900, not slewing toward it from 1.0
            Assert.Equal(900.0, seeded.TotalTime, precision: 1);
        }

        /// <summary>
        /// CGF1-S0203 / Part A.2: A non-zero <c>TotalWallTicks</c> seeded via
        /// <see cref="SlaveTimeController.SeedState"/> must survive the very next
        /// <see cref="SlaveTimeController.Update"/> call within a small delta tolerance —
        /// proving the PLL jitter filter is bypassed and the baseline is preserved for
        /// Future Barrier comparisons.
        /// </summary>
        [Fact]
        public void SeedState_NonZeroWallTicks_ArePreservedAfterUpdate()
        {
            var bus = new FdpEventBus();
            var controller = new SlaveTimeController(bus, TimeConfig.Default, GetTicks);

            const long seedWallTicks = 987_654_321L;

            // Seed with a non-zero TotalWallTicks to prove the baseline is preserved
            controller.SeedState(new GlobalTime { TotalTime = 50.0, TotalWallTicks = seedWallTicks });

            // Advance a single small tick (≈ 1 ms) so rawDelta > 0 but tiny
            AdvanceTime(0.001);
            var afterUpdate = controller.Update();

            // Delta contributed by a 1 ms tick: Stopwatch.Frequency / 1000
            long maxExpectedDelta = Stopwatch.Frequency / 100; // generous 10 ms tolerance

            Assert.True(
                afterUpdate.TotalWallTicks >= seedWallTicks,
                $"TotalWallTicks should be >= seed ({seedWallTicks}); got {afterUpdate.TotalWallTicks}");
            Assert.True(
                afterUpdate.TotalWallTicks < seedWallTicks + maxExpectedDelta,
                $"TotalWallTicks drifted too far from seed ({seedWallTicks}); got {afterUpdate.TotalWallTicks}");
        }
    }
}
