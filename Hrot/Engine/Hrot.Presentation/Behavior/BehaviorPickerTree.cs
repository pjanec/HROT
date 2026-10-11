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
        /// Returns <c>true</c> and the name in <paramref name="picked"/> when the user picks a behaviour.</summary>
        public static bool Combo(string label, string preview, IReadOnlyList<BehaviorChoice> choices, string? current, out string picked)
        {
            picked = "";
            if (!ImGui.BeginCombo(label, preview)) return false;
            var root = Build(choices);
            string? currentPath = current == null ? null : choices.FirstOrDefault(c => c.Name == current)?.Group;
            bool done = DrawFolder(root, current, currentPath, ref picked);
            ImGui.EndCombo();
            return done;
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
