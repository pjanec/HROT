using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// <b><c>UXI-11</c> slice <c>S-1</c>'s gate, as a rail: no host with an ECS world keeps a second
/// selection store.</b>
///
/// <para>📄 <c>docs/UX/UX_Feature_Selection.md</c> §2.7.4 — <em>"<c>DefaultSelectionState</c>'s own
/// <c>HashSet</c> becomes <c>EcsSelectionState</c>, a read-through"</em>.</para>
///
/// <para>⭐⭐⭐ <b>Why a source scan and not a unit test.</b> The defect is not a wrong value — it is a
/// second <em>place</em> a value can live. 🔴 Measured <c>2026-09-20</c>: <c>CgfSubsystem</c> and
/// <c>EditorSubsystem</c> each constructed a <c>DefaultSelectionState</c> while
/// <c>SelectionInteractionSystem</c> wrote the <c>SelectionState</c> component, and the ~8 000-test
/// suite was green throughout — because every test used one store or the other, never both. ⛔ No
/// behavioural assertion catches a construction site; only looking for it does.</para>
///
/// <para>⚠ <b><c>DefaultSelectionState</c> is NOT banned.</b> It is the legitimate implementation for a
/// host with no world — §2.7.1's <c>DdsBackedSelectionState</c> (ExCon) is the same shape — and the
/// cheap fake for tests. ⭐ What is banned is a host that HAS a world holding one anyway: that is the
/// silent-default shape, <em>"a production caller that HAS a dependency must pass it"</em>.</para>
/// </summary>
public sealed class NoProductionHostKeepsAParallelSelectionStoreTests
{
    /// <summary>
    /// Trees searched. ⚠ <c>FDP/Examples</c> is excluded deliberately — <c>CarKinem</c> is a
    /// world-less sample and its adapter is a correct non-ECS implementation.
    /// </summary>
    private static readonly string[] ProductionTrees = { "Hrot", "Stride" };

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "docs")) &&
                Directory.Exists(Path.Combine(dir, "FDP")) &&
                Directory.Exists(Path.Combine(dir, "Hrot")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("repo root not found from " + AppContext.BaseDirectory);
    }

    private static bool IsProductionFile(string path)
    {
        var p = path.Replace('\\', '/');
        if (p.Contains("/obj/") || p.Contains("/bin/")) return false;
        // ⭐ Tests may construct one freely -- it is the in-memory fake.
        if (p.Contains(".Tests/") || p.Contains(".Tests.")) return false;
        if (p.Contains("/Examples/")) return false;
        return true;
    }

    [Fact]
    public void NoProductionHostConstructsTheInMemorySelectionStore()
    {
        var root      = RepoRoot();
        var offenders = new List<string>();

        foreach (var tree in ProductionTrees)
        {
            var treeDir = Path.Combine(root, tree);
            if (!Directory.Exists(treeDir)) continue;

            foreach (var file in Directory.EnumerateFiles(treeDir, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsProductionFile(file)) continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!lines[i].Contains("new DefaultSelectionState")) continue;
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A host with an ECS world is holding a second selection store. Use " +
            "Hrot.ScenarioEditor.Selection.EcsSelectionState instead (UXI-11 S-1, " +
            "UX_Feature_Selection.md §2.7.4). Sites:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-301</c>'s gate: the AI editors' entity cell is a PROJECTION, so exactly ONE
    /// production site may assign it.</b>
    /// 📄 <c>docs/blueprints/DESIGN_Editor_Entity_Selection_Source.md</c> §2.
    ///
    /// <para>⭐ <b>Same argument as the rail above, on a different cell.</b> The defect is not a wrong
    /// value — it is a second <em>place</em> the entity can be written from, which is what turns a view
    /// back into a store. ⛔ No behavioural assertion catches a second writer: both writers would agree
    /// most of the time, and disagree exactly when it matters.</para>
    ///
    /// <para>⭐⭐ <b>The permitted sites are the two HOST forwards to the pack</b>
    /// (<c>AiEntitySelection = e =&gt; _sharedEntitySelection.Selected = e</c>, the editor's and CGF's),
    /// which are the sink <c>SelectionNotificationSystem</c> drives — <b>plus
    /// <c>EditorSelectionStore</c>'s own delegating setter.</b> ⚠ The exact FILE SET is asserted, not a
    /// count, ⛔ so this reddens if a host DROPS its forward as well as if a third writer appears.
    /// 📌 <c>R-67</c>: the control for a forwarding rail is on the site that forwards.</para>
    ///
    /// <para>⭐⭐⭐ <b>And the delegating setter is a CONDUIT, so the second assertion closes it</b>: no
    /// production code may write <c>store.SelectedEntity</c> directly. 📐 Measured — the only such
    /// writer was <c>CallbackSelectionBridge</c>, which <c>CE-300</c> deleted. ⛔ Excluding the conduit
    /// from the scan instead would hide exactly the route a second writer would take.</para>
    ///
    /// <para>⚠ <b>What this CANNOT see</b> *(say which layer is faked)*: that the sink is reached at
    /// runtime. ⭐ That is
    /// <c>SelectionInteractionSystemTests.ASelectionFromANonMapCause_ReachesTheAiEditorsEntityCell</c>'s
    /// job, and the two together are the claim.</para>
    /// </summary>
    [Fact]
    public void OnlyTheHostForwardWritesTheAiEditorsEntityCell()
    {
        var root    = RepoRoot();
        var writers = new List<string>();

        foreach (var tree in ProductionTrees)
        {
            var treeDir = Path.Combine(root, tree);
            if (!Directory.Exists(treeDir)) continue;

            foreach (var file in Directory.EnumerateFiles(treeDir, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsProductionFile(file)) continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    // ⚠ Comments and doc comments are skipped: this file's own prose names the member,
                    //   and so does the cell's header. ⛔ A text rail that counts prose is a rail that
                    //   reddens on documentation.
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*"))
                        continue;

                    // ⭐ An ASSIGNMENT to the cell, not a read: `.Selected =` but not `==`.
                    int at = line.IndexOf(".Selected", StringComparison.Ordinal);
                    while (at >= 0)
                    {
                        var rest = line[(at + ".Selected".Length)..].TrimStart();
                        if (rest.StartsWith("=", StringComparison.Ordinal) &&
                            !rest.StartsWith("==", StringComparison.Ordinal))
                        {
                            writers.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                            break;
                        }
                        at = line.IndexOf(".Selected", at + 1, StringComparison.Ordinal);
                    }
                }
            }
        }

        // ⭐⭐ THREE sites are correct, and naming the third is the point: EditorSelectionStore's own
        //    `SelectedEntity` setter DELEGATES to the cell. It is a CONDUIT, not a writer — ⛔ but it is
        //    a public setter, so anything holding a store could write the cell through it and quietly
        //    make it a store again. ⇒ that hole is closed by the SECOND assertion below, not by
        //    excluding the line here: a rail that hides a conduit cannot see it being used.
        var expected = new[]
        {
            "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs",
            "Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs",
            "Hrot/Editor/Hrot.Editor.AiShared/Selection/EditorSelectionStore.cs",
        };
        var actualFiles = writers
            .Select(w => w[..w.LastIndexOf(':')].Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            actualFiles.SequenceEqual(expected.OrderBy(f => f, StringComparer.Ordinal)),
            "The AI editors' entity cell (SharedEntitySelection.Selected) must be assigned by exactly " +
            "three production sites: the editor's and CGF's forward of MapInteractionContext" +
            ".AiEntitySelection (which SelectionNotificationSystem drives), plus EditorSelectionStore's " +
            "own delegating setter. More means a second writer turned the projection back into a " +
            "store; fewer means a host dropped its forward (CE-300/CE-301, " +
            "DESIGN_Editor_Entity_Selection_Source.md §2). Sites:" +
            Environment.NewLine + string.Join(Environment.NewLine, writers));

        // ⭐⭐⭐ THE CONDUIT IS UNUSED IN PRODUCTION, and that is the half that actually keeps the cell a
        //    projection. 📐 Measured 2026-09-21: the only production writer of `store.SelectedEntity`
        //    was CallbackSelectionBridge, which CE-300 deleted. ⚠ Tests and the smoke harness write it
        //    freely — they are excluded by IsProductionFile, and that is correct: a rail needs to be
        //    able to place an entity without standing up a notification pipeline.
        var conduitUsers = new List<string>();
        foreach (var tree in ProductionTrees)
        {
            var treeDir = Path.Combine(root, tree);
            if (!Directory.Exists(treeDir)) continue;

            foreach (var file in Directory.EnumerateFiles(treeDir, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsProductionFile(file)) continue;
                // ⛔ The declaring file itself is where the property LIVES.
                if (file.Replace('\\', '/').EndsWith("Selection/EditorSelectionStore.cs",
                        StringComparison.Ordinal))
                    continue;

                var text = File.ReadAllText(file);
                // ⭐ A file that never names the type cannot be writing one's property.
                if (!text.Contains("EditorSelectionStore", StringComparison.Ordinal)) continue;

                var lines = text.Split('\n');

                // ⭐⭐⭐ THE RECEIVER SET, DERIVED FROM DECLARATIONS — not guessed from names.
                // 🔴 Two weaker discriminators were tried and BOTH were wrong, in opposite directions:
                //    a blacklist of variable names (fragile — it passes or fails on what someone called
                //    a local), then a file-level filter (over-scoped — the two host files DO name the
                //    type, so their unrelated `_fdpInspectorState.SelectedEntity` writes were flagged).
                // ⚠ `SelectedEntity` is a member name at least THREE types carry here. ⇒ the only
                //    honest text answer is to learn which identifiers in THIS file are stores, and flag
                //    writes through those alone. 📌 CLAUDE.md: text cannot tell a real reference from a
                //    same-named symbol — so narrow the text until it can.
                var storeNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var raw in lines)
                {
                    var m = System.Text.RegularExpressions.Regex.Matches(
                        raw,
                        @"(?:EditorSelectionStore[?\s]+(\w+)\s*[=;)]"        // typed declaration/param
                        + @"|(\w+)\s*=\s*new\s+(?:[\w.]*\.)?EditorSelectionStore)"); // var x = new ...
                    foreach (System.Text.RegularExpressions.Match hit in m)
                    {
                        var name = hit.Groups[1].Success ? hit.Groups[1].Value : hit.Groups[2].Value;
                        if (!string.IsNullOrEmpty(name)) storeNames.Add(name);
                    }
                }
                if (storeNames.Count == 0) continue;

                for (int i = 0; i < lines.Length; i++)
                {
                    var trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*"))
                        continue;

                    foreach (var name in storeNames)
                    {
                        int at = lines[i].IndexOf(name + ".SelectedEntity", StringComparison.Ordinal);
                        if (at < 0) continue;
                        // ⚠ Must be the WHOLE identifier, not a suffix of a longer one.
                        if (at > 0 && (char.IsLetterOrDigit(lines[i][at - 1]) || lines[i][at - 1] == '_'))
                            continue;
                        var rest = lines[i][(at + name.Length + ".SelectedEntity".Length)..].TrimStart();
                        if (rest.StartsWith("=", StringComparison.Ordinal) &&
                            !rest.StartsWith("==", StringComparison.Ordinal))
                        {
                            conduitUsers.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                            break;
                        }
                    }
                }
            }
        }

        Assert.True(
            conduitUsers.Count == 0,
            "Production code is writing EditorSelectionStore.SelectedEntity directly. That is a SECOND " +
            "writer of the AI editors' entity cell, which turns the projection back into a store — the " +
            "cell's one writer is SelectionNotificationSystem, via MapInteractionContext" +
            ".AiEntitySelection (CE-301). Sites:" +
            Environment.NewLine + string.Join(Environment.NewLine, conduitUsers));
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-11</c> slice <c>S-2</c>'s gate: NOTHING hand-writes the <c>SelectionState</c>
    /// component except the view.</b>
    ///
    /// <para>📄 §2.7.3 rule 1 — one writer. 🔴 Measured <c>2026-09-20</c>, BEFORE <c>S-2</c>: four
    /// production sites each hand-rolled the same clear-loop-then-set —
    /// <c>EditorSubsystem</c>'s <c>GlobalActionIds.Select</c> handler, <c>EditorSubsystem.SetSelection2D</c>,
    /// <c>IgApplication.SelectEntityOnMap</c>, and (until <c>S-1</c>) <c>SelectionInteractionSystem</c>.
    /// ⚠ §2.7.4's delete-list named THREE; <c>SetSelection2D</c> was the fourth and it had been
    /// missed — that is why this is a scan and not a checklist.</para>
    ///
    /// <para>⚠ <b>The exemption is a WHITELIST, not a pattern.</b> ⛔ Any new file that writes the
    /// component fails this, including a "small" one — that is the point. ⭐ If a host genuinely needs
    /// to write it, the answer is to publish a <c>SelectionChangeRequest</c>.</para>
    /// </summary>
    [Fact]
    public void OnlyTheViewWritesTheSelectionStateComponent()
    {
        // ⭐ The ONE implementation. It uses an alias (SelectionStateComponent) precisely because the
        //   view's own type name would collide -- so the literal below cannot match it by accident.
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "EcsSelectionState.cs" };

        var root      = RepoRoot();
        var offenders = new List<string>();

        foreach (var tree in ProductionTrees)
        {
            var treeDir = Path.Combine(root, tree);
            if (!Directory.Exists(treeDir)) continue;

            foreach (var file in Directory.EnumerateFiles(treeDir, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsProductionFile(file)) continue;
                if (allowed.Contains(Path.GetFileName(file))) continue;
                // ⚠ Scoped to Hrot.IG.Components.SelectionState. The Blueprint/BTree/HSM editors have
                //   their own unrelated SelectionState -- a NODE selection, a different concept
                //   (the ".*Selection.*" trap: ~60 of 67 classes are not this one).
                var p = file.Replace('\\', '/');
                if (p.Contains("/Blueprints/") || p.Contains("/NodeEdit") || p.Contains("/AI/")) continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var l = lines[i];
                    if (l.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    if (!l.Contains("new SelectionState {") && !l.Contains("new SelectionState{")) continue;
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "The SelectionState component is being written outside EcsSelectionState. Publish a " +
            "SelectionChangeRequest instead (UXI-11 S-2, UX_Feature_Selection.md §2.7.3 rule 1). Sites:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>NO HOST BUILDS ITS OWN SELECTION — <c>MapInteractionPack</c> BUILDS IT FOR ALL FIVE.</b>
    /// 🔒 User, <c>2026-09-20</c>: <i>"we want to unify across host also the bootstrap code as far as
    /// possible, including this entity selection stuff."</i>
    ///
    /// <para>📐 The checkable form: <c>new EcsSelectionState(</c> and
    /// <c>new SelectionInteractionSystem(</c> appear in production in exactly ONE place — the pack.
    /// ⛔ A host that constructs either gets a SECOND selection over the same world, which is the
    /// split <c>UXI-11</c> spent four slices removing.</para>
    ///
    /// <para>⚠⚠ <b>This rail REPLACED an earlier one that asserted the opposite</b> — that every host
    /// which constructs <c>SelectionInteractionSystem</c> also constructs <c>EcsSelectionState</c>.
    /// 🔴 That was right for the world where each host wired its own and became FALSE the moment the
    /// pack took over; it failed on the very commit that unified them. ⭐ The invariant it was reaching
    /// for is stronger and simpler: <b>nobody wires their own.</b></para>
    /// </summary>
    [Fact]
    public void OnlyTheSharedPackConstructsTheSelection()
    {
        var root      = RepoRoot();
        var offenders = new List<string>();
        var packHits  = 0;

        foreach (var tree in ProductionTrees)
        {
            var treeDir = Path.Combine(root, tree);
            if (!Directory.Exists(treeDir)) continue;

            foreach (var file in Directory.EnumerateFiles(treeDir, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsProductionFile(file)) continue;
                var name = Path.GetFileName(file);

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var l = lines[i].TrimStart();
                    if (l.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (!l.Contains("new Hrot.ScenarioEditor.Selection.EcsSelectionState(") &&
                        !l.Contains("new EcsSelectionState(") &&
                        !l.Contains("new SelectionInteractionSystem(") &&
                        !l.Contains("new Hrot.ScenarioEditor.Systems.SelectionInteractionSystem(")) continue;

                    if (name == "MapInteractionPack.cs") { packHits++; continue; }
                    // ⚠ TWO named `?? new …` fallbacks survive, both for callers that construct the
                    //   object directly in a test. ⛔ Neither is reachable in production, because
                    //   MapInteractionPack always passes the instance it built:
                    //     · SimHostVisualization — SimHostApp passes the pack's view and gesture system;
                    //     · SelectionInteractionSystem — the pack passes the view it just constructed.
                    // ⭐ Keyed on the `??`, so REPLACING either fallback with an unconditional `new`
                    //   fails this rail — which is the case that would actually make two selections.
                    if (l.Contains("??") &&
                        (name == "SimHostVisualization.cs" || name == "SelectionInteractionSystem.cs"))
                        continue;

                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                }
            }
        }

        // ⚠ Anti-vacuity: the pack must actually build both, or this scan is matching nothing.
        Assert.True(packHits >= 2,
            $"expected MapInteractionPack to construct the view AND the gesture system; found {packHits}");

        Assert.True(
            offenders.Count == 0,
            "A host is constructing its own selection instead of taking MapInteractionPack's. Two " +
            "instances over one world are two selections (UXI-11, UX_Feature_Selection.md §2.7.10). " +
            "Sites:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE MARQUEE BELONGS TO EVERY HOST WITH A 2-D MAP — and to exactly one place.</b>
    /// 🔒 User ruling, <c>2026-09-20</c>: <i>"any perspective showing 2d map should support marquee and
    /// rubberband, not just editor and cgf."</i> 📄 <c>UX_Feature_Selection.md</c> §2.7.16.
    ///
    /// <para>🔴 <b>What it pins, measured before the fix:</b> the box-select LOGIC ran on all five hosts
    /// — <c>SelectionInteractionSystem</c> tracks the box and commits it — but only <c>EditorSubsystem</c>
    /// and <c>ReplayBrowserSubsystem</c> ever registered a <c>RubberBandGizmo</c>. ⇒ on IG, SimHost and
    /// CGF the operator dragged a box that WORKED and was INVISIBLE. ⛔ That is the worst shape of gap:
    /// not a missing feature, a feature with no feedback.</para>
    ///
    /// <para>⚠ <b>Why "exactly one" and not "at least one":</b> moving it into the pack without removing
    /// the two host registrations would draw the marquee TWICE on the hosts that already had it — and a
    /// doubled overlay is the kind of defect that looks like a rendering artefact rather than a wiring
    /// one. ⛔ Red-proof: restore either host's <c>RegisterGlobal(new RubberBandGizmo(</c> and this
    /// reddens.</para>
    /// </summary>
    [Fact]
    public void OnlyTheSharedPackRegistersTheMarquee()
    {
        var root      = RepoRoot();
        var offenders = new List<string>();
        var packHits  = 0;

        foreach (var tree in ProductionTrees)
        {
            var treeDir = Path.Combine(root, tree);
            if (!Directory.Exists(treeDir)) continue;

            foreach (var file in Directory.EnumerateFiles(treeDir, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsProductionFile(file)) continue;
                var name  = Path.GetFileName(file);
                var lines = File.ReadAllLines(file);

                for (int i = 0; i < lines.Length; i++)
                {
                    var l = lines[i].TrimStart();
                    if (l.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (!l.Contains("new Hrot.ScenarioEditor.Gizmos.RubberBandGizmo(") &&
                        !l.Contains("new RubberBandGizmo(")) continue;

                    if (name == "MapInteractionPack.cs") { packHits++; continue; }
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                }
            }
        }

        // ⚠ Anti-vacuity: if the pack stopped registering it, every host loses the marquee silently —
        //   which is exactly the state this rail exists to prevent returning to.
        Assert.True(packHits == 1,
            $"MapInteractionPack must register the marquee exactly once; found {packHits}");

        Assert.True(
            offenders.Count == 0,
            "A host registers its own RubberBandGizmo. The pack registers it for every host now, so " +
            "this one draws the marquee TWICE (UX_Feature_Selection.md §2.7.16). Sites:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>EVERY PRODUCTION <c>DebugGizmoLayer</c> GETS A CAMERA — a map without one is DEAD, and
    /// silently.</b>
    ///
    /// <para>🔴 <b>Measured in the product <c>2026-09-20</c>, and it cost an operator four builds.</b>
    /// <c>CgfSubsystem.cs:1598</c> constructed the layer without <c>camera:</c>, so its <c>Camera2D</c>
    /// stayed <c>default</c> — <c>zoom=0, offset=(0,0)</c>. <c>Raylib.GetScreenToWorld2D</c> is
    /// <c>(screen − Offset) / Zoom + Target</c>, so it DIVIDED BY ZERO: every mouse position became
    /// <c>NaN</c>, every hit-test comparison <c>false</c>, and every click fell through to the canvas.
    /// 📐 The diagnostic that caught it read <c>frame=763 pickable=708</c> at <c>worldPos=(NaN,NaN)</c> —
    /// 708 pick boxes present and not one reachable.</para>
    ///
    /// <para>⛔ <b>Why nothing noticed for so long:</b> DRAWING is unaffected. The map looked perfect;
    /// only input was dead. ⚠ And the camera was two lines above the construction site — the
    /// silent-default shape exactly: a caller that HAD the dependency and did not pass it.</para>
    ///
    /// <para>⚠ The parameterless/no-camera form stays legal for headless rails and for a layer that only
    /// renders — this rail covers PRODUCTION construction sites, which are the ones an operator clicks
    /// on. ⛔ Red-proof: drop <c>camera:</c> from any host and this reddens naming the file and line.</para>
    /// </summary>
    [Fact]
    public void EveryProductionGizmoLayerIsGivenACamera()
    {
        var root      = RepoRoot();
        var offenders = new List<string>();
        var sites     = 0;

        // ⚠ Two spellings, scanned independently — a combined loop condition is how the first version of
        //   this rail threw: the second IndexOf ran with the first one's stale index.
        string[] patterns = { "new DebugGizmoLayer(", "new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(" };

        foreach (var tree in ProductionTrees)
        {
            var treeDir = Path.Combine(root, tree);
            if (!Directory.Exists(treeDir)) continue;

            foreach (var file in Directory.EnumerateFiles(treeDir, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsProductionFile(file)) continue;
                var text = File.ReadAllText(file);

                foreach (var pattern in patterns)
                {
                    int idx = text.IndexOf(pattern, StringComparison.Ordinal);
                    while (idx >= 0)
                    {
                        // ⭐ Take the BALANCED argument list, not one line: the CGF site that caused this
                        //   was one line, but the editor's and IG's span five, and a line-based scan
                        //   would have declared those clean without looking.
                        int open  = text.IndexOf('(', idx);
                        int depth = 0, end = open;
                        for (; end < text.Length; end++)
                        {
                            if (text[end] == '(') depth++;
                            else if (text[end] == ')') { depth--; if (depth == 0) break; }
                        }
                        if (end >= text.Length) break;

                        var args = text.Substring(open, end - open + 1);
                        sites++;

                        if (!args.Contains("camera:", StringComparison.Ordinal))
                        {
                            int line = 1;
                            for (int i = 0; i < idx; i++) if (text[i] == '\n') line++;
                            offenders.Add($"{Path.GetRelativePath(root, file)}:{line}");
                        }

                        idx = text.IndexOf(pattern, end, StringComparison.Ordinal);
                    }
                }
            }
        }

        // ⚠ Anti-vacuity: if the scan matches nothing the assertion below is meaningless.
        Assert.True(sites >= 3,
            $"expected to find production DebugGizmoLayer constructions; found {sites}");

        Assert.True(
            offenders.Count == 0,
            "A production map layer is built with NO CAMERA. Its Camera2D stays default (zoom=0), so " +
            "GetScreenToWorld2D divides by zero, every mouse position is NaN and NOTHING on that map " +
            "can be clicked — while drawing looks perfect. Sites:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// ⚠ <b>The negative control.</b> ⛔ A scan that finds nothing because it is looking in the wrong
    /// place passes exactly like a scan that finds nothing because the tree is clean. 📌 This is the
    /// <c>T-1</c> lesson — <em>"if it stays GREEN while the feature is BROKEN, THAT is the finding"</em>
    /// — applied before the fact.
    /// </summary>
    [Fact]
    public void TheScanActuallyReachesTheHostFilesItIsSupposedToGuard()
    {
        var root  = RepoRoot();
        var seen  = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tree in ProductionTrees)
        {
            var treeDir = Path.Combine(root, tree);
            if (!Directory.Exists(treeDir)) continue;
            foreach (var file in Directory.EnumerateFiles(treeDir, "*.cs", SearchOption.AllDirectories))
                if (IsProductionFile(file)) seen.Add(Path.GetFileName(file));
        }

        Assert.Contains("CgfSubsystem.cs", seen);
        Assert.Contains("EditorSubsystem.cs", seen);
        Assert.Contains("SelectionInteractionSystem.cs", seen);
    }
}
