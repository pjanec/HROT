using System.Collections.Generic;
using Fdp.Kernel;
using Xunit;

using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Messages;

namespace FDP.Toolkit.Time.Tests
{
    /// <summary>
    /// CGF1-S0203 success conditions for <see cref="SwitchableTimeController"/>.
    /// </summary>
    public class SwitchableTimeControllerTests
    {
        /// <summary>
        /// CGF1-S0203: When <see cref="SwitchableTimeController.SwitchTo"/> is called with
        /// a new controller, the new controller is seeded with the current state of the
        /// outgoing controller so that simulation time is continuous across the swap.
        /// </summary>
        [Fact]
        public void SwitchTo_TransfersCurrentStateToNewController()
        {
            // Arrange: master seeded to TotalTime = 5.0
            var masterBus = new FdpEventBus();
            var master = new MasterTimeController(masterBus);
            master.SeedState(new GlobalTime { TotalTime = 5.0, FrameNumber = 50, TimeScale = 1.0f });

            var switchable = new SwitchableTimeController(master);

            // Target: SteppedMasterController with no slaves (pure time bookkeeping)
            var steppedBus = new FdpEventBus();
            var stepped = new SteppedMasterController(steppedBus, new HashSet<int>(), TimeConfig.Default);

            // Act
            switchable.SwitchTo(stepped);

            // Assert: new controller was seeded with the master's state
            var state = stepped.GetCurrentState();
            Assert.Equal(5.0, state.TotalTime, precision: 3);
        }

        /// <summary>
        /// CGF1-S0203: Calling <see cref="SwitchableTimeController.SwitchTo"/> with the
        /// currently active instance must be a no-op — no state mutation, same reference.
        /// </summary>
        [Fact]
        public void SwitchTo_SameInstance_IsNoOp()
        {
            var bus = new FdpEventBus();
            var master = new MasterTimeController(bus);
            master.SeedState(new GlobalTime { TotalTime = 3.0, TimeScale = 1.0f });

            var switchable = new SwitchableTimeController(master);

            // Act: call SwitchTo with the already-active instance
            switchable.SwitchTo(master);

            // Assert: active controller is unchanged and state was not mutated by an erroneous SeedState call
            Assert.Same(master, switchable.ActiveController);
            Assert.Equal(3.0, switchable.GetCurrentState().TotalTime, precision: 3);
        }
    }
}
