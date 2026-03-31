using System;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using Fdp.Kernel;
using ModuleHost.Core;
using ModuleHost.Core.Providers;
using ModuleHost.Core.Network;
using ModuleHost.Core.Time;
using Xunit;

using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Messages;

namespace FDP.Toolkit.Time.Tests
{
    public class DistributedPauseTests
    {
        private FdpEventBus _sharedBus;
        
        public DistributedPauseTests()
        {
            _sharedBus = new FdpEventBus();
            // Assuming FdpEventBus works in-process for pub/sub
        }
        
        [Fact]
        public void FutureBarrier_Pause_SyncsAtScheduledFrame()
        {
            // 1. Setup Master
            var masterRepo = new EntityRepository();
            var masterKernel = new ModuleHostKernel(masterRepo, new EventAccumulator());
            var masterConfig = new TimeControllerConfig 
            { 
                Role = TimeRole.Master,
                SyncConfig = new TimeConfig { LookaheadWallTicks = (long)(0.01 * Stopwatch.Frequency) }
            };
            
            // Replaced ConfigureTime
            var masterCtrl = new MasterTimeController(masterRepo.Bus, masterConfig.SyncConfig);
            masterKernel.SetTimeController(masterCtrl);
            
            masterKernel.Initialize();
            
            var coordinator = new DistributedTimeCoordinator(_sharedBus, masterKernel, masterConfig, new HashSet<int>{1});
            
            // 2. Setup Slave
            var slaveRepo = new EntityRepository();
            var slaveKernel = new ModuleHostKernel(slaveRepo, new EventAccumulator());
            var slaveConfig = new TimeControllerConfig 
            { 
                Role = TimeRole.Slave, 
                LocalNodeId = 1,
                TickProvider = () => Stopwatch.GetTimestamp() // Use real time or mock?
            };
            
            // Replaced ConfigureTime
            var slaveCtrl = new SlaveTimeController(slaveRepo.Bus, slaveConfig.SyncConfig, slaveConfig.TickProvider);
            slaveKernel.SetTimeController(slaveCtrl);
            
            slaveKernel.Initialize();
            
            var listener = new SlaveTimeModeListener(_sharedBus, slaveKernel, slaveConfig);
            
            // 3. Run a bit (Continuous)
            // Need to tick kernel to advance frame count
            masterKernel.Update();
            slaveKernel.Update();
            
            Assert.Equal(TimeMode.Continuous, masterKernel.GetTimeController().GetMode());
            Assert.Equal(TimeMode.Continuous, slaveKernel.GetTimeController().GetMode());
            
            long startFrame = masterKernel.CurrentTime.FrameNumber;
            
            // 4. Initiate Pause
            coordinator.SwitchToDeterministic(new HashSet<int>{1});
            
            // Verify NOT paused yet
            masterKernel.Update(); // Updates Time, but Coordinator.Update needs explicit call?
            // Coordinator.Update is manual in DemoSimulation. So we must call it.
            coordinator.Update();
            
            // Should still be Continuous (Barrier is +5 frames)
            Assert.Equal(TimeMode.Continuous, masterKernel.GetTimeController().GetMode());
            
            // Slave Tick
            slaveKernel.Update();
            listener.Update();
            Assert.Equal(TimeMode.Continuous, slaveKernel.GetTimeController().GetMode());
            
            // 5. Advance Time until Barrier
            // Barrier = startFrame + 1 (Master updated once before switch?) + 5 = ~6 frames higher
            
            // Loop until barrier reached
            int safety = 0;
            while(masterKernel.GetTimeController().GetMode() == TimeMode.Continuous && safety++ < 100)
            {
                masterKernel.Update();
                coordinator.Update();
                
                // Swap shared bus to propagate network events
                _sharedBus.SwapBuffers();
                
                // Slave follows (assuming Pulse arrives via shared bus)
                slaveKernel.Update(); 
                listener.Update();
                
                Thread.Sleep(5); // Advance wall clock
            }
            
            // 6. Verify Mode Switched
            Assert.Equal(TimeMode.Deterministic, masterKernel.GetTimeController().GetMode());
            Assert.Equal(TimeMode.Deterministic, slaveKernel.GetTimeController().GetMode());
            
            // 7. Verify Frame Alignment
            // Using reflection or cast to check if SteppedController
            Assert.IsType<SteppedMasterController>(masterKernel.GetTimeController());
            Assert.IsType<SteppedSlaveController>(slaveKernel.GetTimeController());
            
            // 8. Verify No Rewinds (Frame should be >= start)
            Assert.True(masterKernel.CurrentTime.FrameNumber > startFrame);
            Assert.True(slaveKernel.CurrentTime.FrameNumber > startFrame);
            
            // Cleanup
            masterKernel.Dispose();
            slaveKernel.Dispose();
        }
        
