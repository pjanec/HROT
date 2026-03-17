using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Kernel;
using Xunit;
using ModuleHost.Core.Time;

using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Messages;

namespace FDP.Toolkit.Time.Tests
{
    /// <summary>
    /// Tests for WCR-BATCH-02: GlobalTime.TotalWallTicks population in all time controllers.
    /// Covers tasks WCR-P3-T002, WCR-P3-T003, WCR-P3-T004.
    /// </summary>
    public class WcrBatch02TimeTests
    {
        // ================================================================
        // WCR-P3-T002: MasterTimeController populates TotalWallTicks
        // ================================================================

        [Fact]
        public void WCR_P3_T002_Master_TotalWallTicks_NonZeroAfterUpdate()
        {
            // Arrange: any non-trivial time will have elapsed before the first Update()
            var bus = new FdpEventBus();
            var controller = new MasterTimeController(bus, TimeConfig.Default);

            // Short real wait so that ElapsedTicks > 0 for the first frame
            System.Threading.Thread.Sleep(5);

            // Act
            var result = controller.Update();

            // Assert
            Assert.True(result.TotalWallTicks > 0,
                $"TotalWallTicks should be > 0 after first Update(), got {result.TotalWallTicks}");
        }

        [Fact]
        public void WCR_P3_T002_Master_TotalWallTicks_Monotonically_NonDecreasing()
        {
            var bus = new FdpEventBus();
            var controller = new MasterTimeController(bus, TimeConfig.Default);

            long prev = long.MinValue;
            for (int i = 0; i < 5; i++)
            {
                System.Threading.Thread.Sleep(1);
                var result = controller.Update();
                Assert.True(result.TotalWallTicks >= prev,
                    $"Frame {i}: TotalWallTicks {result.TotalWallTicks} must be >= previous {prev}");
                prev = result.TotalWallTicks;
            }
        }

        [Fact]
        public void WCR_P3_T002_Master_GetCurrentState_Returns_TotalWallTicks()
        {
            var bus = new FdpEventBus();
            var controller = new MasterTimeController(bus, TimeConfig.Default);

            System.Threading.Thread.Sleep(5);
            var updateResult = controller.Update();

            var stateResult = controller.GetCurrentState();

            // GetCurrentState() must return the same TotalWallTicks that Update() returned
            Assert.Equal(updateResult.TotalWallTicks, stateResult.TotalWallTicks);
        }

        // ================================================================
        // WCR-P3-T003: SlaveTimeController populates TotalWallTicks from PLL
        // ================================================================

        [Fact]
        public void WCR_P3_T003_Slave_TotalWallTicks_FromPLL()
        {
            // Arrange: controllable tick source so virtualWallTicks is deterministic
            long currentTicks = 0;
            long freq = Stopwatch.Frequency;
            var bus = new FdpEventBus();
            var controller = new SlaveTimeController(bus, TimeConfig.Default, () => currentTicks);

            // Advance local tick source by 0.1 s, simulating elapsed real time
            currentTicks += (long)(0.1 * freq);

            // Act: deliver a TimePulse then call Update()
            bus.Publish(new TimePulseDescriptor
            {
                MasterWallTicks = currentTicks,
                SimTimeSnapshot = 0.1,
                TimeScale = 1.0f,
                SequenceId = 1
            });
            bus.SwapBuffers();

            var result = controller.Update();

            // Assert: TotalWallTicks is populated from _virtualWallTicks (non-zero)
            Assert.True(result.TotalWallTicks > 0,
                $"TotalWallTicks should be > 0, got {result.TotalWallTicks}");
        }

