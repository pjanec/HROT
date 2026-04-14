using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Fdp.Examples.NetworkDemo.Tests.Infrastructure;
using Fdp.Kernel;
using Fdp.ModuleHost.Core.Time;
using System.Linq;
using FDP.Toolkit.Time.Messages;
using System;

namespace Fdp.Examples.NetworkDemo.Tests.Scenarios
{
    public class AdvancedTests
    {
        private readonly ITestOutputHelper _output;

        public AdvancedTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Deterministic_Time_Switch_Synchronizes_Nodes()
        {
            using var env = new DistributedTestEnv(_output);
            await env.StartNodesAsync();

            // Reduced delay for faster test execution
            await Task.Delay(100);

            _output.WriteLine("Triggering Mode Switch...");

            // Publish a SwitchTimeModeEvent to NodeA's EventBus.
            // TimeSyncSystem.ExecuteMaster will relay it into the shared TimeModeComponent,
            // which is replicated to NodeB. BarrierWallTicks = 1 (past) → slave crosses immediately.
            env.NodeA.EventBus.Publish(new SwitchTimeModeEvent 
            { 
               TargetMode       = TimeMode.Deterministic,
               BarrierWallTicks = 1L  // past barrier → slave enters Stepping the same frame it's seen
            });

            // Wait for NodeB (slave) to reach Deterministic mode.
            await env.WaitForCondition(app => 
                app.Kernel.GetTimeController().GetMode() == TimeMode.Deterministic, 
                env.NodeB);

            _output.WriteLine("NodeB switched to Deterministic mode.");

            // Verify Controller Mode
            Assert.Equal(TimeMode.Deterministic, env.NodeB.Kernel.GetTimeController().GetMode());
        }
    }
}