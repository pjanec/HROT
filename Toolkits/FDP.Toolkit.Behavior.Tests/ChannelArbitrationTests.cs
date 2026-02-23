using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Systems;
using Fbt;
using Xunit;

namespace FDP.Toolkit.Behavior.Tests
{
    public class ChannelArbitrationTests
    {
        [Fact]
        public void Arbitration_ClearsStaleChannel()
        {
            var world = TestWorldFactory.Create();
            
            var sys = new ChannelArbitrationSystem();
            sys.Create(world);
            
            var e = world.CreateEntity();
            // Doctrine at version 2 (preempted version 1)
            world.AddComponent(e, new DoctrineState { InstanceId = 2 });
            // Channel still has action from version 1
            world.AddComponent(e, new LocomotionChannel { 
                ActiveAction = 1, 
                DoctrineInstanceId = 1,
                Status = NodeStatus.Running
            });
            
            sys.Run();
            
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(0, channel.ActiveAction);
            Assert.Equal(NodeStatus.Failure, channel.Status); // default is 0 (Failure)
            
            sys.Dispose();
            world.Dispose();
        }

        [Fact]
        public void Arbitration_IgnoresValidChannel()
        {
            var world = TestWorldFactory.Create();
            
            var sys = new ChannelArbitrationSystem();
            sys.Create(world);
            
            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { InstanceId = 2 });
            world.AddComponent(e, new LocomotionChannel { 
                ActiveAction = 1, 
                DoctrineInstanceId = 2, // Matches
                Status = NodeStatus.Running
            });
            
            sys.Run();
            
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(1, channel.ActiveAction);
            Assert.Equal(NodeStatus.Running, channel.Status);
            
            sys.Dispose();
            world.Dispose();
        }

        [Fact]
        public void Arbitration_IgnoresEmptyChannel()
        {
            var world = TestWorldFactory.Create();
            
            var sys = new ChannelArbitrationSystem();
            sys.Create(world);
            
            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState { InstanceId = 2 });
            world.AddComponent(e, new LocomotionChannel { 
                ActiveAction = 0, // None
                DoctrineInstanceId = 1, // Stale ID
                Status = NodeStatus.Success // Old status
            });
            
            sys.Run();
            
            var channel = world.GetComponent<LocomotionChannel>(e);
            Assert.Equal(0, channel.ActiveAction);
            Assert.Equal(NodeStatus.Success, channel.Status); // Should not be cleared
            // Wait, if it's stale (ID mismatch) but Action is 0. 
            // My implementation: if (channel.ActiveAction != 0 && ...)
            // So it skips clearing. Status remains Success.
            
            sys.Dispose();
            world.Dispose();
        }
    }
}
