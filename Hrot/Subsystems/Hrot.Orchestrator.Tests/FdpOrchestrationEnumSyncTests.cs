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
        foreach (Fdp.Toolkit.Orchestration.ClusterState fdpVal
                 in Enum.GetValues<Fdp.Toolkit.Orchestration.ClusterState>())
        {
            var nedVal = (Hrot.NED.Descriptors.Orchestration.ClusterState)(int)fdpVal;
            Assert.Equal(fdpVal.ToString(), nedVal.ToString());
        }
    }

    [Fact]
    public void ClusterOpTypeValuesMatchHrot()
    {
        foreach (Fdp.Toolkit.Orchestration.ClusterOpType fdpVal
                 in Enum.GetValues<Fdp.Toolkit.Orchestration.ClusterOpType>())
        {
            var nedVal = (Hrot.NED.Descriptors.Orchestration.ClusterOpType)(int)fdpVal;
            Assert.Equal(fdpVal.ToString(), nedVal.ToString());
        }
    }

    [Fact]
    public void NodeOpTypeValuesMatchHrot()
    {
        foreach (Fdp.Toolkit.Orchestration.NodeOpType fdpVal
                 in Enum.GetValues<Fdp.Toolkit.Orchestration.NodeOpType>())
        {
            var nedVal = (Hrot.NED.Descriptors.Orchestration.NodeOpType)(int)fdpVal;
            Assert.Equal(fdpVal.ToString(), nedVal.ToString());
        }
    }
}
