#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle.Events;

namespace Hrot.Stride.Core;

/// <summary>
/// Pre-physics system that reactively creates and destroys Bullet bodies keyed on the
/// authority bit (STR-P1-T2, design §5.6).
///
/// <para>
/// <b>Body lifecycle:</b>
/// <list type="bullet">
///   <item>
///     <b>Creation</b>: every entity that is <c>WithOwned&lt;SimTransform&gt;</c> and
///     NOT yet in <see cref="Bodies"/> → call
///     <see cref="IPhysicsBodyService.CreateBody"/> using the shape from the entity's
///     <see cref="StrideVisualReference"/> in <paramref name="visualBindingSystem"/>,
///     then record a <see cref="PhysicsBodyReference"/> in <see cref="Bodies"/>.
///   </item>
///   <item>
///     <b>Revocation</b>: every entity whose authority has been revoked
///     (<c>WithoutOwned&lt;SimTransform&gt;</c>) but still appears in
///     <see cref="Bodies"/> → call <see cref="IPhysicsBodyService.RemoveBody"/>,
///     remove the entry.
///   </item>
///   <item>
///     <b>Destruction</b>: consume <see cref="DestructionOrder"/> events → tear down
///     body + remove entry (the entity may already be non-owned, so both paths are
///     safe to call in sequence).
///   </item>
/// </list>
/// </para>
///
/// <para>
/// Bodies are stored in a parallel <see cref="Dictionary{TKey,TValue}"/> (not in the
/// ECS itself) because Bullet body objects cannot be blitted into fixed-size ECS
/// component slots — the same pattern used by
/// <see cref="StrideVisualBindingSystem"/> for <see cref="StrideVisualReference"/>.
/// </para>
///
/// <para>
/// <b>Shape source:</b> reads <see cref="StrideVisualReference.ShapeKind"/> and
/// <see cref="StrideVisualReference.Dims"/> from the visual binding system's
/// <see cref="StrideVisualBindingSystem.Visuals"/> dictionary.  If no visual
/// reference exists for an entity (e.g. the visual hasn't been created yet), body
/// creation is <em>skipped</em> for this frame and retried next frame.
/// </para>
///
/// <para>
/// <b>Idempotency:</b> a second pass for an entity that already has a
/// <see cref="PhysicsBodyReference"/> is a no-op (the dictionary lookup short-circuits).
/// </para>
///
/// <para>
/// <b>Phase:</b> <see cref="SystemPhase.Simulation"/> — runs before the physics step
/// each frame.  Matches design §5.6 "runs pre-physics".
/// </para>
/// </summary>
[UpdateInPhase(SystemPhase.Simulation)]
public sealed class PhysicsBodyLifecycleSystem : IEcsModuleSystem
{
    // ── Diagnostics (DIAG-LC) ──────────────────────────────────────────────
    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

    // First N verbatim events, then throttled.
    private const int VerbatimLimit = 10;
    private int _verbatimCount;                // total verbatim lines emitted so far

    // Throttled counters (reset each ~1 s window).
    private int _throttledCreates;
    private int _throttledTeardowns;
    private readonly Stopwatch _throttleSw = Stopwatch.StartNew();
    private const double ThrottleWindowSec = 1.0;

    private void LogCreate(Entity entity)
    {
        if (_verbatimCount < VerbatimLimit)
        {
            ++_verbatimCount;
            Log.Info("[Lifecycle] CREATE entity={0} (owned)", entity);
        }
        else
        {
            ++_throttledCreates;
            FlushThrottleIfDue();
        }
    }

    private void LogTeardown(Entity entity, string reason)
    {
        if (_verbatimCount < VerbatimLimit)
        {
            ++_verbatimCount;
            Log.Info("[Lifecycle] TEARDOWN entity={0} ({1})", entity, reason);
        }
        else
        {
            ++_throttledTeardowns;
            FlushThrottleIfDue();
        }
    }

    private void FlushThrottleIfDue()
    {
        if (_throttleSw.Elapsed.TotalSeconds < ThrottleWindowSec) return;
        Log.Info("[Lifecycle] last {0:F1}s: creates={1} teardowns={2}",
            _throttleSw.Elapsed.TotalSeconds, _throttledCreates, _throttledTeardowns);
        _throttledCreates   = 0;
        _throttledTeardowns = 0;
        _throttleSw.Restart();
    }
    // ── End diagnostics ────────────────────────────────────────────────────

    private readonly IPhysicsBodyService    _bodyService;
    private readonly StrideVisualBindingSystem _visualBindingSystem;