        [Fact]
        public void Unpause_SwitchesImmediately()
        {
             // 1. Setup Master/Slave in Stepped Mode (Simulating already paused)
             // We can use the logic above to get there, or force it.
             // For speed, let's force swap manually first.
             
             var masterRepo = new EntityRepository();
             var masterKernel = new ModuleHostKernel(masterRepo, new EventAccumulator());
             var masterConfig = new TimeControllerConfig { Role = TimeRole.Master };
             
             // Replaced ConfigureTime
             masterKernel.SetTimeController(new MasterTimeController(masterRepo.Bus, masterConfig.SyncConfig));
             
             masterKernel.Initialize();
             
             var coordinator = new DistributedTimeCoordinator(_sharedBus, masterKernel, masterConfig, new HashSet<int>{1});
             
            // Force Master to Stepped
             masterKernel.SwapTimeController(new SteppedMasterController(_sharedBus, new HashSet<int>{1}, masterConfig));
             Assert.Equal(TimeMode.Deterministic, masterKernel.GetTimeController().GetMode());

             // Setup Slave Swapped
             var slaveRepo = new EntityRepository();
             var slaveKernel = new ModuleHostKernel(slaveRepo, new EventAccumulator());
             var slaveConfig = new TimeControllerConfig { Role = TimeRole.Slave };
             
             // Replaced ConfigureTime
             slaveKernel.SetTimeController(new SlaveTimeController(slaveRepo.Bus, slaveConfig.SyncConfig));
             
             slaveKernel.Initialize();
             
             var listener = new SlaveTimeModeListener(_sharedBus, slaveKernel, slaveConfig);
             
             // Initial Swap to Stepped Slave
             slaveKernel.SwapTimeController(new SteppedSlaveController(_sharedBus, 1, 0.016f));
             Assert.Equal(TimeMode.Deterministic, slaveKernel.GetTimeController().GetMode());

             // 2. Unpause
             coordinator.SwitchToContinuous();
             
             // Swap shared bus to deliver event
             _sharedBus.SwapBuffers();
             
             // Verify Immediately Swapped Master
             Assert.Equal(TimeMode.Continuous, masterKernel.GetTimeController().GetMode());
             
             // 3. Slave Unpause
             listener.Update(); 
             
             Assert.Equal(TimeMode.Continuous, slaveKernel.GetTimeController().GetMode());
             
             masterKernel.Dispose();
             slaveKernel.Dispose();
        }

