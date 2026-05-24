using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Integration tests for the EQS translator pipeline (TASK-EQS-007).
///
/// <para>Domain range: 71-79 (free gap between SensorMechanism 60-69 and
/// HrotRunnerHarness auto-range 100-145).</para>
///
/// <list type="number">
///   <item>T8 — EqsSensor config replicates from Brain (CGF) to Muscle (SimHost).</item>
///   <item>T9 — EqsResultEvent round-trip populates EqsCognitiveBuffer.IsReady on Brain.</item>
///   <item>T10 — Entity destruction triggers NOT_ALIVE_DISPOSED, removing EqsSensor from Muscle.</item>
/// </list>
///
/// <para><b>T10 deviation from spec:</b> The spec says "remove EqsSensor from Brain entity".
/// The translator's <c>Dispose(networkEntityId)</c> (which sends DDS NOT_ALIVE_DISPOSED) is
/// invoked by <c>CycloneNetworkCleanupSystem</c> only on entity destruction, not on component
/// removal.  T10 therefore destroys the entity via <see cref="DestroyEntityCommand"/> to
/// exercise the NOT_ALIVE_DISPOSED path.</para>
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsTranslatorTests
{
    /// <para>Domain range: 71-79.</para>
    private static int _domainCounter = 70;
    private static int NextDomain() => Interlocked.Increment(ref _domainCounter);

    // ── T8 — EqsSensor config replicates Brain -> Muscle ─────────────────────

    [Fact(Timeout = 30_000)]
    public void EqsTranslators_T8_ConfigReplicatesBrainToMuscle()
    {
        int domainId = NextDomain();
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        long networkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // Wait for the ghost entity to appear on the Muscle side.
        bool entityReady = harness.PumpUntil(
            () => harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out _),
            timeoutFrames: 2000);
        Assert.True(entityReady, "Muscle entity must appear within timeout after split-authority spawn.");

        // Add EqsSensor to the Brain (CGF) entity.
        harness.Cgf!.GhostEntityMap!.TryGetEntity(networkId, out Entity cgfEntity);
        harness.Cgf!.World!.AddComponent(cgfEntity, new EqsSensor
            { BlueprintId = 1u, Epoch = 1u, SearchRadius = 25f });

        // PumpUntil the Muscle ghost entity carries the replicated EqsSensor.
        bool replicated = harness.PumpUntil(() =>
        {
            if (!harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out Entity simEntity))
                return false;
            if (!harness.SimHost.World!.HasComponent<EqsSensor>(simEntity))
                return false;
            return harness.SimHost.World.GetComponent<EqsSensor>(simEntity).SearchRadius == 25f;
        }, timeoutFrames: 2000);

        Assert.True(replicated, "EqsSensor must replicate from Brain to Muscle (SearchRadius == 25f) within timeout.");
    }

    // ── T9 — EqsResult round-trip populates Brain EqsCognitiveBuffer ─────────

    [Fact(Timeout = 30_000)]
    public void EqsTranslators_T9_ResultRoundTripPopulatesBrainBuffer()
    {
        int domainId = NextDomain();
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        long networkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        harness.PumpUntil(
            () => harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out _),
            timeoutFrames: 2000);

        harness.Cgf!.GhostEntityMap!.TryGetEntity(networkId, out Entity cgfEntity);
        harness.Cgf!.World!.AddComponent(cgfEntity, new EqsSensor
            { BlueprintId = 1u, Epoch = 1u, SearchRadius = 25f });

        // PumpUntil Brain entity has EqsCognitiveBuffer.IsReady == true.
        // Pipeline: EqsSensor(Brain) -> DDS -> EqsSensor(Muscle) ->
        //           EqsSolverSystem(Muscle, stub, 10Hz) -> EqsResultEvent ->
        //           EqsResultEventEgressTranslator -> DDS -> EqsResultIngressTranslator(Brain) ->
        //           EqsResultUpdateEvent(Brain bus) -> EqsResultUpdateSystem(Brain) ->
        //           EqsCognitiveBuffer.IsReady
        bool bufferReady = harness.PumpUntil(() =>
        {
            var world = harness.Cgf!.World;
            if (world == null) return false;
            if (!world.HasComponent<EqsCognitiveBuffer>(cgfEntity)) return false;
            return world.GetComponent<EqsCognitiveBuffer>(cgfEntity).IsReady;
        }, timeoutFrames: 2000);

        Assert.True(bufferReady,
            "Brain EqsCognitiveBuffer must be ready (IsReady == true) after EQS result round-trip.");
    }

    // ── T10 — Entity destruction triggers NOT_ALIVE_DISPOSED on Muscle ────────

    [Fact(Timeout = 30_000)]
    public void EqsTranslators_T10_EntityDestroyedRemovesSensorFromMuscle()
    {
        int domainId = NextDomain();
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        long networkId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        harness.PumpUntil(
            () => harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out _),
            timeoutFrames: 2000);

        harness.Cgf!.GhostEntityMap!.TryGetEntity(networkId, out Entity cgfEntity);
        harness.Cgf!.World!.AddComponent(cgfEntity, new EqsSensor
            { BlueprintId = 1u, Epoch = 1u, SearchRadius = 25f });

        // Wait for EqsSensor to replicate to Muscle (T8 precondition).
        harness.PumpUntil(() =>
        {
            if (!harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out Entity simEntity))
                return false;
            return harness.SimHost.World!.HasComponent<EqsSensor>(simEntity);
        }, timeoutFrames: 2000);

        // Destroy entity on Brain side.  CycloneNetworkCleanupSystem calls
        // EqsSensorConfigEgressTranslator.Dispose(networkId) which writes
        // DDS NOT_ALIVE_DISPOSED.  The Muscle's EqsSensorConfigIngressTranslator
        // receives it and removes EqsSensor from the ghost entity.
        harness.Cgf!.World!.Bus.PublishManaged(new DestroyEntityCommand
        {
            NetworkId = networkId,
            Reason    = "T10-NotAliveDisposed-test",
        });

        // PumpUntil the Muscle entity is gone from the entity map (entity fully destroyed)
        // OR has lost the EqsSensor component via NOT_ALIVE_DISPOSED.
        bool sensorRemoved = harness.PumpUntil(() =>
        {
            if (!harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out Entity simEntity))
                return true; // Entity purged from map — cleanup completed.
            if (!harness.SimHost.World!.IsAlive(simEntity))
                return true;
            return !harness.SimHost.World.HasComponent<EqsSensor>(simEntity);
        }, timeoutFrames: 2000);

        Assert.True(sensorRemoved,
            "EqsSensor must be removed from Muscle after entity destruction triggers NOT_ALIVE_DISPOSED.");
    }
}
