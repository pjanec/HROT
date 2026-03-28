using Bagira.BDC.SSTD.Orchestration;
using CycloneDDS.Runtime;

namespace Bagira.Orchestrator.Tests;

public sealed class DrillMasterBootstrapTests
{
    [Fact]
    public void OrchestratorPublishesStandbyOnStartup()
    {
        using var participant = new DdsParticipant(0);
        using var reader = new DdsReader<SystemStateTopic>(participant);
        SystemStateTopic? received = null;
        var deadline = DateTime.UtcNow.AddSeconds(3);

        using (var drill = new DrillMaster(participant))
        {
            while (DateTime.UtcNow < deadline && received == null)
            {
                drill.Tick();
                using (var scope = reader.Take())
                {
                    foreach (var sample in scope)
                    {
                        if (!sample.IsValid) continue;
                        received = sample.Data;
                        break;
                    }
                }

                if (received == null)
                    Thread.Sleep(20);
            }
        }

        Assert.True(received.HasValue, "No SystemStateTopic sample within 3 s.");
        Assert.Equal(DSMState.Standby, received!.Value.CurrentState);
        Assert.Equal(0, received.Value.TransactionEpoch);
    }
}
