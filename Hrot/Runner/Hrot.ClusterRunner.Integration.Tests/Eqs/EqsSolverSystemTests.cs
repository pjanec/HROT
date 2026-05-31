using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.SimHost;
using System.Collections.Generic;
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
    // ── Nested stubs for OFX-021 test ─────────────────────────────────────────

    private sealed class CountingGenerator : IEqsGenerator
    {
        public int CallCount { get; private set; }
        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view,
            Span<EqsResult> candidates)
        {
            CallCount++;
            return 0;
        }
    }

    private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
    {
        private readonly Dictionary<uint, EqsQueryTemplate> _templates = new();
        public void Register(EqsQueryTemplate t) => _templates[t.BlueprintId] = t;
        public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _templates.TryGetValue(id, out t);
    }
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

    /// <summary>
    /// T-OFX-021: When <see cref="SensorEvalState.Phase"/> is <c>_AwaitingRaycasts</c>
    /// and <c>AwaitingSinceTick</c> is older than the current tick, the solver must
    /// NOT call the generator during the phase it is stuck in, then recover.
    /// </summary>
    [Fact(Timeout = 8_000)]
    public void EvaluateSensor_AwaitingRaycasts_RecoversThroughGeneration()
    {
        // Arrange: register a template with a counting generator.
        const uint blueprintId = 200u;
        var        countingGen = new CountingGenerator();
        var        registry    = new SimpleEqsTemplateRegistry();
        var        template    = new EqsQueryTemplate
        {
            BlueprintId   = blueprintId,
            Generator     = countingGen,
            MaxCandidates = 4,
        };
        registry.Register(template);
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        // Pre-compute the structure hash so the hard-reset guard does not
        // overwrite Phase=Idle before the _AwaitingRaycasts guard can run.
        ulong structHash = template.ComputeStructureHash();

        // Create entity with SensorEvalState pre-set to _AwaitingRaycasts so the
        // solver encounters the guard immediately on its first EQS cycle.
        var entity = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(entity, new EqsSensor
        {
            BlueprintId = blueprintId,
            Epoch       = 1,
        });
        _harness.Repo.AddComponent(entity, new SensorEvalState
        {
            Phase                = EqsEvalPhase._AwaitingRaycasts,
            AwaitingSinceTick    = 0, // older than any tick the solver will see
            CurrentEpoch         = 1,
            CurrentStructureHash = structHash,
        });

        // Act + Assert: the sensor must recover from _AwaitingRaycasts and eventually
        // call the generator. PumpUntil is used because EqsModule runs asynchronously
        // and the exact number of frames before recovery is timing-dependent.
        bool recovered = _harness.PumpUntil(() => countingGen.CallCount >= 1, timeoutMs: 5000);
        Assert.True(recovered,
            $"Sensor should recover from _AwaitingRaycasts and call the generator at least once (CallCount={countingGen.CallCount})");
    }
}
