using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Examples.Scenarios.Integrated;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Time.Controllers;
using Hrot.Orchestrator;
using Hrot.SimHost;
using Hrot.ExCon;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.ModuleHost;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// PACK3-U004 â€” Urban Combat File Lifecycle Integration Test.
///
/// <para>Proves that a scenario extracted from <see cref="UrbanCombatNewScenario"/> and
/// serialised to a local JSON file can be loaded by the full cluster state machine
/// (Orchestrator + SimHost) and executed to completion as validated by
/// <see cref="UrbanCombatValidator"/>.</para>
///
/// <para>In the <c>HeavyE2ETests</c> collection so it runs sequentially with other DDS-
/// heavy tests to avoid CPU starvation.</para>
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class UrbanCombatFileLifecycleTests : IDisposable
{
    // Domain IDs in the valid CycloneDDS range (0â€“232).
    // Use a high value to avoid collisions with AllSubsystems (160), ClusterOpE2e (170),
    // DistributedBrainMuscle (220â€“221), and CgfHarness auto-counter (200â€“).
    // U004 uses only a single domain slot so a fixed high value is safe.
    private const int DomainBase = 228;
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly string _scenarioId;
    private readonly string _stagingDir;

    public UrbanCombatFileLifecycleTests()
    {
        _scenarioId = Guid.NewGuid().ToString();
        _stagingDir = Path.Combine(@"C:\FDP_Temp", _scenarioId);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Clean up staging directory even if the test fails.
        if (Directory.Exists(_stagingDir))
        {
            try { Directory.Delete(_stagingDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Extracts the UrbanCombat scenario to a JSON file, boots the cluster, loads it
    /// via a <c>TransitionState â†’ OperatingLive</c> request, and validates that all
    /// four ambush latches fire within 600 ticks.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task UrbanCombatExtractedToJson_ExecutesSuccessfullyInLiveMode()
    {
        // â”€â”€ 1. Extract scenario to a local JSON file â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        ExtractScenarioToFile();
        var scenarioFilePath = System.IO.Path.Combine(_stagingDir, "scenario.json");
        Assert.True(System.IO.File.Exists(scenarioFilePath),
            $"Scenario file must exist before cluster boot: {scenarioFilePath}");

        // â”€â”€ 2. Boot cluster â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        int domainId = NextDomainId();
        using var harness = new HrotRunnerHarness("simhost", domainId);
        using var cgf     = new CgfHarness(domainId);

        // Extra warmup to allow DDS discovery for all topics.
        harness.PumpFrames(20);
        cgf.PumpFrames(20);

        // â”€â”€ 3. Transition cluster to OperatingLive â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Wait for at least one node in the roster before issuing the request
        // (mirrors the pattern in AllSubsystemsClusterTransitionTests).
        var clusterMaster = harness.OrchestratorSvc.TestHook_ClusterMaster!;

        var rosterDeadline = DateTime.UtcNow.AddSeconds(5.0);
        while (clusterMaster.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline)
        {
            harness.PumpFrames(1);
            cgf.PumpFrames(1);
            Thread.Sleep(10);
        }

        Assert.True(clusterMaster.NodeRoster.ActiveNodes.Count > 0,
            "At least one node should appear in the cluster roster within 5 s.");

        // Register UC behaviors in both the SimHost's and the CGF's BehaviorRegistry.
        // SimHost runs kinematic/combat systems (Muscle tier); CGF runs mission-control
        // and behavior execution (Brain tier). Both must know the UC behavior definitions
        // before the cluster transitions to OperatingLive and scenario missions start.
        UrbanCombatNewScenario.RegisterUrbanCombatBehaviors(
            harness.SimHost.TestHook_BehaviorRegistry);
        UrbanCombatNewScenario.RegisterUrbanCombatBehaviors(
            cgf.CgfSvc.TestHook_BehaviorRegistry!);

        var payloadJson = JsonSerializer.Serialize(new { TargetState = 31, ScenarioId = _scenarioId });
        await clusterMaster.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = payloadJson,
        }).ConfigureAwait(false);

        // Pump until the cluster master reaches OperatingLive (31).
        bool reachedLive = PumpBothUntil(
            harness, cgf,
            () => (int)clusterMaster.CurrentClusterState == 31,
            timeoutMs: 15_000);

        Assert.True(reachedLive,
            $"Cluster should reach OperatingLive (31) within 15 s. " +
            $"Current state: {(int)clusterMaster.CurrentClusterState}.");

        // â”€â”€ 4. Validate UC narrative latches â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var validator = new UrbanCombatValidator();
        bool success  = false;
        int  finalEntityCount = 0;
        for (uint tick = 0; tick < 800 && !success; tick++)
        {
            harness.PumpFrames(1);
            cgf.PumpFrames(1);

            var world = harness.SimHost.World;
            if (world != null)
            {
                finalEntityCount = world.EntityCount;
                success = validator.EvaluateTick(tick, world);
            }
        }

        Assert.True(success,
            $"All 4 ambush latches should fire within 800 frames. " +
            $"Latches: ambush={validator.LatchAmbushFired}, apcHalt={validator.LatchApcHalted}, " +
            $"hit={validator.LatchInsurgentHit}, killed={validator.LatchInsurgentKilled}. " +
            $"EntityCount in world after 800 ticks: {finalEntityCount}.");
    }

    // â”€â”€ Private helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Creates <see cref="UrbanCombatNewScenario"/>, configures it into a temporary
    /// <see cref="EntityRepository"/>, serialises the ECS state to
    /// <c>C:\FDP_Temp\{_scenarioId}\scenario.json</c> using
    /// <see cref="HrotSerializerOptions.HrotJsonOptions"/>, then disposes the
    /// temporary world.
    /// </summary>
    private void ExtractScenarioToFile()
    {
        var extractRepo = new EntityRepository();
        var accumulator = new EventAccumulator();
        var kernel      = new ModuleHostKernel(extractRepo, accumulator);
        kernel.SetTimeController(new SteppingTimeController(new GlobalTime { DeltaTime = 1f / 60f, TimeScale = 1.0f }));

        var scenario = new UrbanCombatNewScenario();
        scenario.Configure(extractRepo, kernel);
        kernel.Initialize();

        // Build the serializer AFTER Configure so all UC component types are registered
        // in the global ComponentTypeRegistry and included in the compiled delegates.
        // TargetMemoryTranslator and PassengerBufferTranslator are registered here to
        // match the production SimHost serializer, ensuring entity cross-references in
        // TargetMemory (insurgent/APC targets) and PassengerBuffer (embarked soldiers)
        // survive the JSON round-trip as GUID-tracked handles.
        var behaviorRegistry = new Fdp.Toolkit.Behavior.BehaviorRegistry();
        var serializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(behaviorRegistry);
        var fdpDom     = serializer.Serialize(extractRepo, new ScenarioHeader("Hrot.Scenario"));

        // Wrap in the application-layer DTO.  SubsystemType must match "Hrot.Scenario" so
        // that HrotScenarioLoader (which uses the SimHost serializer's SubsystemType) can
        // select this file during the cluster load.
        var envelope = new HrotScenarioEnvelopeDto
        {
            Header = new ScenarioHeaderDto
            {
                SubsystemType = "Hrot.Scenario",
            },
            Zones    = null,
            Entities = fdpDom["Entities"]?.AsObject(),
        };

        Directory.CreateDirectory(_stagingDir);
        File.WriteAllText(
            Path.Combine(_stagingDir, "scenario.json"),
            JsonSerializer.Serialize(envelope, HrotSerializerOptions.HrotJsonOptions));

        // Dispose the extraction world; scenario systems are no longer needed.
        scenario.OnShutdown();
        extractRepo.Dispose();
    }

    /// <summary>
    /// Pumps both harnesses one frame at a time until <paramref name="condition"/> is
    /// satisfied or the wall-clock deadline expires.
    /// </summary>
    private static bool PumpBothUntil(
        HrotRunnerHarness harness,
        CgfHarness        cgf,
        Func<bool>        condition,
        int               timeoutMs)
    {
        if (condition()) return true;
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            harness.PumpFrames(1);
            cgf.PumpFrames(1);
            if (condition()) return true;
            Thread.Sleep(5);
        }
        return false;
    }
}
