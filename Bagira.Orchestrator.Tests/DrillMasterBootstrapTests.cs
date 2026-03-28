using Bagira.BDC.SSTD.Orchestration;
using CycloneDDS.Runtime;

namespace Bagira.Orchestrator.Tests;

// Domain 15 is reserved for orchestrator unit tests to avoid interfering with
// domain-0 tests in Bagira.SimHost.Integration.Tests and other assemblies.
[CollectionDefinition("OrchestratorTests", DisableParallelization = true)]
public class OrchestratorTestCollection { }

[Collection("OrchestratorTests")]
public sealed class DrillMasterBootstrapTests
{
    private const int TestDomain = 15;

    [Fact]
    public void OrchestratorPublishesStandbyOnStartup()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var reader = new DdsReader<SystemStateTopic>(participant);
        var received = new List<SystemStateTopic>();
        var deadline = DateTime.UtcNow.AddSeconds(3);

        using (var drill = new DrillMaster(participant))
        {
            while (DateTime.UtcNow < deadline)
            {
                drill.Tick();
                using (var scope = reader.Take())
                {
                    foreach (var sample in scope)
                    {
                        if (!sample.IsValid) continue;
                        received.Add(sample.Data);
                    }
                }

                if (received.Count >= 1) break;
                Thread.Sleep(20);
            }
        }

        Assert.True(received.Count > 0, "No SystemStateTopic sample within 3 s.");
        Assert.Equal(1, received.Count);
        Assert.Equal(DSMState.Standby, received[0].CurrentState);
        Assert.Equal(0, received[0].TransactionEpoch);
    }
}

