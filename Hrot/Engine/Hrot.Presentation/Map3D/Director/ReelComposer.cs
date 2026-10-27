using System;
using System.Collections.Generic;
using System.Linq;

namespace Hrot.UI.Common.Map3D.Director
{
    /// <summary>
    /// ⭐ <c>CE-1306</c> R4 (docs/DESIGN_Director_Timeline_Editor.md §11u D1) — the REEL COMPOSER: the one editing model of a <see cref="Reel"/> (the record the
    /// runner plays and <see cref="TakeStore"/> reads and writes — ⛔ no second reel model). Open a saved reel or start a new one, add takes, remove and
    /// reorder items, set each item's title / chapter card / note and the reel's title / transition / gap / loop, save it to <c>films/&lt;name&gt;.reel.json</c>.
    /// The Director window draws it; the runner plays <see cref="Reel"/> AS IT STANDS (unsaved edits included — CR-4's rule for a take, here for a reel).
    /// Pure: no ImGui, no runner.
    /// </summary>
    public sealed class ReelComposer
    {
        private readonly Func<TakeStore?> _store;
        private readonly Func<string?>? _openRecording;
        private Reel? _reel;

        /// <param name="store">The host's takes and reels.</param>
        /// <param name="openRecording">⭐ CE-1391 — the recording open now (its <c>RecordingName.Text</c>; null: none), stamped on a take <see cref="Add"/> adds:
        /// the take lives beside THAT recording, so the item plays over it (TE-16). Null: this host opens no recordings (the editor).</param>
        public ReelComposer(Func<TakeStore?> store, Func<string?>? openRecording = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _openRecording = openRecording;
        }

        /// <summary>The reel being composed (null: none open). Every edit replaces it with a new record.</summary>
        public Reel? Reel => _reel;

        public bool IsOpen => _reel != null;

        /// <summary>Unsaved edits since the open or the last save.</summary>
        public bool Dirty { get; private set; }

        /// <summary>Opens the saved reel <paramref name="name"/>; returns why not (empty: opened). A reel with problems still opens (its problems are returned).</summary>
        public IReadOnlyList<string> Open(string name)
        {
            if (_store() is not { } store) return new[] { "no take store on this host" };
            var loaded = store.LoadReel(name);
            if (loaded.Value is null) return loaded.Problems;
            _reel = loaded.Value;
            Dirty = false;
            return loaded.Problems;
        }

        /// <summary>Starts a new, empty reel named <paramref name="name"/> (unsaved). Refused when the name is not a file name or a reel of that name exists.</summary>
        public IReadOnlyList<string> New(string name, string? title = null)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/'))
                return new[] { $"'{name}' is not a reel name (a file name)" };
            if (_store() is { } store && store.ReelNames().Contains(name, StringComparer.Ordinal))
                return new[] { $"a reel '{name}' exists: open it instead" };
            _reel = new Reel { Name = name, Title = title };
            Dirty = true;
            return Array.Empty<string>();
        }

        public void Close() { _reel = null; Dirty = false; }

        /// <summary>Appends (or inserts at <paramref name="at"/>) the take <paramref name="scenario"/>/<paramref name="take"/>; false when no reel is open.
        /// ⭐ CE-1391 — on a host that opens recordings the item names the recording open now (the take is the one beside it), so a reel can gather takes of
        /// several recordings — open one, add its take, open the next.</summary>
        public bool Add(string scenario, string take, int? at = null, string? title = null)
        {
            if (_reel is not { } r || string.IsNullOrWhiteSpace(scenario) || string.IsNullOrWhiteSpace(take)) return false;
            var items = r.Items.ToList();
            int i = at is { } a ? Math.Clamp(a, 0, items.Count) : items.Count;
            items.Insert(i, new ReelItem { Scenario = scenario, Take = take, Title = title, Recording = _openRecording?.Invoke() });
            return Set(r with { Items = items });
        }

        public bool Remove(int index)
        {
            if (_reel is not { } r || index < 0 || index >= r.Items.Count) return false;
            var items = r.Items.ToList();
            items.RemoveAt(index);
            return Set(r with { Items = items });
        }

        /// <summary>Moves item <paramref name="from"/> to position <paramref name="to"/> (clamped); false when nothing moves.</summary>
        public bool Move(int from, int to)
        {
            if (_reel is not { } r || from < 0 || from >= r.Items.Count) return false;
            to = Math.Clamp(to, 0, r.Items.Count - 1);
            if (to == from) return false;
            var items = r.Items.ToList();
            var it = items[from];
            items.RemoveAt(from);
            items.Insert(to, it);
            return Set(r with { Items = items });
        }

        /// <summary>Replaces item <paramref name="index"/> (its title, chapter card, note, clip, from / to — the options the model has).</summary>
        public bool SetItem(int index, ReelItem item)
        {
            if (_reel is not { } r || index < 0 || index >= r.Items.Count || item == null || string.IsNullOrWhiteSpace(item.Scenario)) return false;
            if (r.Items[index] == item) return false;
            var items = r.Items.ToList();
            items[index] = item;
            return Set(r with { Items = items });
        }

        /// <summary>The reel's own options: title, transition, gap between items (seconds, 0–10), loop (a live show only).</summary>
        public bool SetOptions(string? title, string transition, double gapSeconds, bool loop)
        {
            if (_reel is not { } r) return false;
            var next = r with { Title = string.IsNullOrWhiteSpace(title) ? null : title, Transition = string.IsNullOrWhiteSpace(transition) ? "fade" : transition,
                                GapSeconds = Math.Clamp(gapSeconds, 0d, 10d), Loop = loop };
            return next != r && Set(next);
        }

        /// <summary>Writes the reel to its file; returns the path, or null with <paramref name="problem"/>.</summary>
        public string? Save(out string? problem)
        {
            problem = null;
            if (_reel is not { } r) { problem = "no reel is open"; return null; }
            if (r.Items.Count == 0) { problem = "a reel needs at least one item"; return null; }
            if (_store() is not { } store) { problem = "no take store on this host"; return null; }
            try
            {
                var path = store.SaveReel(r);
                Dirty = false;
                return path;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
            {
                problem = ex.Message;
                return null;
            }
        }

        private bool Set(Reel next)
        {
            _reel = next;
            Dirty = true;
            return true;
        }
    }
}
