using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Hrot.MuscleCharacter.Animation.Contracts;

namespace Hrot.Stride.Animation;

/// <summary>
/// Drives the <see cref="StrideAnimationBackend"/> from FDP ECS state for the
/// <c>editor_stride</c> composition (STR-P4-T3/T4, DD-1 §10 "AnimationRuntimeBridgeSystem",
/// design §6.4). It is the headless, testable glue that the full
/// <c>AnimationRuntimeBridgeSystem</c> would be in a Muscle node — but adapted to the
/// editor_stride world, which does not run the DD-4 <c>AnimationTkbTranslator</c> (so there
/// are no <c>CharacterAnimationDefRuntime</c>/<c>AnimationExecutorState</c> components to
/// read). Instead this bridge identifies animated (mannequin) entities directly by their
/// <see cref="TkbIdentity"/> via the <c>isAnimatedClass</c> predicate supplied at
/// construction.
///
/// <para>Per <see cref="Execute"/> call (once per FDP frame) it:</para>
/// <list type="number">
///   <item><b>Reconciles backend registration</b> with the live mannequin entity set —
///     <see cref="StrideAnimationBackend.RegisterEntity"/> on first appearance,
///     <see cref="StrideAnimationBackend.UnregisterEntity"/> on death/disappearance
///     (STR-P4-T3 register/unregister-on-appear/death).</item>
///   <item><b>Pumps locomotion inputs</b> — reads <see cref="SimTransform"/> +
///     <see cref="SimVelocity"/> (physics-sourced on a Stride node; driven directly by the
///     harness in the NoOp-physics app) and calls
///     <see cref="StrideAnimationBackend.UpdateLocomotionInputs"/> so the backend blends
///     idle→walk→run by planar speed.</item>
///   <item><b>Advances any in-flight jump montage sequences</b> (Start→Loop→End) and
///     <b>ticks the backend</b> once.</item>
/// </list>
///
/// <para><b>Montage dispatch (STR-P4-T4):</b> the off-mesh-link traversal seam is the
/// <see cref="OffMeshTraversalStartedEvent"/> that <c>OffMeshLinkDetectionSystem</c> publishes
/// (it deliberately does not reference the animation assembly; see its design note). Feed
/// those events to <see cref="DispatchTraversal"/> — for a <see cref="TraversalKind.Jump"/>
/// it starts the Jump_Start→Jump_Loop→Jump_End montage chain on the entity's montage slot via
/// <see cref="StrideAnimationBackend.PlayMontageOnSlot"/>. The harness "Trigger Jump" case
/// calls <see cref="TriggerJump"/> directly so it works without the nav stack wired.</para>
///
/// <para><b>No Stride engine types appear here</b> — the GPU-bound
/// <see cref="PerEntityBlendTreeBuilder"/> attachment is done separately by the visual binding
/// (it needs a real <c>AnimationComponent</c>); this bridge owns only the headless decision
/// logic, so it is fully unit-testable without a <c>GraphicsDevice</c>.</para>
/// </summary>
public sealed class StrideAnimationBridge
{
    /// <summary>The three sequential jump-traversal montage phases.</summary>
    private enum JumpPhase : byte { Start = 0, Loop = 1, End = 2, Done = 3 }

    private readonly StrideAnimationBackend _backend;
    private readonly Func<long, bool> _isAnimatedClass;
    private readonly int _jumpStartMontageId;
    private readonly int _jumpLoopMontageId;
    private readonly int _jumpEndMontageId;

    // Registered animated entities: PackedValue -> (handle, the Entity it was registered for).
    private readonly Dictionary<ulong, Registration> _registered = new();

    // Reusable stale list (never reallocated) so reconciliation does not allocate per frame.
    private readonly List<ulong> _stale = new(32);

    // In-flight jump montage sequences keyed by entity PackedValue.
    private readonly Dictionary<ulong, JumpSequence> _jumps = new();
    private readonly List<ulong> _finishedJumps = new(8);

    // ── Locomotion-blend diagnostic throttle ──────────────────────────────────
    // Throttle: emit blend-weight log once every N frames per entity.
    // At 60 fps this is ~2 s; enough to see the blend changing without flooding the log.
    private const int BlendLogIntervalFrames = 120;
    private readonly Dictionary<ulong, int> _blendLogCounter = new();

    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

    private readonly struct Registration
    {
        public Registration(AnimationBackendHandleBox handle, Entity entity)
        {
            Handle = handle;
            Entity = entity;
        }
        public AnimationBackendHandleBox Handle { get; }
        public Entity Entity { get; }
    }

    private sealed class JumpSequence
    {
        public JumpPhase Phase;
        public float PhaseElapsed;
    }

