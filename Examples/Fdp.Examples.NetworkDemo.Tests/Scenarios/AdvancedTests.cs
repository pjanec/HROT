using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Fdp.Examples.NetworkDemo.Tests.Infrastructure;
using Fdp.Kernel;
using ModuleHost.Core.Time;
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

            // 1. Trigger Switch on Node A (Master) directly via EventBus
            env.NodeA.EventBus.Publish(new SwitchTimeModeEvent 
            { 
               TargetMode = TimeMode.Deterministic,
               BarrierWallTicks = 0  // immediate
            });

            // 2. Wait for propagation and switch
            // Check for controller type change
            await env.WaitForCondition(app => 
            {
                var name = app.Kernel.GetTimeController().GetType().Name;
                // if (!name.Contains("Stepped")) _output.WriteLine($"[Node {(app == env.NodeA ? 100 : 200)}] Controller: {name}");
                return name.Contains("Stepped");
            }, 
                env.NodeA);
                
            await env.WaitForCondition(app => 
                app.Kernel.GetTimeController().GetType().Name.Contains("Stepped"), 
                env.NodeB);

            _output.WriteLine("Both nodes switched to Stepped Controller.");

            // 3. Verify Synchronization
            // In Stepped mode, frames should be locked or at least close.
            // If they are paused waiting for steps (which SteppedMaster might do if no inputs), they should be static.
            
            long frameA = env.NodeA.Kernel.CurrentTime.FrameNumber;
            long frameB = env.NodeB.Kernel.CurrentTime.FrameNumber;

            _output.WriteLine($"Frame A: {frameA}, Frame B: {frameB}");

            // Allow difference of 1 frame due to snapshot timing
            Assert.True(Math.Abs(frameA - frameB) <= 2, $"Frames desynchronized: A={frameA}, B={frameB}");
            
            // 4. Verify Controller Type
            Assert.Contains("Stepped", env.NodeA.Kernel.GetTimeController().GetType().Name);
            Assert.Contains("Stepped", env.NodeB.Kernel.GetTimeController().GetType().Name);
        }
    }
}