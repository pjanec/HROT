using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Orchestration;
using Hrot.IG.Components;

namespace Hrot.StrideMock;

/// <summary>
/// ECS-to-engine synchronisation script.  Mimics the API of a Stride SyncScript.
///
/// <para>
/// Performs a 2-pass differential sync each frame:
/// <list type="bullet">
///   <item>Pass 1 (destructions): iterate dictionaries, retire stale entries via
///   <see cref="EntityRepository.IsAlive"/>.</item>
///   <item>Pass 2 (creations + updates): query ECS, upsert live entries.</item>
/// </list>
/// </para>
///
/// <para>
/// Sync is skipped (and a splash message is shown) when the cluster is in any
/// non-operating state (e.g. <c>LoadingLive</c>, <c>LoadingReplay</c>).
/// </para>
///
/// <para>
/// The two-pass approach works correctly during live, replay, and seek because
/// <c>PlaybackSystem</c> blasts raw ECS memory directly without firing lifecycle
/// events — <see cref="EntityRepository.IsAlive"/> detects the generation mismatch
/// in O(1) without any event subscriptions.
/// </para>
/// </summary>
public sealed class SyncFdpToStrideScript : FakeStrideScript
{
    private readonly StrideNodeBootstrapper _core;

    private readonly Dictionary<Entity, FakeStrideEntity> _entities = new();
    private readonly Dictionary<Entity, FakeStrideEffect>  _effects  = new();

    // Pre-allocated list reused each frame to collect stale keys. Never replaced —
    // verified by SC_SM004_8 reflection test.
    private readonly List<Entity> _staleEntities = new(64);

    /// <summary>All currently live simulation entities (non-effect).</summary>
    public IEnumerable<FakeStrideEntity> ActiveEntities => _entities.Values;

    /// <summary>All currently live visual effect entities.</summary>
    public IEnumerable<FakeStrideEffect> ActiveEffects => _effects.Values;

    /// <summary>
    /// Non-empty string when the cluster is in a loading state and a splash screen
    /// should be displayed.  Empty during operating states.
    /// </summary>
    public string CurrentStateMessage { get; private set; } = string.Empty;

    /// <summary>The most recently observed cluster state.</summary>
    public ClusterState CurrentClusterState =>
        (ClusterState)_core.Context.ClusterSlave.LocalStateIdForTest;

    /// <param name="core">The bootstrapped node providing the ECS world and cluster slave.</param>
    public SyncFdpToStrideScript(StrideNodeBootstrapper core)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
    }

    /// <inheritdoc/>
    public override void Start() { }

    /// <inheritdoc/>
    public override void Update(float deltaTime)
    {
        var state = CurrentClusterState;

        if (IsOperatingState(state))
        {
            CurrentStateMessage = string.Empty;
            SyncStrideEntities();
            SyncStrideEffects();
        }
        else
        {
            CurrentStateMessage = $"Cluster: {state}";
        }
    }

    // ── Private sync passes ───────────────────────────────────────────────────

    private void SyncStrideEntities()
    {
        var world = _core.Context.World;
        var view  = (ISimulationView)world;

        // Pass 1 — destructions: collect stale entries then remove them.
        _staleEntities.Clear();
        foreach (var kvp in _entities)
        {
            if (!world.IsAlive(kvp.Key))
                _staleEntities.Add(kvp.Key);
        }
        foreach (var stale in _staleEntities)
            _entities.Remove(stale);

        // Pass 2 — creations + updates: entities with SimTransform but without VisualEffectState.
        var query = view.Query().With<SimTransform>().Without<VisualEffectState>().Build();
        foreach (var entity in query)
        {
            ref readonly var xform = ref view.GetComponentRO<SimTransform>(entity);

            if (!_entities.TryGetValue(entity, out var strideEntity))
            {
                strideEntity = new FakeStrideEntity();
                _entities[entity] = strideEntity;
            }

            strideEntity.Position = xform.Position;
            strideEntity.Rotation = ExtractYaw(xform.Rotation);
        }
    }

    private void SyncStrideEffects()
    {
        var world = _core.Context.World;
        var view  = (ISimulationView)world;

        // Pass 1 — destructions.
        _staleEntities.Clear();
        foreach (var kvp in _effects)
        {
            if (!world.IsAlive(kvp.Key))
                _staleEntities.Add(kvp.Key);
        }
        foreach (var stale in _staleEntities)
            _effects.Remove(stale);

        // Pass 2 — creations + updates: entities with both SimTransform and VisualEffectState.
        var query = view.Query().With<SimTransform>().With<VisualEffectState>().Build();
        foreach (var entity in query)
        {
            ref readonly var xform  = ref view.GetComponentRO<SimTransform>(entity);
            ref readonly var effect = ref view.GetComponentRO<VisualEffectState>(entity);

            if (!_effects.TryGetValue(entity, out var strideEffect))
            {
                strideEffect = new FakeStrideEffect();
                _effects[entity] = strideEffect;
            }

            strideEffect.Type     = effect.Type;
            strideEffect.Position = xform.Position;
            strideEffect.Scale    = effect.Scale;
            strideEffect.Alpha    = effect.Alpha;

            // Resolve tracer endpoint from TracerTarget if present.
            if (view.HasComponent<TracerTarget>(entity))
            {
                ref readonly var tracer = ref view.GetComponentRO<TracerTarget>(entity);
                strideEffect.TracerEnd = new Vector3(tracer.EndX, tracer.EndY, 0f);
            }
            else
            {
                strideEffect.TracerEnd = Vector3.Zero;
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true for states where ECS data is valid and the 3D scene should
    /// be synchronised and rendered.
    /// </summary>
    private static bool IsOperatingState(ClusterState state) =>
        state is ClusterState.OperatingLive
               or ClusterState.OperatingEdit
               or ClusterState.OperatingPreview
               or ClusterState.OperatingReplay;

    /// <summary>
    /// Extracts the yaw angle (rotation around the world-up Z axis) from a quaternion,
    /// in radians. Uses the yaw-pitch-roll (Z-Y-X) convention from SimTransform.
    /// </summary>
    private static float ExtractYaw(Quaternion q) =>
        MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y),
                    1f - 2f * (q.Y * q.Y + q.Z * q.Z));
}
