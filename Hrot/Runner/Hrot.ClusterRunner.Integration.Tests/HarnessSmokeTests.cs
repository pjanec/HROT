using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Smoke tests for PACK2-R003 harness scaffolding.
/// </summary>
public class HarnessSmokeTests
{
    [Fact]
    public void EditorHarness_Initializes_WithoutException()
    {
        using var h = new EditorHarness();
        Assert.NotNull(h.Repo);
        Assert.NotNull(h.Bus);
        Assert.NotNull(h.Kernel);
    }

    [Fact]
    public void EditorHarness_PumpFrames_WithoutException()
    {
        using var h = new EditorHarness();
        h.PumpFrames(5);
        Assert.True(true);
    }

    [Fact(Skip = "Requires CycloneDDS")]
    public void CgfHarness_TwoInstances_HaveDifferentDomainIds()
    {
        using var h1 = new CgfHarness();
        using var h2 = new CgfHarness();
        Assert.NotEqual(h1.DomainId, h2.DomainId);
    }

    [Fact(Skip = "Requires CycloneDDS")]
    public void CgfHarness_SharedDomainCtor_UsesSuppledDomainId()
    {
        using var h = new CgfHarness(domainId: 150);
        Assert.Equal(150, h.DomainId);
    }

    [Fact(Skip = "Requires CycloneDDS")]
    public void HrotRunnerHarness_SharedDomainCtor_UsesSuppledDomainId()
    {
        // Use a distinct domain (250) to avoid clashes with the auto-counter test run.
        using var h = new HrotRunnerHarness("simhost", domainId: 250);
        Assert.Equal(250, h.DomainId);
    }
}