    // Parallel dictionary: FDP entity ↔ PhysicsBodyReference.
    // Not in the ECS because Bullet body objects are managed class references.
    private readonly Dictionary<Entity, PhysicsBodyReference> _bodies = new();

    /// <summary>
    /// Read-only view of the active body map (for tests / diagnostics).
    /// </summary>
    public IReadOnlyDictionary<Entity, PhysicsBodyReference> Bodies => _bodies;

    /// <summary>
    /// Constructs the system.
    /// </summary>
    /// <param name="bodyService">
    /// Physics body service (create/remove Bullet bodies).
    /// Use <see cref="RecordingFakePhysicsBodyService"/> in tests.
    /// </param>
    /// <param name="visualBindingSystem">
    /// The visual binding system whose <see cref="StrideVisualBindingSystem.Visuals"/>
    /// dictionary supplies the per-entity shape kind and dimensions.
    /// </param>
    public PhysicsBodyLifecycleSystem(
        IPhysicsBodyService    bodyService,
        StrideVisualBindingSystem visualBindingSystem)
    {
        _bodyService         = bodyService         ?? throw new ArgumentNullException(nameof(bodyService));
        _visualBindingSystem = visualBindingSystem ?? throw new ArgumentNullException(nameof(visualBindingSystem));
    }

    /// <summary>
    /// Runs pre-physics: processes destructions, revocations, then creations.
    /// </summary>
    public void Execute(ISimulationView view, float deltaTime)
    {
        // ── 1. Destruction events ─────────────────────────────────────────────
        // Consume DestructionOrder events first so we don't try to process
        // a destroyed entity in the creation sweep below.
        _destroyedThisFrame.Clear();
        var destructions = view.ReadEvents<DestructionOrder>();
        foreach (ref readonly var evt in destructions)
        {
            LogTeardown(evt.Entity, "destruction event");
            TeardownBody(evt.Entity);
            _destroyedThisFrame.Add(evt.Entity);
        }

        // ── 2. Revocation: WithoutOwned<SimTransform> + body exists ──────────
        // Build a query for alive entities with a SimTransform but without ownership.
        // If such an entity is in our dictionary, its authority was revoked — remove body.
        var revokedQuery = view.Query()
            .With<SimTransform>()
            .WithoutOwned<SimTransform>()
            .Build();
        _staleEntities.Clear();
        foreach (var entity in revokedQuery)
        {
            if (_bodies.ContainsKey(entity))
                _staleEntities.Add(entity);
        }
        foreach (var entity in _staleEntities)
        {
            LogTeardown(entity, "ownership revoked");
            TeardownBody(entity);
        }

        // ── 3. Creation: WithOwned<SimTransform> + no body yet ───────────────
        var ownedQuery = view.Query()
            .With<SimTransform>()
            .WithOwned<SimTransform>()
            .Build();

        foreach (var entity in ownedQuery)
        {
            // Skip entities that were just destroyed this frame — they are being torn down
            // and even if the entity handle is still alive in the ECS (the destroy command
            // is deferred), we must not re-create the body we just removed.
            if (_destroyedThisFrame.Contains(entity))
                continue;

            if (_bodies.ContainsKey(entity))
                continue; // already has a body — idempotency

            // Shape is taken from the StrideVisualReference (already resolved by
            // StrideVisualBindingSystem; never re-resolve the descriptor here).
            if (!_visualBindingSystem.Visuals.TryGetValue(entity, out var visualRef))
                continue; // visual not yet created — skip; retry next frame

            ref readonly var simTf = ref view.GetComponentRO<SimTransform>(entity);
            var handle = _bodyService.CreateBody(
                entity, visualRef.ShapeKind, visualRef.Dims, in simTf);

            _bodies[entity] = new PhysicsBodyReference(handle, visualRef.ShapeKind, visualRef.Dims);
            LogCreate(entity);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private readonly List<Entity>      _staleEntities     = new(16);
    private readonly HashSet<Entity>   _destroyedThisFrame = new();

    private void TeardownBody(Entity entity)
    {
        if (!_bodies.TryGetValue(entity, out var bodyRef))
            return; // nothing to tear down

        _bodyService.RemoveBody(bodyRef.BodyHandle);
        _bodies.Remove(entity);
    }

    /// <summary>
    /// Tears down all active bodies.  Called during subsystem disposal.
    /// </summary>
    public void DestroyAll()
    {
        foreach (var kvp in _bodies)
            _bodyService.RemoveBody(kvp.Value.BodyHandle);
        _bodies.Clear();
    }
}