        [Fact]
        public void WCR_P3_T003_Slave_TotalWallTicks_AfterSnap()
        {
            // Arrange: config with a tiny snap threshold so any drift triggers a snap
            long currentTicks = 0;
            long freq = Stopwatch.Frequency;
            var config = new TimeConfig { SnapThresholdMs = 1 }; // 1 ms threshold → easy to trigger
            var bus = new FdpEventBus();
            var controller = new SlaveTimeController(bus, config, () => currentTicks);

            // Advance by 1 second locally
            currentTicks += freq;
            controller.Update(); // consume initial state

            // Advance by another second and inject a pulse claiming master is 5 seconds ahead
            currentTicks += freq;
            long masterTicks = currentTicks + (long)(5.0 * freq); // large discrepancy → hard snap
            bus.Publish(new TimePulseDescriptor
            {
                MasterWallTicks = masterTicks,
                SimTimeSnapshot = 5.0,
                TimeScale = 1.0f,
                SequenceId = 2
            });
            bus.SwapBuffers();

            var result = controller.Update();

            // After a hard snap, _virtualWallTicks = targetWallTicks (non-zero)
            Assert.True(result.TotalWallTicks > 0,
                $"Post-snap TotalWallTicks should be > 0, got {result.TotalWallTicks}");
        }

        [Fact]
        public void WCR_P3_T003_Slave_GetCurrentState_Returns_TotalWallTicks()
        {
            long currentTicks = 0;
            long freq = Stopwatch.Frequency;
            var bus = new FdpEventBus();
            var controller = new SlaveTimeController(bus, TimeConfig.Default, () => currentTicks);

            currentTicks += (long)(0.1 * freq);
            var updateResult = controller.Update();

            var stateResult = controller.GetCurrentState();

            Assert.Equal(updateResult.TotalWallTicks, stateResult.TotalWallTicks);
        }

        // ================================================================
        // WCR-P3-T004: Deterministic controllers populate TotalWallTicks
        // ================================================================

        [Fact]
        public void WCR_P3_T004_SteppedMaster_TotalWallTicks_NonZeroAfterStep()
        {
            var bus = new FdpEventBus();
            var controller = new SteppedMasterController(
                bus,
                new HashSet<int>(),     // no slaves → advances immediately
                new TimeConfig { FixedDeltaSeconds = 0.016f });

            // Act: Update() will call Step() since no slaves are waiting
            var result = controller.Update();

            Assert.True(result.TotalWallTicks > 0,
                $"TotalWallTicks must be > 0 after first step, got {result.TotalWallTicks}");
        }

        [Fact]
        public void WCR_P3_T004_SteppedMaster_TotalWallTicks_Monotonically_NonDecreasing()
        {
            var bus = new FdpEventBus();
            var controller = new SteppedMasterController(
                bus,
                new HashSet<int>(),
                new TimeConfig { FixedDeltaSeconds = 0.016f });

            long prev = long.MinValue;
            for (int i = 0; i < 5; i++)
            {
                var result = controller.Update();
                Assert.True(result.TotalWallTicks >= prev,
                    $"Frame {i}: TotalWallTicks {result.TotalWallTicks} must be >= previous {prev}");
                prev = result.TotalWallTicks;
            }
        }

        [Fact]
        public void WCR_P3_T004_SteppingController_TotalWallTicks_ZeroBeforeStep()
        {
            // Arrange: seed with zero UnscaledTotalTime
            var seedState = new GlobalTime
            {
                FrameNumber = 0,
                TotalTime = 0.0f,
                UnscaledTotalTime = 0.0,
                TimeScale = 1.0f
            };
            var controller = new SteppingTimeController(seedState);

            // Act: Update() before any Step()
            var result = controller.Update();

            // Assert: no time elapsed → TotalWallTicks == 0
            Assert.Equal(0L, result.TotalWallTicks);
        }

        [Fact]
        public void WCR_P3_T004_SteppingController_TotalWallTicks_Increases_After_Step()
        {
            var seedState = new GlobalTime
            {
                FrameNumber = 0,
                TotalTime = 0.0f,
                UnscaledTotalTime = 0.0,
                TimeScale = 1.0f
            };
            var controller = new SteppingTimeController(seedState);

            // Act: step by 1 second of fixed delta
            var result = controller.Step(1.0f);

            // Assert: TotalWallTicks = 1 * Stopwatch.Frequency (≈ 1 second in ticks)
            long expected = (long)(1.0 * Stopwatch.Frequency);
            Assert.True(result.TotalWallTicks > 0,
                $"TotalWallTicks should be > 0 after stepping 1 second, got {result.TotalWallTicks}");
            // Should be within 1% of expected Stopwatch ticks for 1 second
            Assert.InRange(result.TotalWallTicks, (long)(expected * 0.99), (long)(expected * 1.01));
        }
    }
}
