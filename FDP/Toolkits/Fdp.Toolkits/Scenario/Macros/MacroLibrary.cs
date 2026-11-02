using System;
using System.Collections.Generic;
using System.IO;
using Fdp.Toolkit.Orchestration;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐ Macros S3a ④ (docs/DESIGN_Scenario_Macros.md §15) — where an expanding node reads a macro FILE by its path under <c>macros/</c>
    /// (<see cref="MacroInstance.Macro"/>). ⛔ <c>false</c> + a reason the status shows (<i>"macro 'x' not found under …"</i>) — never a silent miss.
    /// </summary>
    public interface IMacroLibrary
    {
        bool TryRead(string macroPath, out string json, out string why);
    }

    /// <summary>
    /// The files: each content root in order (a node's staging first, the content root after), through the ONE name resolver
    /// (<see cref="ContentLayout.ResolveScenarioDirectory"/>, the <c>macros/</c> arm, SR-173) — <c>{root}/macros/&lt;path&gt;/scenario.json</c>.
    /// </summary>
    public sealed class FileMacroLibrary : IMacroLibrary
    {
        private readonly IReadOnlyList<string> _roots;

        public FileMacroLibrary(params string[] roots)
        {
            if (roots is null || roots.Length == 0) throw new ArgumentException("a macro library needs a content root", nameof(roots));
            _roots = roots;
        }

        public IReadOnlyList<string> Roots => _roots;

        /// <summary>⭐ A node's library: its staging root first (where the orchestrator stages the macros package, SR-80 / SR-148), then the
        /// content source tree when the node runs inside one (the editor, <c>--mode all</c>, tests).</summary>
        public static FileMacroLibrary ForNode(string? stagingRoot)
        {
            var roots = new List<string>();
            if (!string.IsNullOrWhiteSpace(stagingRoot)) roots.Add(stagingRoot);
            if (ContentLayout.TryFindSourceTree() is { } tree && !roots.Contains(tree)) roots.Add(tree);
            if (roots.Count == 0) roots.Add(Environment.CurrentDirectory);
            return new FileMacroLibrary(roots.ToArray());
        }

        public bool TryRead(string macroPath, out string json, out string why)
        {
            json = "";
            if (string.IsNullOrWhiteSpace(macroPath)) { why = "the instance names no macro"; return false; }
            string name = ContentLayout.IsMacroName(macroPath) ? macroPath : ContentLayout.MacroName(macroPath);
            foreach (var root in _roots)
            {
                string file;
                try { file = Path.Combine(ContentLayout.ResolveScenarioDirectory(root, name), ContentLayout.ScenarioFileName); }
                catch (ArgumentException ex) { why = $"macro '{macroPath}': {ex.Message}"; return false; }
                if (!File.Exists(file)) continue;
                json = File.ReadAllText(file);
                why = "";
                return true;
            }
            why = $"macro '{macroPath}' not found under {string.Join(", ", _roots)}";
            return false;
        }
    }

    /// <summary>An in-memory library (tests, previews): path → file JSON.</summary>
    public sealed class InMemoryMacroLibrary : IMacroLibrary
    {
        public readonly Dictionary<string, string> Files = new(StringComparer.Ordinal);

        public bool TryRead(string macroPath, out string json, out string why)
        {
            if (Files.TryGetValue(macroPath, out json!)) { why = ""; return true; }
            json = "";
            why = $"macro '{macroPath}' not found";
            return false;
        }
    }

    /// <summary>
    /// ⭐ Macros S3a ④ — the WAIT of one expansion, on the instance entity (§15 E-5): the child requests it published and the ids it
    /// waits for. ⭐ <c>ScenarioLoadStep</c> ③ waits while any instance carries one; it is removed when every child EXISTS (no gap before
    /// condition ②). Transient: never saved, never recorded (a restore mid-expansion re-expands from the status).
    /// </summary>
    [Fdp.Core.DataPolicy(Fdp.Core.DataPolicy.Transient)]
    [Fdp.Core.ComponentId(Fdp.Core.GlobalComponentIds.MacroExpansionIntent)]
    public sealed class MacroExpansionIntent
    {
        /// <summary>The network ids of every child (all levels) still to appear on this node.</summary>
        public HashSet<int> Awaited { get; init; } = new();

        /// <summary>The tick the children were published (a wait limit counts from it).</summary>
        public uint PublishedAtTick { get; init; }

        /// <summary><c>true</c> once the child requests are out (before that the expansion has not run).</summary>
        public bool Published { get; init; }

        /// <summary>⭐ Macros S3a ⑤ (SR-180, SR-185) — the expanded children held back while their placement is asked; <c>null</c> = not asking.</summary>
        public IReadOnlyList<Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest>? Parked { get; init; }

        /// <summary>The <c>PlacementRequestBatch.RequestId</c> the parked children wait on.</summary>
        public long PlacementRequestId { get; init; }

        /// <summary>The expansion system's frame count when placement was asked (frames, not ticks: a load runs PAUSED).</summary>
        public long AskedAtFrame { get; init; }

        /// <summary>The top macro's <c>Placement.MaxShift</c>, kept for the local fallback.</summary>
        public float MaxShift { get; init; }

        /// <summary>⭐ Macros debt A3 (SR-157 / SR-180) — the parked children are a RECONCILE's (a moved / edited instance) and are applied to the
        /// existing entities by GUID, not published fresh; <see cref="ReconcileFrom"/> is the baseline it was computed against.</summary>
        public MacroExpandedWith? ReconcileFrom { get; init; }

        /// <summary>⭐ A3 — what a reconcile moves the instance TO (its <see cref="MacroInstance"/> and anchor when it asked).</summary>
        public MacroExpandedWith? ReconcileTo { get; init; }

        /// <summary>⭐ A3 — the placement request as sent (its slots carry <c>Self</c>), reused by the local fallback.</summary>
        public Fdp.Toolkit.Navigation.PlacementRequestBatch? Ask { get; init; }
    }

    /// <summary>
    /// ⭐ Macros S3a ⑥ — what a done instance last expanded WITH (its <see cref="MacroInstance"/> and its anchor), on the node that owns it:
    /// an edit is detected against it and the reconcile compares the OLD args' child set with the NEW (SR-150 / SR-172). Transient — after a
    /// restore the instance has none, so it is not reconciled until it expands again.
    /// </summary>
    [Fdp.Core.DataPolicy(Fdp.Core.DataPolicy.Transient)]
    [Fdp.Core.ComponentId(Fdp.Core.GlobalComponentIds.MacroExpandedWith)]
    public sealed class MacroExpandedWith
    {
        public MacroInstance Instance { get; init; } = new();
        public Fdp.Core.SimTransform Anchor { get; init; }
    }
}
