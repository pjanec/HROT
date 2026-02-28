using System.Runtime.CompilerServices;
using Bagira.SimHost.Brains;
using FDP.Toolkit.Behavior;
using Xunit;

namespace Bagira.SimHost.Tests
{
    public class SimHostNodesParseParamsTests
    {
        [Fact]
        public unsafe void SimHostNodes_ParseParams_WritesCorrectBytesToBlackboard()
        {
            var def = new DoctrineDefinition
            {
                Name = "MoveToLocation",
                BrainTier = BehaviorConstants.BrainTierBTree,
                ParseParams = SimHostNodes.ParseMoveToParams
            };

            const string json = "{\"x\":12.5,\"y\":-4.25,\"speed\":7.75,\"arrivalRadius\":2.5}";

            var buffer = stackalloc byte[BehaviorConstants.BrainBlackboardByteSize];
            def.ParseParams?.Invoke(json, buffer);

            var parsed = Unsafe.Read<SimHostNodes.MoveToLocationParams>(buffer);
            Assert.Equal(12.5f, parsed.X, 3);
            Assert.Equal(-4.25f, parsed.Y, 3);
            Assert.Equal(7.75f, parsed.Speed, 3);
            Assert.Equal(2.5f, parsed.ArrivalRadius, 3);
        }
    }
}
