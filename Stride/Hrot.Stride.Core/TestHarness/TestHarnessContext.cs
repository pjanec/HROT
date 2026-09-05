#nullable enable
using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.Core.Network;
using Stride.Engine;
using StrideEntity = Stride.Engine.Entity;

namespace Hrot.Stride.Core.TestHarness;

/// <summary>
/// The execution context handed to every <see cref="VisualTestCase.Run"/> delegate
/// (BATCH-12, STR-TEST-1). It is the single surface a test case uses to drive the live
/// <c>editor_stride</c> app: spawn/destroy FDP entities, inspect/move their state, reach the
/// Stride scene + camera, write to the harness log, and register continuous per-frame
/// behaviour.
///
/// <para>
/// <b>Engine-agnostic by design.</b> Every member is expressed in types available to
/// <c>Hrot.Stride.Core</c> (Fdp.Core <see cref="EntityRepository"/>, Hrot.Core
/// <see cref="ScenarioEntityCreationRequestSource"/>, Hrot.Stride.Core
/// <see cref="StrideVisualBindingSystem"/>, and Stride <see cref="Scene"/> /
/// <see cref="Entity"/>). The context therefore does <b>not</b> reference
/// <c>HrotStrideApp.Game</c> (no dependency cycle); the game constructs it from its
/// <c>EditorStrideSubsystem</c> instance.
/// </para>
///
/// <para>
/// <b>Continuous cases.</b> A case that needs to run every frame (e.g. an orbiting entity)
/// calls <see cref="RegisterUpdate"/> from inside its <see cref="VisualTestCase.Run"/>. The
/// harness pumps all registered hooks once per frame via <see cref="PumpUpdates"/>. A hook
/// returns <c>true</c> to keep running or <c>false</c> to stop (one-shot / finished).
/// </para>
///
/// <para>
/// <b>Threading.</b> All members are invoked on the single Stride game thread (from
/// <c>Game.Update</c>), satisfying the design §8.3 single-thread invariant. The context is
/// not thread-safe and must not be touched from background threads.
/// </para>
/// </summary>
public sealed class TestHarnessContext
{
    private readonly Action<string> _log;
    private readonly List<Func<float, bool>> _updateHooks = new();
    // Scratch list so PumpUpdates does not allocate per frame and tolerates hooks that
    // register/unregister other hooks during iteration.
    private readonly List<Func<float, bool>> _completedHooks = new();

    /// <summary>
    /// Constructs the context.
    /// </summary>
    /// <param name="world">The live FDP ECS world (simulation layer).</param>
    /// <param name="scenarioSource">
    /// The scenario spawn queue; <see cref="ScenarioEntityCreationRequestSource.Enqueue"/>
    /// feeds the Brain spawn path (CreateEntityRequestSystem → NetworkSpawningSystem).
    /// </param>
    /// <param name="visualBindingSystem">
    /// The Stride visual binding system (may be <c>null</c> in a headless harness without a
    /// GPU factory). Cases that inspect visuals must null-check it.
    /// </param>
    /// <param name="scene">The active Stride root scene (where visuals + camera live).</param>
    /// <param name="cameraEntity">The overview camera entity (may be <c>null</c> if absent).</param>
    /// <param name="log">
    /// The harness log sink: writes via NLog and (optionally) echoes on-screen. Never null.
    /// </param>
    public TestHarnessContext(
        EntityRepository world,
        ScenarioEntityCreationRequestSource scenarioSource,
        StrideVisualBindingSystem? visualBindingSystem,
        Scene scene,
        StrideEntity? cameraEntity,
        Action<string> log)
    {
        World               = world          ?? throw new ArgumentNullException(nameof(world));
        ScenarioSource      = scenarioSource  ?? throw new ArgumentNullException(nameof(scenarioSource));
        VisualBindingSystem = visualBindingSystem;
        Scene               = scene          ?? throw new ArgumentNullException(nameof(scene));
        CameraEntity        = cameraEntity;
        _log                = log            ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>The live FDP ECS world (simulation layer).</summary>
    public EntityRepository World { get; }

    /// <summary>The scenario spawn queue feeding the Brain spawn path.</summary>
    public ScenarioEntityCreationRequestSource ScenarioSource { get; }

    /// <summary>
    /// The Stride visual binding system; <c>null</c> when the harness runs without a GPU
    /// visual factory (headless). Cases that read <c>Visuals</c> must null-check.
    /// </summary>
    public StrideVisualBindingSystem? VisualBindingSystem { get; }

    /// <summary>The active Stride root scene.</summary>
    public Scene Scene { get; }

    /// <summary>The overview camera entity (may be <c>null</c>).</summary>
    public StrideEntity? CameraEntity { get; }

    /// <summary>Number of continuous per-frame hooks currently registered.</summary>
    public int ActiveUpdateHookCount => _updateHooks.Count;

    /// <summary>
    /// Writes <paramref name="message"/> to the harness log (NLog + optional on-screen echo).
    /// </summary>
    public void Log(string message) => _log(message ?? string.Empty);

    /// <summary>
    /// Registers a continuous per-frame hook. The hook is invoked once per frame by
    /// <see cref="PumpUpdates"/> with the frame delta-time (seconds); it returns <c>true</c>
    /// to keep running or <c>false</c> to stop and be removed.
    /// </summary>
    /// <param name="hook">The per-frame callback. Must not be null.</param>
    public void RegisterUpdate(Func<float, bool> hook)
    {
        if (hook == null) throw new ArgumentNullException(nameof(hook));
        _updateHooks.Add(hook);
    }

    /// <summary>
    /// Removes every registered continuous hook (used by a "stop all" / "clear" case).
    /// </summary>
    public void ClearUpdates() => _updateHooks.Clear();

    /// <summary>
    /// Pumps all registered continuous hooks once. Called by the harness every frame.
    /// Hooks returning <c>false</c> are removed after the pass. Exceptions from a hook are
    /// caught, logged, and the offending hook removed so one bad case cannot wedge the loop.
    /// </summary>
    /// <param name="dt">Frame delta-time in seconds.</param>
    public void PumpUpdates(float dt)
    {
        if (_updateHooks.Count == 0)
            return;

        _completedHooks.Clear();

        // Snapshot count so hooks that register new hooks this frame run next frame, not
        // re-entrantly this frame (avoids infinite growth within a single pump).
        int count = _updateHooks.Count;
        for (int i = 0; i < count; i++)
        {
            var hook = _updateHooks[i];
            bool keep;
            try
            {
                keep = hook(dt);
            }
            catch (Exception ex)
            {
                _log($"[harness] continuous hook threw and was removed: {ex.GetType().Name}: {ex.Message}");
                keep = false;
            }
            if (!keep)
                _completedHooks.Add(hook);
        }

        foreach (var done in _completedHooks)
            _updateHooks.Remove(done);
    }
}