    /// <summary>
    /// Construct the bridge.
    /// </summary>
    /// <param name="backend">The animation backend to drive (non-null).</param>
    /// <param name="isAnimatedClass">
    /// Predicate mapping a <see cref="TkbIdentity.TkbType"/> to whether that class is an
    /// animated mannequin (i.e. has a <c>CharacterAnimationDefDto</c>). Entities whose class
    /// returns <c>true</c> are registered with the backend and locomotion-driven.
    /// </param>
    /// <param name="jumpStartMontageId">MontageAssetId hash of the Jump_Start montage.</param>
    /// <param name="jumpLoopMontageId">MontageAssetId hash of the Jump_Loop montage.</param>
    /// <param name="jumpEndMontageId">MontageAssetId hash of the Jump_End montage.</param>
    public StrideAnimationBridge(
        StrideAnimationBackend backend,
        Func<long, bool> isAnimatedClass,
        int jumpStartMontageId,
        int jumpLoopMontageId,
        int jumpEndMontageId)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _isAnimatedClass = isAnimatedClass ?? throw new ArgumentNullException(nameof(isAnimatedClass));
        _jumpStartMontageId = jumpStartMontageId;
        _jumpLoopMontageId = jumpLoopMontageId;
        _jumpEndMontageId = jumpEndMontageId;
    }

    /// <summary>Number of entities currently registered with the backend through this bridge.</summary>
    public int RegisteredCount => _registered.Count;

    /// <summary>Number of in-flight jump montage sequences.</summary>
    public int ActiveJumpCount => _jumps.Count;

    /// <summary>
    /// Try to get the backend handle the bridge holds for a live entity (test/diagnostic seam).
    /// Returns false if the entity is not currently registered.
    /// </summary>
    public bool TryGetHandle(Entity entity, out AnimationBackendHandle handle)
    {
        if (_registered.TryGetValue(entity.PackedValue, out var reg))
        {
            handle = reg.Handle.Value;
            return true;
        }
        handle = default;
        return false;
    }

    /// <summary>
    /// Advance the bridge one FDP frame: reconcile registration with the live animated
    /// entity set, pump locomotion inputs from <see cref="SimTransform"/>/<see cref="SimVelocity"/>,
    /// advance in-flight jump sequences, and tick the backend.
    /// </summary>
    /// <param name="world">The shared editor_stride ECS world.</param>
    /// <param name="deltaTime">Frame delta in seconds.</param>
    public void Execute(EntityRepository world, float deltaTime)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));

        ReconcileRegistrations(world);
        PumpLocomotion(world);
        AdvanceJumpSequences(world, deltaTime);

        _backend.Tick(deltaTime);
    }

    /// <summary>
    /// Route off-mesh-link traversal events (the <c>OffMeshLinkDetectionSystem</c> seam) to
    /// the backend's montage path. Call once per frame with the events read from the bus.
    /// </summary>
    public void DispatchTraversals(ReadOnlySpan<OffMeshTraversalStartedEvent> events)
    {
        for (int i = 0; i < events.Length; i++)
            DispatchTraversal(events[i]);
    }

    /// <summary>
    /// Route a single off-mesh-link traversal to the backend's montage path. A
    /// <see cref="TraversalKind.Jump"/> begins the Jump_Start→Loop→End sequence on the
    /// entity's montage slot. Other traversal kinds are ignored this pass (only Jump is
    /// authored on the mannequin).
    /// </summary>
    public void DispatchTraversal(in OffMeshTraversalStartedEvent evt)
    {
        if (evt.TraversalKind != TraversalKind.Jump)
            return;
        TriggerJump(evt.Target);
    }

    /// <summary>
    /// Begin (or restart) the Jump_Start→Jump_Loop→Jump_End montage sequence on
    /// <paramref name="entity"/>'s montage slot. The entity must be registered with the
    /// backend (animated mannequin); no-op otherwise. Used by both
    /// <see cref="DispatchTraversal"/> and the harness "Trigger Jump" case.
    /// </summary>
    public void TriggerJump(Entity entity)
    {
        if (!_registered.TryGetValue(entity.PackedValue, out var reg))
            return;

        PlayJumpMontage(reg.Handle.Value, _jumpStartMontageId);

        _jumps[entity.PackedValue] = new JumpSequence { Phase = JumpPhase.Start, PhaseElapsed = 0f };
    }

    // ── Registration reconciliation ─────────────────────────────────────────

    private void ReconcileRegistrations(EntityRepository world)
    {
        // Pass 1: unregister entities that died or are no longer present/animated.
        _stale.Clear();
        foreach (var kvp in _registered)
        {
            var entity = kvp.Value.Entity;
            bool stillAnimated = world.IsAlive(entity)
                && world.HasComponent<TkbIdentity>(entity)
                && _isAnimatedClass(world.GetComponentRO<TkbIdentity>(entity).TkbType);
            if (!stillAnimated)
                _stale.Add(kvp.Key);
        }
        foreach (var packed in _stale)
        {
            _backend.UnregisterEntity(_registered[packed].Handle.Value);
            _registered.Remove(packed);
            _jumps.Remove(packed);
            _blendLogCounter.Remove(packed); // clean up throttle state
        }

        // Pass 2: register newly-appeared animated entities.
        var q = world.Query()
            .With<SimTransform>()
            .With<TkbIdentity>()
            .Build();

        foreach (var entity in q)
        {
            if (_registered.ContainsKey(entity.PackedValue))
                continue;

            long tkbType = world.GetComponentRO<TkbIdentity>(entity).TkbType;
            if (!_isAnimatedClass(tkbType))
                continue;

            var handle = _backend.RegisterEntity((uint)entity.Index, tkbType);
            _registered[entity.PackedValue] = new Registration(new AnimationBackendHandleBox(handle), entity);
        }
    }

    // ── Locomotion pumping ──────────────────────────────────────────────────

    private void PumpLocomotion(EntityRepository world)
    {
        foreach (var kvp in _registered)
        {
            var entity = kvp.Value.Entity;
            ulong packed = kvp.Key;

            if (!world.IsAlive(entity) || !world.HasComponent<SimVelocity>(entity))
            {
                // No velocity component yet → treat as at-rest (pure idle).
                _backend.UpdateLocomotionInputs(kvp.Value.Handle.Value, 0f, 0f, 0f, isGrounded: true);
                continue;
            }

            ref readonly var vel = ref world.GetComponentRO<SimVelocity>(entity);

            // FDP world axes: X = east, Y = north, Z = up. Planar locomotion speed is the
            // magnitude of the (X, Y) ground-plane velocity; vertical is Z. The backend's
            // LocomotionBlend uses the planar magnitude, so we hand it the two horizontal
            // components and the vertical separately. Grounded := negligible vertical motion.
            float horizX = vel.Linear.X;
            float horizZ = vel.Linear.Y; // FDP-north maps to the backend's second planar axis
            float vertical = vel.Linear.Z;
            bool grounded = MathF.Abs(vertical) < 0.01f;

            _backend.UpdateLocomotionInputs(kvp.Value.Handle.Value, horizX, horizZ, vertical, grounded);

            // ── Throttled locomotion blend diagnostic (BATCH-17 follow-up) ────
            // Confirms that SimVelocity→walk blend reaches the builder.
            // Emitted at Debug level every BlendLogIntervalFrames frames per entity.
            if (!_blendLogCounter.TryGetValue(packed, out int counter))
                counter = 0;

            counter++;
            if (counter >= BlendLogIntervalFrames)
            {
                counter = 0;
                var blend = _backend.QueryLocomotion(kvp.Value.Handle.Value);
                Log.Debug(
                    "[StrideAnimationBridge] entity #{0} locomotion blend: " +
                    "Idle={1:F3} Walk={2:F3} Run={3:F3} Factor={4:F3} " +
                    "SimVel=({5:F2},{6:F2},{7:F2}) grounded={8}",
                    entity.Index,
                    blend.Idle, blend.Walk, blend.Run, blend.Factor,
                    horizX, horizZ, vertical, grounded);
            }
            _blendLogCounter[packed] = counter;
        }
    }

    // ── Jump montage sequencing ─────────────────────────────────────────────

    private void AdvanceJumpSequences(EntityRepository world, float deltaTime)
    {
        if (_jumps.Count == 0)
            return;

        _finishedJumps.Clear();

        foreach (var kvp in _jumps)
        {
            ulong packed = kvp.Key;
            var seq = kvp.Value;

            if (!_registered.TryGetValue(packed, out var reg) || !world.IsAlive(reg.Entity))
            {
                _finishedJumps.Add(packed);
                continue;
            }

            AnimationBackendHandle handle = reg.Handle.Value;
            seq.PhaseElapsed += deltaTime;

            // Advance to the next phase when the backend reports the current slot is no
            // longer active (the montage played out) — the slot state machine owns the
            // actual timing; we just chain the next clip when it finishes.
            bool slotActive = _backend.IsAnySlotActive(handle);
            if (!slotActive)
            {
                switch (seq.Phase)
                {
                    case JumpPhase.Start:
                        seq.Phase = JumpPhase.Loop;
                        seq.PhaseElapsed = 0f;
                        PlayJumpMontage(handle, _jumpLoopMontageId);
                        break;
                    case JumpPhase.Loop:
                        seq.Phase = JumpPhase.End;
                        seq.PhaseElapsed = 0f;
                        PlayJumpMontage(handle, _jumpEndMontageId);
                        break;
                    case JumpPhase.End:
                        seq.Phase = JumpPhase.Done;
                        _finishedJumps.Add(packed);
                        break;
                }
            }
        }

        foreach (var packed in _finishedJumps)
            _jumps.Remove(packed);
    }

    private void PlayJumpMontage(AnimationBackendHandle handle, int montageId)
    {
        _backend.PlayMontageOnSlot(handle, new PlayMontageParams
        {
            MontageId = montageId,
            PlayRate = 1f,
            BlendInTime = 0.08f,
            BlendOutTime = 0.1f,
            StartSectionIndex = 0,
        });
    }

    /// <summary>
    /// Boxed handle wrapper so the bridge can store handles in a reference-typed dictionary
    /// value without the struct being copied into a value-type slot that would defeat
    /// generation tracking. Keeps <see cref="AnimationBackendHandle"/> a value type while the
    /// dictionary value stays a class.
    /// </summary>
    private sealed class AnimationBackendHandleBox
    {
        public AnimationBackendHandleBox(AnimationBackendHandle value) => Value = value;
        public AnimationBackendHandle Value { get; }
    }
}
