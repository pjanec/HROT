using System.Threading.Tasks;
using Xunit;

namespace Bagira.Runner.Integration.Tests;

public class BagiraRunnerHarnessTests
{
    [Fact]
    public void Constructor_InitializesWithoutException()
    {
        using var harness = new BagiraRunnerHarness();

        Assert.NotNull(harness.Orchestrator);
        Assert.True(harness.DomainId >= 100);
    }

    [Fact]
    public void PumpUntil_ConditionMet_ReturnsTrue()
    {
        using var harness = new BagiraRunnerHarness();

        bool result = harness.PumpUntil(() => true, timeoutFrames: 5);

        Assert.True(result);
    }

    [Fact]
    public void PumpUntil_ConditionNeverMet_ReturnsFalse()
    {
        using var harness = new BagiraRunnerHarness();

        bool result = harness.PumpUntil(() => false, timeoutFrames: 5);

        Assert.False(result);
    }
}
