#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.Stride.Core;

/// <summary>
/// Authority-forked forward-sync that replaces the P0 flat forward-sync in
/// <c>EditorStrideSubsystem</c> (STR-P1-T6, design §7).
///
/// <para>
/// Runs <b>after</b> the FDP kernel tick (<c>_core.Tick</c> / <c>Kernel.Update()</c>)
/// each frame. Implements two passes:
/// </para>
///
/// <para>
/// <b>Pass A — reconcile Stride visual entity set (all entities):</b>
/// Delegates to <see cref="StrideVisualBindingSystem.Sync"/> which performs a
/// two-pass differential upsert/teardown (appear → create, die → teardown),
/// identical to the mock's <c>SyncFdpToStrideScript</c>. This is orthogonal to the
/// authority fork — it manages existence, not transform direction.
/// </para>
///
/// <para>
/// <b>Pass B — forward-sync (FDP → Stride visual), non-owned entities only:</b>
/// Queries <c>.WithoutOwned&lt;SimTransform&gt;()</c> — entities without local
/// physics authority (ghosts driven by <c>DeadReckoningSyncSystem</c> or
/// <c>PlaybackTickSystem</c> during replay). Writes the Stride visual entity
/// transform from <see cref="SimTransform"/> via
/// <see cref="FdpStrideTransform.ToStridePosition"/> /
/// <see cref="FdpStrideTransform.ToStrideRotation"/>.
/// Locally-owned entities are <b>skipped</b> — their Stride body is physics-driven
/// by Bullet and must not be overwritten from FDP state (doing so would jitter/freeze
/// the physics simulation).
/// </para>
///
/// <para>
/// <b>Why <c>.WithoutOwned</c>:</b> O(1) bitwise test against the
/// <see cref="EntityMetadataCold"/> authority bit. Reflects runtime authority
/// transfers automatically (Mode 2's <c>DeferredTakeoverSystem</c> flips an entity from
/// forward to reverse with no extra code path). In Mode 1 every entity is owned so
/// Pass B matches nothing and is a no-op; in Mode 2 it forward-syncs remote ghosts.
/// Same code, both modes. (Design §7.)
/// </para>
/// </summary>
public sealed class SplitAuthorityStrideSyncScript
{
    private readonly StrideVisualBindingSystem _visualBindingSystem;
    private readonly IStrideVisualFactory      _factory;

    /// <summary>
    /// Constructs the split-authority sync script.
    /// </summary>
    /// <param name="visualBindingSystem">
    /// The visual binding system responsible for Pass A (entity set reconciliation:
    /// appear/disappear). Must be the same instance used by the rest of the system.
    /// </param>
    /// <param name="factory">
    /// The visual factory used to update visual poses for non-owned entities (Pass B).
    /// The factory's <c>UpdatePose</c> method receives the FDP-to-Stride-converted
    /// <see cref="SimTransform"/> for each non-owned entity.
    /// </param>
    public SplitAuthorityStrideSyncScript(
        StrideVisualBindingSystem visualBindingSystem,
        IStrideVisualFactory      factory)
    {
        _visualBindingSystem = visualBindingSystem
            ?? throw new ArgumentNullException(nameof(visualBindingSystem));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Executes one frame:
    /// 1. Pass A — delegate visual set reconciliation to <see cref="StrideVisualBindingSystem"/>.
    /// 2. Pass B — forward-sync non-owned entities' visual transforms.
    /// </summary>
    /// <param name="world">The ECS repository (read-write access required for Pass A queries).</param>
    public void Sync(EntityRepository world)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));

        // ── Pass A: reconcile Stride visual entity set (appear/disappear) ─────
        // SyncExistenceOnly handles the two-pass differential upsert/teardown
        // (appear → create, die → teardown) WITHOUT calling UpdatePose for existing
        // entities. Pass B handles the transform update with authority-forked logic.
        _visualBindingSystem.SyncExistenceOnly(world);

        // ── Pass B: forward-sync non-owned entities only ──────────────────────
        // Owned entities are skipped — their Stride body is driven by Bullet physics.
        // Non-owned entities (ghosts, replayed) get their visual pose from SimTransform.
        ISimulationView view = world;
        var nonOwnedQuery = view.Query()
            .With<SimTransform>()
            .WithoutOwned<SimTransform>()
            .Build();

        foreach (var entity in nonOwnedQuery)
        {
            // Only update visuals that exist (Pass A may not yet have created one
            // if this is the same frame as entity appearance).
            if (!_visualBindingSystem.Visuals.TryGetValue(entity, out var visualRef))
                continue;

            ref readonly var simTf = ref view.GetComponentRO<SimTransform>(entity);

            // Build a SimTransform in Stride coordinates to pass to UpdatePose.
            // The factory interprets position + rotation in FDP space; it applies
            // FdpStrideTransform internally when converting to Stride entity transform.
            // We pass the FDP-space SimTransform directly — the factory contract matches.
            _factory.UpdatePose(visualRef.VisualHandle, in simTf);
        }
    }
}