        [Fact]
        public void PausedStepping_AdvancesFrameByFrame()
        {
            // Arrange: Kernel in Deterministic mode with NO slave nodes.
            // With zero slaves, Step() never blocks on ACKs, so consecutive StepFrame()
            // calls advance freely — modelling a single-process / offline scenario.
            var repo = new EntityRepository();
            var eventBus = new FdpEventBus(); // Local bus for testing
            var kernel = new ModuleHostKernel(repo, new EventAccumulator());

            repo.RegisterComponent<GlobalTime>();
            repo.SetSingletonUnmanaged(new GlobalTime());

            // Start directly in Deterministic Master mode for simplicity.
            // Empty slave set — no ACK waiting, pure manual stepping.
            var config = new TimeControllerConfig
            {
                Role = TimeRole.Master,
                Mode = TimeMode.Deterministic,
                AllNodeIds = new HashSet<int>() // no slaves
            };

            // Use SteppedMaster directly
            var steppedCtrl = new SteppedMasterController(repo.Bus, config.AllNodeIds, config.SyncConfig);
            kernel.SetTimeController(steppedCtrl);
            kernel.Initialize();

            // Act: Step 3 times
            // 1/60 = 0.016666...
            // kernel.StepFrame calls steppedCtrl.Step() because ISteppableTimeController is implemented
            kernel.StepFrame(1.0f / 60.0f);
            kernel.StepFrame(1.0f / 60.0f);
            kernel.StepFrame(1.0f / 60.0f);

            var time = kernel.CurrentTime;

            // Assert: Time advanced by exactly 3 * (1/60)s
            Assert.Equal(3.0 / 60.0, time.TotalTime, precision: 4);
            Assert.Equal(3, time.FrameNumber);

            kernel.Dispose();
        }

        [Fact]
        public void LateArrivingSlave_SwapsImmediately_NoRewind()
        {
            // Arrange: Master and Slave with simulated high latency
            var sharedBus = new FdpEventBus();
            
            // Master Setup
            var masterRepo = new EntityRepository();
            var masterKernel = new ModuleHostKernel(masterRepo, new EventAccumulator());
            var masterConfig = new TimeControllerConfig 
            { 
                Role = TimeRole.Master,
                SyncConfig = new TimeConfig { LookaheadWallTicks = (long)(0.01 * Stopwatch.Frequency) }
            };
            
            masterKernel.SetTimeController(new MasterTimeController(masterRepo.Bus, masterConfig.SyncConfig));
            masterKernel.Initialize();
            
            var coordinator = new DistributedTimeCoordinator(sharedBus, masterKernel, masterConfig, new HashSet<int>{1});
            
            // Slave Setup (starts delayed)
            var slaveRepo = new EntityRepository();
            var slaveKernel = new ModuleHostKernel(slaveRepo, new EventAccumulator());
            var slaveConfig = new TimeControllerConfig 
            { 
                Role = TimeRole.Slave,
                LocalNodeId = 1
            };
            
            slaveKernel.SetTimeController(new SlaveTimeController(slaveRepo.Bus, slaveConfig.SyncConfig));
            slaveKernel.Initialize();
            
            var listener = new SlaveTimeModeListener(sharedBus, slaveKernel, slaveConfig);
            
            // Advance Master ahead
            for (int i = 0; i < 10; i++)
            {
                masterKernel.Update();
                Thread.Sleep(5);
            }
            
            long masterFrameBeforePause = masterKernel.CurrentTime.FrameNumber;
            
            // Master initiates pause (Barrier = Current + 5)
            coordinator.SwitchToDeterministic(new HashSet<int>{1});
            
            // Master advances to and past barrier
            for (int i = 0; i < 10; i++)
            {
                masterKernel.Update();
                coordinator.Update();
                Thread.Sleep(5);
            }
            
            // Swap ONCE to deliver the event to "Current" buffer
            sharedBus.SwapBuffers();
            
            // Verify Master is now paused
            Assert.Equal(TimeMode.Deterministic, masterKernel.GetTimeController().GetMode());
            long masterFrameAfterPause = masterKernel.CurrentTime.FrameNumber;
            
            // Slave is still running normally (simulating network delay - hasn't received event OR hasn't processed it)
            // Advance Slave past the barrier (Barrier was ~15, we go to ~20)
            for (int i = 0; i < 20; i++)
            {
                slaveKernel.Update();
                Thread.Sleep(1);
            }
            
            long slaveFrameBeforeEvent = slaveKernel.CurrentTime.FrameNumber;
            double slaveTimeBeforeEvent = slaveKernel.CurrentTime.TotalTime;
            
            // NOW: Event finally arrives (processed)
            listener.Update();
            
            // Act & Assert
            // Slave should immediately swap (past barrier) using LOCAL state
            Assert.Equal(TimeMode.Deterministic, slaveKernel.GetTimeController().GetMode());
            
            // Verify NO REWIND: Frame should be >= before event
            Assert.True(slaveKernel.CurrentTime.FrameNumber >= slaveFrameBeforeEvent,
                $"Slave rewound! Was {slaveFrameBeforeEvent}, now {slaveKernel.CurrentTime.FrameNumber}");
            
            // Verify Time continuity
            Assert.True(slaveKernel.CurrentTime.TotalTime >= slaveTimeBeforeEvent,
                $"Slave time jumped backwards! Was {slaveTimeBeforeEvent}, now {slaveKernel.CurrentTime.TotalTime}");
            
            // Cleanup
            masterKernel.Dispose();
            slaveKernel.Dispose();
        }

