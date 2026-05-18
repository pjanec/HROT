using System;
using Xunit;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Kernel
{
    public unsafe class CommandLaneTests
    {
        [Fact]
        public void SetLane_Changes_CurrentLane()
        {
            var page = new CommandPage();
            var writer = new HsmCommandWriter(&page, 4080, CommandLane.Animation);
            
            Assert.Equal(CommandLane.Animation, writer.CurrentLane);
            
            writer.SetLane(CommandLane.Navigation);
            Assert.Equal(CommandLane.Navigation, writer.CurrentLane);
        }

        [Fact]
        public void CommandWriter_Default_Lane_Is_Gameplay()
        {
            var page = new CommandPage();
            var writer = new HsmCommandWriter(&page);
            
            Assert.Equal(CommandLane.Gameplay, writer.CurrentLane);
        }
    }
}
