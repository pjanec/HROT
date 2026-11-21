using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Panels.Timeline;
using Hrot.Presentation.Edits;

namespace Hrot.UI.Common.Map3D.Director
{
    /// <summary>
    /// ⭐ <c>CE-1364</c> T3 (docs/DESIGN_Director_Timeline_Editor.md §10, T-L2 / T-L3) — the take OPEN in the editor: ONE <see cref="TakeEditor"/> per open take,
    /// shared by the Director window (open · save · close) and the Event Timeline panel (its <see cref="Tracks"/>), so a drag on the panel and a click in the
    /// window are the same edit path. Its take edits are document steps on the host's ONE <see cref="EditHistory"/>
    /// (⭐ U4, docs/UX/DESIGN_Edit_Mode_Undo.md Q3 / Q6: the editor passes its own, so Edit ▸ Undo / Redo / History show them; a host without one — the Replay Browser —
    /// gets a private history), reached by the panel's Ctrl+Z / Ctrl+Y and by <see cref="UndoLast"/> / <see cref="RedoLast"/> (which act on a TAKE step only).
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
        /// <param name="history">⭐ U4 — the host's ONE edit history; null ⇒ a private one (no shared history on this host).</param>
        public DirectorTakeSession(Func<TakeStore?> store, Func<DirectorRunner?> runner, Func<CameraSpec?> pilot, Func<double, EventAnchor?>? eventAt = null, EditHistory? history = null)
        {
            _store = store; _runner = runner; _pilot = pilot; _eventAt = eventAt;
            History = history ?? new EditHistory();
            SharedHistory = history != null;
        }

        /// <summary>⭐ <c>CE-1305</c> (§11v T2) — a shot template's camera for a key hung on an event (<see cref="DirectorDrafts.TemplateCamera"/>); set by
        /// <see cref="DirectorPack.Attach"/> where the host has a drafter, null otherwise (no templates offered).</summary>
        public Func<EventAnchor, ShotTemplate, double, CameraSpec?>? Templates { get; set; }

        /// <summary>⭐ U4 — the history the take edits are steps of (the host's ONE, or a private one).</summary>
        public EditHistory History { get; }

        /// <summary>The history is the host's shared one: its Edit ▸ Undo / Redo keys already reach the take steps.</summary>
        public bool SharedHistory { get; }

        public TakeEditor? Editor { get; private set; }
        public string? Scenario => Editor?.Take.Scenario;
        public string? TakeName => Editor?.Take.Name;

        /// <summary>⭐ <c>CE-1303</c> P2a — CR-4: the open take as it stands NOW (unsaved edits included) when it is <paramref name="scenario"/>/<paramref name="take"/>;
        /// null otherwise. The runner plays this instead of the file (<see cref="DirectorRunner.OpenTake"/>).</summary>
        public Take? OpenTakeOf(string scenario, string take)
            => Editor is { } e && e.Take.Scenario == scenario && e.Take.Name == take ? e.Take : null;

        /// <summary>The open take's tracks as the Event Timeline shows them — ⭐ CE-4262: under ONE collapsible <i>Director</i> row (<see cref="DirectorTracks.Grouped"/>); empty when none is open. (The panel's <c>Tracks</c> func.)</summary>
        public IReadOnlyList<ITimelineTrack> Tracks => _tracks?.Grouped ?? Array.Empty<ITimelineTrack>();

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
            OpenEditor(new TakeEditor(store, loaded.Value, History, owner: this));
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
            OpenEditor(new TakeEditor(_store(), draft, History, unsaved: true, owner: this));
            return Array.Empty<string>();
        }

        /// <summary>The host's take store (null: none).</summary>
        public TakeStore? Store => _store();

        /// <summary>Counts every take opened (§11q: the Director window follows the session to a newly opened draft).</summary>
        public int Opened { get; private set; }

        private void OpenEditor(TakeEditor editor)
        {
            Editor?.Retire();   // ⭐ U4 — the previous take's steps stay on the history, greyed
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
            Editor?.Retire();   // ⭐ U4 — its steps stay on the history, greyed: a closed take leaves nothing to undo
            Editor = null; _tracks = null;
        }

        /// <summary>The newest step is a take edit that can be undone (a world step on top is Edit ▸ Undo's, not the take window's).</summary>
        public bool CanUndo => History.CanUndo && ReferenceEquals(History.UndoOwner, this);
        public bool CanRedo => History.CanRedo && ReferenceEquals(History.RedoOwner, this);

        /// <summary>Undoes the last take edit.</summary>
        public void UndoLast() { if (CanUndo) History.Undo(); }
        public void RedoLast() { if (CanRedo) History.Redo(); }
    }
}
