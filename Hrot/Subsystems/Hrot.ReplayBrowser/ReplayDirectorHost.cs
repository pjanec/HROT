using System;
using Fdp.Core;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.ReplayBrowser;

/// <summary>
/// ⭐ <c>CE-1303</c> P2a — CR-3 (docs/DESIGN_Director_Timeline_Editor.md §11m D1) — the Replay Browser as the director's host: the SOURCE is the
/// open recording, so it can seek. Every member is a delegate onto what <see cref="ReplayBrowserSubsystem"/> already holds (its shown world, its
/// play state, its playhead), as <c>EditorDirectorHost</c> is onto the editor's — ⛔ it holds nothing of its own, so it cannot drift from the
/// browser's own controls. Main thread only.
/// <list type="bullet">
/// <item><b>Load / preview:</b> the recording IS the world — nothing loads. <see cref="LoadScenario"/> puts the playhead at the recording's start;
/// the preview is "a recording is open", entering or leaving it only pauses the viewer's own playback (⛔ never closes the recording).</item>
/// <item><b>No entity count</b> (<see cref="EntityCount"/> is null): nothing reloads, so <see cref="ScenarioReadyProbe"/> has no edge to watch and
/// is ready on the state alone — a count would make it wait out its grace on every play.</item>
/// <item><b>Step and seek</b> move the playhead by SIM time through the one call every playhead move of this host ends in
/// (<c>FederatedReplayManager.SetBaseWallTicks</c> — the Event Timeline's cursor and the viewer's loop wrap make it too), with no history: the
/// director is playing, not navigating.</item>
/// </list>
/// </summary>
internal sealed class ReplayDirectorHost : IDirectorHost
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<bool> _open;
    private readonly Func<bool> _playing;
    private readonly Action _pause;
    private readonly Func<double> _sim;
    private readonly Action<double> _moveTo;
    private readonly Func<int, string?> _behaviourName;
    private readonly System.Diagnostics.Stopwatch _wall = System.Diagnostics.Stopwatch.StartNew();

    /// <param name="world">The world shown now (the primary node's sandbox, or the merged one).</param>
    /// <param name="open">A recording is open.</param>
    /// <param name="playing">The viewer's own playback runs.</param>
    /// <param name="pause">Pauses the viewer's own playback.</param>
    /// <param name="simSeconds">The recording's sim second at the playhead.</param>
    /// <param name="moveTo">Moves the playhead to a sim second (clamped to the recording), no history.</param>
    public ReplayDirectorHost(Func<EntityRepository?> world, Func<bool> open, Func<bool> playing, Action pause, Func<double> simSeconds,
                              Action<double> moveTo, Func<int, string?> behaviourName, TakeStore takes)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _playing = playing ?? throw new ArgumentNullException(nameof(playing));
        _pause = pause ?? throw new ArgumentNullException(nameof(pause));
        _sim = simSeconds ?? throw new ArgumentNullException(nameof(simSeconds));
        _moveTo = moveTo ?? throw new ArgumentNullException(nameof(moveTo));
        _behaviourName = behaviourName ?? throw new ArgumentNullException(nameof(behaviourName));
        Takes = takes ?? throw new ArgumentNullException(nameof(takes));
    }

    public EntityRepository? World => _world();

    /// <summary>The recording is the world: to its start (the run's sim 0, clamped to the first frame).</summary>
    public void LoadScenario(string scenario) { if (_open()) _moveTo(0d); }

    public bool IsOperatingEdit => _open();
    public int? EntityCount => null;
    public bool InPreview => _open();
    public void EnterPreview(bool startPaused) => _pause();
    public void ExitPreview() => _pause();
    public bool Paused => !_playing();
    public void Pause() => _pause();

    /// <summary>One step = the playhead <paramref name="seconds"/> of sim later.</summary>
    public void StepBy(float seconds) { if (_open()) _moveTo(_sim() + seconds); }

    public double SimTime => _sim();
    public double WallNow => _wall.Elapsed.TotalSeconds;
    public string? BehaviourName(int hash) => _behaviourName(hash);
    public TakeStore Takes { get; }

    /// <summary>⭐ CR-3 — a recording seeks.</summary>
    public bool CanSeek => true;

    public void Seek(double sourceSim) { if (_open()) _moveTo(sourceSim); }

    /// <summary>Holding the frame, as the editor's host holds it: sleep until the step is due (a ms or two spun).</summary>
    public void Hold(double seconds)
    {
        if (!(seconds > 0)) return;
        double until = WallNow + Math.Min(seconds, 0.25);
        double sleep = until - WallNow - 0.002;
        if (sleep > 0) System.Threading.Thread.Sleep(TimeSpan.FromSeconds(sleep));
        while (WallNow < until) System.Threading.Thread.SpinWait(64);
    }
}
