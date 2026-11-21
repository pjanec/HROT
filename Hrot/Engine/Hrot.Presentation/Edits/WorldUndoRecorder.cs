using System;
using Fdp.Core;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Replication.Edits;

namespace Hrot.Presentation.Edits;

/// <summary>
/// ⭐⭐ B-14 / Edit-mode undo U2 (📄 docs/UX/DESIGN_Edit_Mode_Undo.md §2.1 <c>WorldStep</c>, §2.2, R-4133 Q1) — ONE user action on the world is ONE
/// step of the editor's ONE <see cref="EditHistory"/>. The step does not know how to invert anything: <see cref="Begin"/> captures the world as it is
/// (<see cref="WorldSnapshotRing.Capture"/>); Undo captures the AFTER state and restores the before; Redo restores the after. ⇒ every write path inside a
/// bracket is undoable by construction.
/// <para>Brackets come from <see cref="Edits.Submit"/> (every accepted authoring edit) and from the map gizmos' begin / commit (a drag — its preview writes
/// the world directly, so its "before" must be taken when it STARTS). They nest: only the outermost pair is the step.</para>
/// <para>⭐ Only while <paramref name="recording"/> holds (the editor's network-less Edit mode, Q2): elsewhere no step is taken. ⭐ Typing in one field
/// coalesces: the same label again within <see cref="CoalesceSeconds"/> of the last step's end, with nothing undone in between, joins that step.</para>
/// </summary>
public sealed class WorldUndoRecorder : IEditStepRecorder
{
    /// <summary>How close a repeat of the same action must follow to join the last step (the design's "~1 s").</summary>
    public const double CoalesceSeconds = 1.0;

    private readonly IUndoStepSink _history;
    private readonly WorldSnapshotRing _ring;
    private readonly Func<bool> _recording;
    private readonly Func<double> _now;

    private int _depth;
    private string? _label;
    private long _before;
    private bool _open;

    private string? _lastLabel;
    private double _lastEnd = double.NegativeInfinity;
    private int _lastVersion = -1;

    public WorldUndoRecorder(IUndoStepSink history, WorldSnapshotRing ring, Func<bool> recording, Func<double>? now = null)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _ring = ring ?? throw new ArgumentNullException(nameof(ring));
        _recording = recording ?? throw new ArgumentNullException(nameof(recording));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _now = now ?? (() => clock.Elapsed.TotalSeconds);
    }

    /// <summary>How many world steps were recorded (a rail reads it).</summary>
    public int Recorded { get; private set; }

    /// <inheritdoc/>
    public void Begin(EntityRepository repo, string label)
    {
        if (_depth++ > 0) return;                 // nested: the outer bracket is the step
        _open = false;
        if (!_recording() || _history.Applying) return;   // ⭐ CE-3386 — a write inside another step's apply / undo / redo is part of THAT step
        label = string.IsNullOrWhiteSpace(label) ? "Edit" : label;
        if (label == _lastLabel && _history.Version == _lastVersion && _now() - _lastEnd <= CoalesceSeconds) { _label = label; return; }   // joins the last step
        _before = _ring.Capture(repo);
        _label = label;
        _open = true;
    }

    /// <inheritdoc/>
    public void End(EntityRepository repo)
    {
        if (_depth == 0 || --_depth > 0) return;
        if (_label is null) return;               // not recording
        if (_open)
        {
            long before = _before;
            long after = 0;
            _history.Push(_label,
                redo: () => _ring.Restore(repo, after),
                undo: () => { after = _ring.Capture(repo); _ring.Restore(repo, before); },
                beforeSlot: before,
                available: () => _ring.Holds(before));
            Recorded++;
        }
        _lastLabel = _label;
        _lastEnd = _now();
        _lastVersion = _history.Version;
        _label = null;
        _open = false;
    }

    /// <inheritdoc/>
    public void Cancel(EntityRepository repo)
    {
        if (_depth == 0 || --_depth > 0) return;
        _label = null;                            // the captured slot stays in the ring until it ages out: harmless
        _open = false;
    }
}