        [Fact]
        public void SwapController_PreservesExactFrameAndTime()
        {
            // Arrange
            var eventBus = new FdpEventBus();
            var repo = new EntityRepository();
            var kernel = new ModuleHostKernel(repo, new EventAccumulator());
            
            var masterConfig = new TimeControllerConfig { Role = TimeRole.Master };
            kernel.SetTimeController(new MasterTimeController(repo.Bus, masterConfig.SyncConfig));
            kernel.Initialize();
            
            // Advance to known state
            for (int i = 0; i < 50; i++)
            {
                kernel.Update();
                Thread.Sleep(10); // ~500ms total
            }
            
            long frameBeforeSwap = kernel.CurrentTime.FrameNumber;
            double timeBeforeSwap = kernel.CurrentTime.TotalTime;
            
            Assert.True(frameBeforeSwap > 0, "Should have advanced some frames");
            Assert.True(timeBeforeSwap > 0, "Should have accumulated time");
            
            // Act: Swap to Deterministic
            var steppedMaster = new SteppedMasterController(
                eventBus,
                new HashSet<int> { 1 },
                new TimeControllerConfig { Role = TimeRole.Master }
            );
            
            kernel.SwapTimeController(steppedMaster);
            
            // Assert: State preserved EXACTLY
            Assert.Equal(frameBeforeSwap, kernel.CurrentTime.FrameNumber);
            Assert.Equal(timeBeforeSwap, kernel.CurrentTime.TotalTime, precision: 6);
            
            // Verify new controller also reports same state
            var newState = kernel.GetTimeController().GetCurrentState();
            Assert.Equal(frameBeforeSwap, newState.FrameNumber);
            Assert.Equal(timeBeforeSwap, newState.TotalTime, precision: 6);
            
            // Cleanup
            kernel.Dispose();
        }

        [Fact]
        public void RapidPauseUnpause_BeforeBarrier_HandlesSafely()
        {
            // Arrange
            var sharedBus = new FdpEventBus();
            var masterRepo = new EntityRepository();
            var masterKernel = new ModuleHostKernel(masterRepo, new EventAccumulator());

            // Use a short lookahead (50 ms) so the test completes quickly while still
            // being long enough that 3 short updates don't accidentally cross it.
            var masterConfig = new TimeControllerConfig
            {
                Role = TimeRole.Master,
                SyncConfig = new TimeConfig { LookaheadWallTicks = (long)(0.05 * Stopwatch.Frequency) }
            };

            masterKernel.SetTimeController(new MasterTimeController(masterRepo.Bus, masterConfig.SyncConfig));
            masterKernel.Initialize();

            var coordinator = new DistributedTimeCoordinator(sharedBus, masterKernel, masterConfig, new HashSet<int> { 1 });

            // Start in Continuous
            masterKernel.Update();
            Assert.Equal(TimeMode.Continuous, masterKernel.GetTimeController().GetMode());

            long frameAtPause = masterKernel.CurrentTime.FrameNumber;

            // Act: Rapid Pause
            coordinator.SwitchToDeterministic(new HashSet<int> { 1 });

            // Advance ~3 updates (< 50 ms lookahead — barrier not reached)
            for (int i = 0; i < 3; i++)
            {
                masterKernel.Update();
                coordinator.Update();
                Thread.Sleep(1); // only ~3 ms total, well below 50 ms barrier
            }

            // Should still be Continuous (barrier not reached)
            Assert.Equal(TimeMode.Continuous, masterKernel.GetTimeController().GetMode());

            // Act: Rapid Unpause (cancel pending pause)
            coordinator.SwitchToContinuous();

            // Advance more frames (well past original barrier time but it was canceled)
            for (int i = 0; i < 15; i++)
            {
                masterKernel.Update();
                coordinator.Update();
                Thread.Sleep(5);
            }

            // Assert: Should remain Continuous (barrier canceled)
            Assert.Equal(TimeMode.Continuous, masterKernel.GetTimeController().GetMode());

            // Verify frame count advanced normally
            Assert.True(masterKernel.CurrentTime.FrameNumber > frameAtPause + 15,
                "Should have advanced past original barrier without pausing");

            // Cleanup
            masterKernel.Dispose();
        }

