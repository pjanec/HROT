using Fdp.Toolkit.Orchestration;
using Hrot.Common.Infrastructure;
using Xunit;

namespace Hrot.Core.Tests.Infrastructure;

/// <summary>⭐ CE-4013 D13 — the one node config every networked host starts from: it stages under <c>&lt;staging&gt;/nodes/node-&lt;id&gt;</c>, where the
/// orchestrator writes each node's scenarios and TKB (docs/DESIGN_Artifact_Staging.md V1); IG and the Stride node took the bare root.</summary>
public sealed class HrotNodeConfigForNodeTests
{
    [Fact]
    public void ForNode_StagesPerNode_LogsBesideTheBinaries_AndCarriesTheIdentity()
    {
        var c = HrotNodeConfig.ForNode(61, 400, "CGF", participant: null);

        Assert.Equal(OrchestrationConstants.GetNodeStagingRoot(400), c.LocalTempRoot);
        Assert.Equal(System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs"), c.LogDirectory);
        Assert.Equal((61, 400, "CGF", false), (c.DomainId, c.NodeId, c.SubsystemName, c.Headless));
    }

    [Fact]
    public void ForNode_AGivenStagingRoot_IsTheBaseOfThePerNodeRoot()
    {
        var c = HrotNodeConfig.ForNode(0, 7, "SimHost", participant: null, stagingRoot: "/tmp/x");

        Assert.Equal(OrchestrationConstants.GetNodeStagingRoot("/tmp/x", 7), c.LocalTempRoot);
    }
}
