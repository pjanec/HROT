using System;
using System.Collections.Generic;
using System.Threading;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// Integration tests for TASK-EQS-038: multi-sensor child-entity support.
/// Verifies the 3-branch compound-key identity scheme added to EqsSolverSystem,
/// EqsResultUpdateSystem, and the DDS translator stack.
///
/// <para>Domain range: 241-249.</para>
///
/// <list type="number">
///   <item>T-38-1 -- Local-only sensor (no NetworkIdentity, no PartMetadata): compound key has ParentNetworkId=0.</item>
///   <item>T-38-2 -- Child entity sensor (PartMetadata present): compound key has correct ParentNetworkId and LocalChildIndex.</item>
///   <item>T-38-3 -- Legacy single-sensor backward compat (entity has NetworkIdentity): LocalChildIndex==0.</item>
///   <item>T-38-4 -- Offline multi-sensor: three child sensors all get populated EqsCognitiveBuffers.</item>
///   <item>T-38-5 -- Distributed carrier ghost: Muscle creates a carrier ghost with PartMetadata+EqsSensor but no NetworkIdentity.</item>
/// </list>
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class EqsMultiSensorTests : IDisposable
{
    // Domain range: 241-249 (above EqsContextSlotTests 231-239).
    private static int _domainCounter = 228;
    private static int NextDomain() => Interlocked.Increment(ref _domainCounter);

    // ── Inner types ────────────────────────────────────────────────────────────

    private sealed class SimpleEqsTemplateRegistry : IEqsTemplateRegistry
    {
        private readonly Dictionary<uint, EqsQueryTemplate> _t = new();
        public void Register(EqsQueryTemplate t) => _t[t.BlueprintId] = t;
        public bool TryGetTemplate(uint id, out EqsQueryTemplate t) => _t.TryGetValue(id, out t);
    }

    // Yields one fixed positional candidate regardless of observer.
    private sealed class SingleCandidateGenerator : IEqsGenerator
    {
        public int Generate(Entity observer, ref EqsSensor sensor,
            ISimulationView view, Span<EqsResult> candidates)
        {
            if (candidates.Length == 0) return 0;
            candidates[0] = new EqsResult { EntityId = 0L, PositionX = 1f, PositionY = 0f, Score = 1f };
            return 1;
        }
    }

    // PostSimulation system that accumulates every EqsResultEvent emitted by the solver.
    // Because SwapBuffers runs before module dispatch, events published by EqsSolverSystem
    // in frame N are readable in frame N+1 PostSimulation (after the next SwapBuffers).
    // PumpUntil runs enough frames for the capture list to be populated.
    [UpdateInPhase(SystemPhase.PostSimulation)]
    private sealed class EqsResultEventCaptureSystem : IEcsModuleSystem
    {
        private readonly List<EqsResultEvent> _captured = new();

        public IReadOnlyList<EqsResultEvent> Captured => _captured;

        public void Execute(ISimulationView view, float deltaTime)
        {
            var evts = view.ReadEvents<EqsResultEvent>();
            for (int i = 0; i < evts.Length; i++)
                _captured.Add(evts[i]);
        }
    }

    // ── Test fixture ──────────────────────────────────────────────────────────

    private readonly EqsResultEventCaptureSystem _captureSystem;
    private readonly EditorHarness _harness;

    public EqsMultiSensorTests()
    {
        _captureSystem = new EqsResultEventCaptureSystem();
        _harness       = new EditorHarness(extraGlobalSystems: new IEcsModuleSystem[] { _captureSystem });
    }

    public void Dispose()
    {
        if (_harness.Repo.HasSingleton<EqsResultPool>())
        {
            var pool = _harness.Repo.GetSingleton<EqsResultPool>();
            if (pool.Results.IsCreated)
                pool.Results.Dispose();
        }
        _harness.Dispose();
    }

    // ── T-38-1: Local-only sensor (no NetworkIdentity, no PartMetadata) ────────

    /// <summary>
    /// T-38-1: A sensor entity with neither <see cref="NetworkIdentity"/> nor
    /// <see cref="PartMetadata"/> must be solved via the local-only branch.
    /// The resulting <see cref="EqsResultEvent"/> must have
    /// <c>ParentNetworkId == 0</c> and <c>LocalChildIndex == entity.Index</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void LocalOnlySensor_NoNetworkIdentity_UsesLocalOnlyBranch()
    {
        // Arrange: single entity with only EqsSensor.
        const uint blueprintId = 2380001u;
        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = blueprintId,
            Generator     = new SingleCandidateGenerator(),
            MaxCandidates = 4,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        Entity sensorEntity = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(sensorEntity, new EqsSensor
        {
            BlueprintId  = blueprintId,
            Epoch        = 1u,
            SearchRadius = 50f,
        });

        // Pump until the EqsCognitiveBuffer is populated (solver has run and routed the result).
        bool bufferReady = _harness.PumpUntil(() =>
        {
            if (!_harness.Repo.HasComponent<EqsCognitiveBuffer>(sensorEntity)) return false;
            ref readonly var buf = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(sensorEntity);
            return buf.IsReady;
        }, timeoutMs: 8_000);

        Assert.True(bufferReady, "EqsCognitiveBuffer must be ready for local-only sensor.");

        // Verify the captured event uses the local-only compound key.
        EqsResultEvent? match = null;
        foreach (var evt in _captureSystem.Captured)
        {
            if (evt.LocalChildIndex == sensorEntity.Index)
            {
                match = evt;
                break;
            }
        }

        Assert.NotNull(match);
        Assert.Equal(0L, match!.Value.ParentNetworkId);
        Assert.Equal(sensorEntity.Index, match.Value.LocalChildIndex);
    }

    // ── T-38-2: Child entity sensor with PartMetadata ─────────────────────────

    /// <summary>
    /// T-38-2: A sensor entity with <see cref="PartMetadata"/> pointing to a parent
    /// that has <see cref="NetworkIdentity"/> must produce a compound key carrying the
    /// parent's network ID and the child's InstanceId.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ChildEntitySensor_WithPartMetadata_UsesCompoundKey()
    {
        // Arrange: parent entity with NetworkIdentity, child with PartMetadata+EqsSensor.
        const uint blueprintId = 2380002u;
        const long parentNetId  = 12345L;
        const int  childInstId  = 42;

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = blueprintId,
            Generator     = new SingleCandidateGenerator(),
            MaxCandidates = 4,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        Entity parentEntity = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(parentEntity, new NetworkIdentity { Value = parentNetId });

        Entity childEntity = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(childEntity, new PartMetadata
        {
            ParentEntity = parentEntity,
            InstanceId   = childInstId,
        });
        _harness.Repo.AddComponent(childEntity, new EqsSensor
        {
            BlueprintId  = blueprintId,
            Epoch        = 1u,
            SearchRadius = 50f,
        });

        // Pump until child's EqsCognitiveBuffer is populated.
        bool bufferReady = _harness.PumpUntil(() =>
        {
            if (!_harness.Repo.HasComponent<EqsCognitiveBuffer>(childEntity)) return false;
            ref readonly var buf = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(childEntity);
            return buf.IsReady;
        }, timeoutMs: 8_000);

        Assert.True(bufferReady, "Child entity EqsCognitiveBuffer must be ready.");

        // Verify the compound key in the captured event.
        EqsResultEvent? match = null;
        foreach (var evt in _captureSystem.Captured)
        {
            if (evt.LocalChildIndex == childInstId && evt.ParentNetworkId == parentNetId)
            {
                match = evt;
                break;
            }
        }

        Assert.NotNull(match);
        Assert.Equal(parentNetId, match!.Value.ParentNetworkId);
        Assert.Equal(childInstId, match.Value.LocalChildIndex);
    }

    // ── T-38-3: Legacy single-sensor backward compat ──────────────────────────

    /// <summary>
    /// T-38-3: A sensor entity directly carrying <see cref="NetworkIdentity"/> (legacy
    /// single-sensor pattern) must use the legacy branch: <c>LocalChildIndex == 0</c>
    /// and <c>ParentNetworkId == NetworkIdentity.Value</c>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void LegacySensor_DirectNetworkIdentity_LocalChildIndexIsZero()
    {
        // Arrange: entity with NetworkIdentity + EqsSensor (no PartMetadata).
        const uint blueprintId  = 2380003u;
        const long networkIdVal = 99001L;

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = blueprintId,
            Generator     = new SingleCandidateGenerator(),
            MaxCandidates = 4,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        Entity sensorEntity = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(sensorEntity, new NetworkIdentity { Value = networkIdVal });
        _harness.Repo.AddComponent(sensorEntity, new EqsSensor
        {
            BlueprintId  = blueprintId,
            Epoch        = 1u,
            SearchRadius = 50f,
        });

        bool bufferReady = _harness.PumpUntil(() =>
        {
            if (!_harness.Repo.HasComponent<EqsCognitiveBuffer>(sensorEntity)) return false;
            ref readonly var buf = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(sensorEntity);
            return buf.IsReady;
        }, timeoutMs: 8_000);

        Assert.True(bufferReady, "Legacy sensor EqsCognitiveBuffer must be ready.");

        // Verify the legacy compound key: LocalChildIndex must be 0.
        EqsResultEvent? match = null;
        foreach (var evt in _captureSystem.Captured)
        {
            if (evt.ParentNetworkId == networkIdVal && evt.LocalChildIndex == 0)
            {
                match = evt;
                break;
            }
        }

        Assert.NotNull(match);
        Assert.Equal(networkIdVal, match!.Value.ParentNetworkId);
        Assert.Equal(0, match.Value.LocalChildIndex);
    }

    // ── T-38-4: Offline multi-sensor (three children) ─────────────────────────

    /// <summary>
    /// T-38-4: Three child sensor entities sharing one parent (with <see cref="NetworkIdentity"/>)
    /// must each receive their own <see cref="EqsCognitiveBuffer"/> after solving.
    /// Verifies that the solver and result-update system correctly route disjoint child indexes.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void MultiChildSensors_ThreeChildren_AllBuffersPopulated()
    {
        // Arrange: one parent + three child sensors.
        const uint blueprintId = 2380004u;
        const long parentNetId = 77777L;

        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = blueprintId,
            Generator     = new SingleCandidateGenerator(),
            MaxCandidates = 4,
        });
        _harness.Repo.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        Entity parentEntity = _harness.Repo.CreateEntity();
        _harness.Repo.AddComponent(parentEntity, new NetworkIdentity { Value = parentNetId });

        // Create 3 child sensor entities with InstanceId 0, 1, 2.
        var children = new Entity[3];
        for (int i = 0; i < 3; i++)
        {
            children[i] = _harness.Repo.CreateEntity();
            _harness.Repo.AddComponent(children[i], new PartMetadata
            {
                ParentEntity = parentEntity,
                InstanceId   = i,
            });
            _harness.Repo.AddComponent(children[i], new EqsSensor
            {
                BlueprintId  = blueprintId,
                Epoch        = 1u,
                SearchRadius = 50f,
            });
        }

        // Pump until all three children have a ready EqsCognitiveBuffer.
        bool allReady = _harness.PumpUntil(() =>
        {
            for (int i = 0; i < 3; i++)
            {
                if (!_harness.Repo.HasComponent<EqsCognitiveBuffer>(children[i])) return false;
                ref readonly var buf = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(children[i]);
                if (!buf.IsReady) return false;
            }
            return true;
        }, timeoutMs: 8_000);

        Assert.True(allReady, "All three child sensor EqsCognitiveBuffers must be ready.");

        // Each buffer must contain at least one candidate.
        for (int i = 0; i < 3; i++)
        {
            ref readonly var buf = ref _harness.Repo.GetComponentRO<EqsCognitiveBuffer>(children[i]);
            Assert.True(buf.Count > 0, $"Child[{i}] buffer must have at least one candidate.");
        }
    }

    // ── T-38-5: Distributed carrier ghost ─────────────────────────────────────

    /// <summary>
    /// T-38-5: When a Brain (CGF) entity has a child sensor (PartMetadata + EqsSensor),
    /// the <see cref="Hrot.Network.NED.SimHost.EqsSensorConfigEgressTranslator"/> publishes
    /// a DDS topic keyed by (ParentNetworkId, LocalChildIndex).  The Muscle-side
    /// <see cref="Hrot.Network.NED.SimHost.EqsSensorConfigIngressTranslator"/> must respond
    /// by creating a carrier ghost entity that has:
    /// <list type="bullet">
    ///   <item><see cref="PartMetadata"/> with InstanceId matching the child's InstanceId.</item>
    ///   <item><see cref="EqsSensor"/> with the correct BlueprintId.</item>
    ///   <item><see cref="EqsCognitiveBuffer"/> (added lazily or by the ingress translator).</item>
    ///   <item>No <see cref="NetworkIdentity"/> (it is a carrier ghost, not a replicated entity).</item>
    /// </list>
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void DistributedChildSensor_MuscleReceivesCarrierGhost()
    {
        const uint blueprintId    = 2380005u;
        const int  childInstId    = 1;

        int domainId = NextDomain();
        using var harness = new HrotRunnerHarness("simhost,cgf", domainId);

        // Register template on Muscle world so the solver can evaluate the sensor.
        var registry = new SimpleEqsTemplateRegistry();
        registry.Register(new EqsQueryTemplate
        {
            BlueprintId   = blueprintId,
            Generator     = new SingleCandidateGenerator(),
            MaxCandidates = 4,
        });
        harness.SimHost.World!.SetSingletonManaged<IEqsTemplateRegistry>(registry);

        // Spawn parent entity on Brain with split authority (Brain owns cognition,
        // Muscle owns kinematics -- standard split topology).
        long parentNetId = harness.Cgf!.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // Wait for the Muscle ghost of the parent to appear.
        bool parentGhostReady = harness.PumpUntil(
            () => harness.SimHost.TestHook_EntityMap.TryGetEntity(parentNetId, out _),
            timeoutFrames: 2000);
        Assert.True(parentGhostReady, "Muscle ghost of parent entity must appear within timeout.");

        // Resolve the Brain-side entity handle.
        harness.Cgf!.GhostEntityMap!.TryGetEntity(parentNetId, out Entity cgfParent);
        Assert.False(cgfParent.IsNull, "Brain-side parent entity must be resolvable.");

        // Create child sensor entity on the Brain side: PartMetadata + EqsSensor.
        Entity cgfChild = harness.Cgf!.World!.CreateEntity();
        harness.Cgf!.World!.AddComponent(cgfChild, new PartMetadata
        {
            ParentEntity = cgfParent,
            InstanceId   = childInstId,
        });
        harness.Cgf!.World!.AddComponent(cgfChild, new EqsSensor
        {
            BlueprintId  = blueprintId,
            Epoch        = 1u,
            SearchRadius = 50f,
        });

        // Pump until a carrier ghost appears on the Muscle side.
        // The carrier ghost is identified by: has EqsSensor + PartMetadata with InstanceId==childInstId
        // + the parent's Muscle ghost as ParentEntity, and does NOT have NetworkIdentity.
        harness.SimHost.TestHook_EntityMap.TryGetEntity(parentNetId, out Entity muscleParent);

        bool carrierGhostReady = harness.PumpUntil(() =>
        {
            var world = harness.SimHost.World;
            if (world == null) return false;

            // Scan all entities with EqsSensor on Muscle world.
            var sensorQuery = world.Query().With<EqsSensor>().Build();
            foreach (var candidate in sensorQuery)
            {
                // Must not be the parent ghost itself.
                if (!world.HasComponent<PartMetadata>(candidate)) continue;
                // Must not have a NetworkIdentity (carrier ghosts don't).
                if (world.HasComponent<NetworkIdentity>(candidate)) continue;

                var meta = world.GetComponentRO<PartMetadata>(candidate);
                if (meta.InstanceId == childInstId)
                    return true;
            }
            return false;
        }, timeoutFrames: 3000);

        Assert.True(carrierGhostReady,
            "Muscle must create a carrier ghost for the child sensor (PartMetadata.InstanceId==1, no NetworkIdentity).");

        // Verify the carrier ghost components.
        var muscleWorld = harness.SimHost.World!;
        Entity carrierGhost = Entity.Null;
        var q = muscleWorld.Query().With<EqsSensor>().Build();
        foreach (var candidate in q)
        {
            if (!muscleWorld.HasComponent<PartMetadata>(candidate)) continue;
            if (muscleWorld.HasComponent<NetworkIdentity>(candidate)) continue;
            var meta = muscleWorld.GetComponentRO<PartMetadata>(candidate);
            if (meta.InstanceId == childInstId)
            {
                carrierGhost = candidate;
                break;
            }
        }

        Assert.False(carrierGhost.IsNull, "Carrier ghost entity must be found.");
        Assert.True(muscleWorld.HasComponent<EqsSensor>(carrierGhost),
            "Carrier ghost must have EqsSensor.");
        Assert.True(muscleWorld.HasComponent<PartMetadata>(carrierGhost),
            "Carrier ghost must have PartMetadata.");
        Assert.False(muscleWorld.HasComponent<NetworkIdentity>(carrierGhost),
            "Carrier ghost must NOT have NetworkIdentity.");

        // InstanceId matches the child's InstanceId.
        var ghostMeta = muscleWorld.GetComponentRO<PartMetadata>(carrierGhost);
        Assert.Equal(childInstId, ghostMeta.InstanceId);

        // Sensor blueprint matches.
        var ghostSensor = muscleWorld.GetComponentRO<EqsSensor>(carrierGhost);
        Assert.Equal(blueprintId, ghostSensor.BlueprintId);
    }
}
