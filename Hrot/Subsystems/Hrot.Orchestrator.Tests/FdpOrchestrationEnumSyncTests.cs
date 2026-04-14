using System;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// CMC-S001: Verifies that the FDP domain enums in <c>FDP.Toolkit.Orchestration</c>
/// have identical integer values to their <c>Hrot.NED.Descriptors.Orchestration</c> counterparts.
/// </summary>
public sealed class FdpOrchestrationEnumSyncTests
{
    [Fact]
    public void ClusterStateValuesMatchHrot()
    {
        foreach (FDP.Toolkit.Orchestration.ClusterState fdpVal
                 in Enum.GetValues<FDP.Toolkit.Orchestration.ClusterState>())
        {
            var nedVal = (Hrot.NED.Descriptors.Orchestration.ClusterState)(int)fdpVal;
            Assert.Equal(fdpVal.ToString(), nedVal.ToString());
        }
    }

    [Fact]
    public void ClusterOpTypeValuesMatchHrot()
    {
        foreach (FDP.Toolkit.Orchestration.ClusterOpType fdpVal
                 in Enum.GetValues<FDP.Toolkit.Orchestration.ClusterOpType>())
        {
            var nedVal = (Hrot.NED.Descriptors.Orchestration.ClusterOpType)(int)fdpVal;
            Assert.Equal(fdpVal.ToString(), nedVal.ToString());
        }
    }

    [Fact]
    public void NodeOpTypeValuesMatchHrot()
    {
        foreach (FDP.Toolkit.Orchestration.NodeOpType fdpVal
                 in Enum.GetValues<FDP.Toolkit.Orchestration.NodeOpType>())
        {
            var nedVal = (Hrot.NED.Descriptors.Orchestration.NodeOpType)(int)fdpVal;
            Assert.Equal(fdpVal.ToString(), nedVal.ToString());
        }
    }
}
