using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo;

namespace Hrot.UI.Common.Map3D.Director
{
    /// <summary>
    /// ⭐ <c>CE-1364</c> T3 (docs/DESIGN_Director_Timeline_Editor.md §10, T-L2 / T-L3) — the take OPEN in the editor: ONE <see cref="TakeEditor"/> per open take,
    /// shared by the Director window (open · save · close) and the Event Timeline panel (its <see cref="Tracks"/>), so a drag on the panel and a click in the
    /// window are the same edit path. It also owns the editor's <see cref="GizmoUndoStack"/> for take edits — the editor had none; this is the one stack
    /// (T-L2: no fifth kind), reached by the panel's Ctrl+Z / Ctrl+Y and by <see cref="UndoLast"/> / <see cref="RedoLast"/>.
    /// <para>The playback rate (<see cref="Speed"/>) lives here too: the window's slider and the tracks' J / L are one number, handed to the runner while it plays.</para>
    /// </summary>
    public sealed class DirectorTakeSession
    {
        /// <summary>The rates J / L step through (the window's slider spans 0.25–2).</summary>
        public static readonly float[] Rates = { 0.25f, 0.5f, 0.75f, 1f, 1.5f, 2f };

        private readonly Func<TakeStore?> _store;
        private readonly Func<DirectorRunner?> _runner;
        private readonly Func<CameraSpec?> _pilot;
        private readonly Func<double, EventAnchor?>? _eventAt;
        private DirectorTracks? _tracks;

        /// <param name="eventAt">⭐ CR-6: the nearest event of THIS host's timeline index at or before a sim second (K hangs the new key on it); null = no index.</param>
        public DirectorTakeSession(Func<TakeStore?> store, Func<DirectorRunner?> runner, Func<CameraSpec?> pilot, Func<double, EventAnchor?>? eventAt = null)
        {
            _store = store; _runner = runner; _pilot = pilot; _eventAt = eventAt;
        }

        /// <summary>The editor's undo stack for take edits.</summary>
        public GizmoUndoStack UndoStack { get; } = new();

        public TakeEditor? Editor { get; private set; }
        public string? Scenario => Editor?.Take.Scenario;
        public string? TakeName => Editor?.Take.Name;

        /// <summary>The Camera and Time tracks of the open take; empty when none is open. (The panel's <c>Tracks</c> func.)</summary>
        public IReadOnlyList<ITimelineTrack> Tracks => _tracks?.Tracks ?? Array.Empty<ITimelineTrack>();

        /// <summary>The playback rate (sim seconds per film second) the Play button starts with and J / L change.</summary>
        public float Speed { get; set; } = 1f;

        /// <summary>J (−1) / L (+1): the next lower / higher rate of <see cref="Rates"/>; handed to the runner while it plays.</summary>
        public void StepRate(int direction)
        {
            int i = direction < 0
                ? Array.FindLastIndex(Rates, r => r < Speed - 1e-4f)
                : Array.FindIndex(Rates, r => r > Speed + 1e-4f);
            if (i < 0) return;
            Speed = Rates[i];
            if (_runner() is { Busy: true } runner) runner.SetSpeed(Speed);
        }

        /// <summary>Opens <paramref name="scenario"/>/<paramref name="take"/> for editing. Refused (with the reason) while another take has unsaved edits.</summary>
        public IReadOnlyList<string> Open(string scenario, string take)
        {
            if (Editor is { Dirty: true } open && !(open.Take.Scenario == scenario && open.Take.Name == take))
                return new[] { $"'{open.Take.Scenario}/{open.Take.Name}' has unsaved edits — save or close it first" };
            if (Editor is { } same && same.Take.Scenario == scenario && same.Take.Name == take) return Array.Empty<string>();
            if (_store() is not { } store) return new[] { "no take store on this host" };
            var loaded = store.Load(scenario, take);
            if (loaded.Value is null) return loaded.Problems;
            UndoStack.Clear();
            Editor = new TakeEditor(store, loaded.Value, UndoStack);
            _tracks = new DirectorTracks(Editor, _pilot, StepRate, _eventAt);
            return Array.Empty<string>();
        }

        /// <summary>The path written, or null when nothing was open or edited (a take that was not edited is never re-written, T-L4).</summary>
        public string? Save() => Editor?.Save();

        /// <summary>Closes the open take, dropping its unsaved edits and its undo history.</summary>
        public void Close()
        {
            Editor = null; _tracks = null;
            UndoStack.Clear();
        }

        public bool CanUndo => UndoStack.CanUndo;
        public bool CanRedo => UndoStack.CanRedo;

        /// <summary>Undoes the last take edit (the take's records ignore the command buffer — T-L2's wart).</summary>
        public void UndoLast() => UndoStack.Undo(null!);
        public void RedoLast() => UndoStack.Redo(null!);
    }
}