        [Fact]
        public void StepFrame_OnContinuousController_ThrowsInvalidOperation()
        {
            // Arrange
            var repo = new EntityRepository();
            var kernel = new ModuleHostKernel(repo, new EventAccumulator());
            
            kernel.SetTimeController(new MasterTimeController(repo.Bus));
            kernel.Initialize();
            
            // Verify we're in Continuous mode
            Assert.Equal(TimeMode.Continuous, kernel.GetTimeController().GetMode());
            
            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                kernel.StepFrame(0.016f);
            });
            
            // Verify error message is clear
            Assert.Contains("does not support", ex.Message);
            Assert.Contains("stepping", ex.Message.ToLower());
            
            // Cleanup
            kernel.Dispose();
        }

        [Fact]
        public void SwitchEvent_PropagatesBarrierFrameCorrectly()
        {
            // Arrange
            var sharedBus = new FdpEventBus();
            
            var masterRepo = new EntityRepository();
            var masterKernel = new ModuleHostKernel(masterRepo, new EventAccumulator());
            
            int lookahead = 7; // Custom lookahead for test (ms)
            var masterConfig = new TimeControllerConfig 
            { 
                Role = TimeRole.Master,
                SyncConfig = new TimeConfig { LookaheadWallTicks = (long)(lookahead * Stopwatch.Frequency / 1000L) }
            };
            
            masterKernel.SetTimeController(new MasterTimeController(masterRepo.Bus, masterConfig.SyncConfig));
            masterKernel.Initialize();
            
            var coordinator = new DistributedTimeCoordinator(sharedBus, masterKernel, masterConfig, new HashSet<int>{1});
            
            // Advance to known frame
            for (int i = 0; i < 10; i++)
            {
                masterKernel.Update();
                Thread.Sleep(5);
            }
            
            long masterStateAtPause = masterKernel.CurrentTime.TotalWallTicks;
            long expectedBarrierMin = masterStateAtPause; // barrier must be strictly in the future
            
            // Act: Initiate pause
            coordinator.SwitchToDeterministic(new HashSet<int>{1});
            
            // Swap to deliver event
            sharedBus.SwapBuffers();
            
            // Capture event by consuming
            SwitchTimeModeEvent? capturedEvent = null;
            foreach(var evt in sharedBus.Consume<SwitchTimeModeEvent>())
            {
                capturedEvent = evt;
                break;
            }
            
            // Assert: event captured with a wall-tick barrier strictly in the future
            Assert.NotNull(capturedEvent);
            Assert.Equal(TimeMode.Deterministic, capturedEvent.Value.TargetMode);
            Assert.True(capturedEvent.Value.BarrierWallTicks > masterStateAtPause,
                $"BarrierWallTicks ({capturedEvent.Value.BarrierWallTicks}) must be > TotalWallTicks at pause time ({masterStateAtPause})");
            
            // Cleanup
            masterKernel.Dispose();
        }
    }
}
