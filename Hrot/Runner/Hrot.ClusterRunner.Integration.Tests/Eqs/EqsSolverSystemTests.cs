using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.SimHost;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Integration tests for the <see cref="Hrot.SimHost.Systems.EqsSolverSystem"/> Phase 1 stub
/// via the offline <see cref="EditorHarness"/>.
///
/// <para>The test proves the full offline round-trip:
/// EqsSolverSystem (SlowBackground 10 Hz) emits <see cref="EqsResultEvent"/>
/// -> cmd buffer merge -> <see cref="Hrot.SimHost.Systems.EqsResultUpdateSystem"/>
/// writes <see cref="EqsCognitiveBuffer.IsReady"/>=true on the Brain entity.</para>
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsSolverSystemTests : IDisposable
{
    private readonly EditorHarness _harness;

    public EqsSolverSystemTests()
    {
        _harness = new EditorHarness();
    }

    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// T4: Creates an entity with <see cref="EqsSensor"/> and <see cref="NetworkIdentity"/>,
    /// then pumps the harness until <see cref="EqsCognitiveBuffer.IsReady"/> is true.
    /// Asserts the buffer is ready and Count == 0 (Phase 1 stub emits no candidates).
    /// </summary>
    [Fact(Timeout = 8_000)]
    public void EqsSolverSystem_Phase1Stub_PopulatesBufferAfterSolverFires()
    {
        // Arrange: create entity with required components
        var entity = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(entity, new EqsSensor { BlueprintId = 1, Epoch = 1 });
        _harness.Repo.AddComponent(entity, new NetworkIdentity { Value = 9001L });

        // Act: pump until IsReady or timeout (5 s >> 100 ms solver period)
        bool ready = _harness.PumpUntil(
            () => _harness.Repo.HasComponent<EqsCognitiveBuffer>(entity) &&
                  _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(entity).IsReady,
            timeoutMs: 5000);

        // Assert
        Assert.True(ready, "EqsCognitiveBuffer should become ready within 2 s");
        ref readonly var buffer = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(entity);
        Assert.True(buffer.IsReady);
        Assert.Equal(0, buffer.Count);
    }
}
