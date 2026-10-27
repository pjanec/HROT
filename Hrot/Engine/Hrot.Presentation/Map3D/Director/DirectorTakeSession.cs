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

        /// <summary>⭐ <c>CE-1305</c> (§11v T2) — a shot template's camera for a key hung on an event (<see cref="DirectorDrafts.TemplateCamera"/>); set by
        /// <see cref="DirectorPack.Attach"/> where the host has a drafter, null otherwise (no templates offered).</summary>
        public Func<EventAnchor, ShotTemplate, double, CameraSpec?>? Templates { get; set; }

        /// <summary>The editor's undo stack for take edits.</summary>
        public GizmoUndoStack UndoStack { get; } = new();

        public TakeEditor? Editor { get; private set; }
        public string? Scenario => Editor?.Take.Scenario;
        public string? TakeName => Editor?.Take.Name;

        /// <summary>⭐ <c>CE-1303</c> P2a — CR-4: the open take as it stands NOW (unsaved edits included) when it is <paramref name="scenario"/>/<paramref name="take"/>;
        /// null otherwise. The runner plays this instead of the file (<see cref="DirectorRunner.OpenTake"/>).</summary>
        public Take? OpenTakeOf(string scenario, string take)
            => Editor is { } e && e.Take.Scenario == scenario && e.Take.Name == take ? e.Take : null;

        /// <summary>The Camera, Time and Subtitles tracks of the open take; empty when none is open. (The panel's <c>Tracks</c> func.)</summary>
        public IReadOnlyList<ITimelineTrack> Tracks => _tracks?.Tracks ?? Array.Empty<ITimelineTrack>();

        /// <summary>⭐ <c>CE-1303</c> P2b — the open take's tracks (the window's <i>Update key from view</i> and the selected segment / subtitle's fields); null when none is open.</summary>
        public DirectorTracks? Director => _tracks;

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
            OpenEditor(new TakeEditor(store, loaded.Value, UndoStack));
            return Array.Empty<string>();
        }

        /// <summary>
        /// ⭐ <c>CE-4059</c> (docs/DESIGN_Director_Timeline_Editor.md §11q D2) — opens <paramref name="draft"/> (a take never written: the drafter's) for editing as a NEW
        /// take: unsaved (<see cref="TakeEditor.Dirty"/> from the start, Save writes it), a fresh undo history; refused while another take has unsaved edits, as
        /// <see cref="Open"/> is. Undoing the draft is closing it.
        /// </summary>
        public IReadOnlyList<string> OpenDraft(Take draft)
        {
            if (draft is null) throw new ArgumentNullException(nameof(draft));
            if (Editor is { Dirty: true } open)
                return new[] { $"'{open.Take.Scenario}/{open.Take.Name}' has unsaved edits — save or close it first" };
            OpenEditor(new TakeEditor(_store(), draft, UndoStack, unsaved: true));
            return Array.Empty<string>();
        }

        /// <summary>The host's take store (null: none).</summary>
        public TakeStore? Store => _store();

        /// <summary>Counts every take opened (§11q: the Director window follows the session to a newly opened draft).</summary>
        public int Opened { get; private set; }

        private void OpenEditor(TakeEditor editor)
        {
            UndoStack.Clear();
            Editor = editor;
            // CR-7 (§11q K3) — the segment on screen while the runner plays THIS take (K / update key from view scope a key to a replay)
            _tracks = new DirectorTracks(editor, _pilot, StepRate, _eventAt,
                () => _runner() is { } r && Editor is { } e ? r.SegmentOf(e.Take.Scenario, e.Take.Name) : null,
                (anchor, template, sim) => Templates?.Invoke(anchor, template, sim));
            Opened++;
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
