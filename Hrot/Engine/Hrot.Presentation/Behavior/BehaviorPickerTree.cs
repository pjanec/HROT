using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.UI.Common.Facades;
using ImGuiNET;

namespace Hrot.Presentation.Behavior
{
    /// <summary>
    /// ⭐⭐ <c>CE-3164</c> (R-268) — the ONE behaviour picker: a combo whose popup is a FOLDER TREE built from each choice's
    /// <see cref="BehaviorChoice.Group"/> (<c>/</c>-separated). Nothing is hidden — demos, samples and tests are offered in their
    /// folders. <c>Library</c> sorts first and opens by default, <c>Other</c> sorts last, and the folder holding the current pick
    /// opens too. Used by the mission panel's task rows and the entity AI details' task/SOP rows.
    /// 📄 docs/blueprints/DESIGN_Behavior_Self_Registration.md §10c.
    /// </summary>
    public static class BehaviorPickerTree
    {
        /// <summary>The folder every picker opens by default.</summary>
        public const string LibraryFolder = "Library";

        /// <summary>One folder: its sub-folders (in picker order) and its behaviours (by name).</summary>
        public sealed class Folder
        {
            public string Name { get; }
            public string Path { get; }
            public List<Folder> Folders { get; } = new();
            public List<BehaviorChoice> Choices { get; } = new();

            internal Folder(string name, string path) { Name = name; Path = path; }
        }

        /// <summary>The tree of <paramref name="choices"/> (pure — the drawing below only walks it).</summary>
        public static Folder Build(IEnumerable<BehaviorChoice> choices)
        {
            var root = new Folder("", "");
            foreach (var c in choices)
            {
                var folder = root;
                string group = string.IsNullOrWhiteSpace(c.Group) ? BehaviorChoice.OtherGroup : c.Group;
                foreach (var part in group.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var next = folder.Folders.FirstOrDefault(f => f.Name == part);
                    if (next == null)
                    {
                        next = new Folder(part, folder.Path.Length == 0 ? part : folder.Path + "/" + part);
                        folder.Folders.Add(next);
                    }
                    folder = next;
                }
                folder.Choices.Add(c);
            }
            Sort(root, top: true);
            return root;
        }

        private static void Sort(Folder f, bool top)
        {
            f.Folders.Sort((a, b) =>
            {
                int ra = top ? Rank(a.Name) : 1, rb = top ? Rank(b.Name) : 1;
                return ra != rb ? ra.CompareTo(rb) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            f.Choices.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var sub in f.Folders) Sort(sub, top: false);
        }

        private static int Rank(string topFolder)
            => topFolder == LibraryFolder ? 0 : topFolder == BehaviorChoice.OtherGroup ? 2 : 1;

        /// <summary>⭐ A combo labelled <paramref name="label"/> showing <paramref name="preview"/>; its popup is the folder tree.
        /// Returns <c>true</c> and the name in <paramref name="picked"/> when the user picks a behaviour.
        /// ⭐ <c>CE-1327</c> — the popup opens with a SEARCH box focused: typing narrows the tree to a flat list of matches (every word must
        /// appear in the name, the technology or the folder), Enter picks the first. Empty search = the tree, as before.</summary>
        public static bool Combo(string label, string preview, IReadOnlyList<BehaviorChoice> choices, string? current, out string picked)
        {
            picked = "";
            uint key = ImGui.GetID(label);
            if (!ImGui.BeginCombo(label, preview, ImGuiComboFlags.HeightLarge)) return false;
            if (ImGui.IsWindowAppearing()) { Searches[key] = ""; ImGui.SetKeyboardFocusHere(); }
            string search = Searches.TryGetValue(key, out var s) ? s : "";
            ImGui.SetNextItemWidth(-1f);
            bool enter = ImGui.InputTextWithHint("##search", "search...", ref search, 128, ImGuiInputTextFlags.EnterReturnsTrue);
            Searches[key] = search;
            bool done;
            if (search.Trim().Length > 0)
            {
                var hits = Matches(choices, search);
                done = false;
                if (hits.Count == 0) ImGui.TextDisabled("no behaviour matches");
                foreach (var c in hits)
                {
                    if (ImGui.Selectable($"{c.Label}##{c.Name}", c.Name == current)) { picked = c.Name; done = true; }
                    ImGui.SameLine();
                    ImGui.TextDisabled(c.Group);
                }
                if (enter && hits.Count > 0 && !done) { picked = hits[0].Name; done = true; }
                if (done) ImGui.CloseCurrentPopup();
            }
            else
            {
                var root = Build(choices);
                string? currentPath = current == null ? null : choices.FirstOrDefault(c => c.Name == current)?.Group;
                done = DrawFolder(root, current, currentPath, ref picked);
            }
            ImGui.EndCombo();
            return done;
        }

        private static readonly Dictionary<uint, string> Searches = new();

        /// <summary>⭐ <c>CE-1327</c> — the choices a search keeps, in picker order (name first): every whitespace-separated word must appear,
        /// case-insensitive, in the name, the label (its technology) or the folder. Pure.</summary>
        public static IReadOnlyList<BehaviorChoice> Matches(IEnumerable<BehaviorChoice> choices, string search)
        {
            var words = search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return choices
                .Where(c => words.All(w => c.Label.Contains(w, StringComparison.OrdinalIgnoreCase)
                                           || c.Group.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(c => c.Name.StartsWith(words.FirstOrDefault() ?? "", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool DrawFolder(Folder folder, string? current, string? currentPath, ref string picked)
        {
            bool done = false;
            foreach (var sub in folder.Folders)
            {
                bool open = sub.Path == LibraryFolder
                         || (currentPath != null && (currentPath == sub.Path || currentPath.StartsWith(sub.Path + "/", StringComparison.Ordinal)));
                ImGui.SetNextItemOpen(open, ImGuiCond.Appearing);
                if (ImGui.TreeNodeEx(sub.Name + "##" + sub.Path, ImGuiTreeNodeFlags.SpanAvailWidth))
                {
                    done |= DrawFolder(sub, current, currentPath, ref picked);
                    ImGui.TreePop();
                }
            }
            foreach (var c in folder.Choices)
            {
                bool selected = c.Name == current;
                if (ImGui.Selectable($"{c.Label}##{c.Name}", selected)) { picked = c.Name; done = true; }
                if (selected) ImGui.SetItemDefaultFocus();
            }
            return done;
        }
    }
}
