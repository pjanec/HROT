using System.Threading;
using Xunit;
using Fdp.Kernel;

using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Messages;

namespace FDP.Toolkit.Time.Tests
{
    public class GlobalTimeTests
    {
        [Fact]
        public void GlobalTime_IsPaused_ReturnsTrueWhenScaleIsZero()
        {
            var time = new GlobalTime { TimeScale = 0.0f };
            Assert.True(time.IsPaused);
        }
        
        [Fact]
        public void GlobalTime_IsPaused_ReturnsFalseWhenScaleIsNonZero()
        {
            var time = new GlobalTime { TimeScale = 1.0f };
            Assert.False(time.IsPaused);
        }

        /// <summary>
        /// CGF1-S0203: <see cref="GlobalTime.TotalWallTicks"/> must be &gt; 0 after a
        /// <see cref="MasterTimeController.Update"/> so that the Future Barrier can use
        /// it as the authoritative distributed wall-clock reference.
        /// </summary>
        [Fact]
        public void TotalWallTicks_IsPopulatedByMasterController()
        {
            var bus = new FdpEventBus();
            var controller = new MasterTimeController(bus, TimeConfig.Default);

            Thread.Sleep(2); // Allow the stopwatch to advance past 0
            var state = controller.Update();

            Assert.True(state.TotalWallTicks > 0,
                $"Expected TotalWallTicks > 0 after Update(); got {state.TotalWallTicks}");
        }
    }
}
