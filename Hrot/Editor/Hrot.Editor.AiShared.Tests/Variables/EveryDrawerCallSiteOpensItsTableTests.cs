using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 96 (<c>96a</c>) — <c>ComponentEditDrawer.DrawEditNode</c> must be called inside a
/// two-column table, and this asserts it AT EVERY PRODUCTION CALL SITE.</b>
///
/// <para>🔴🔴 <b>The defect.</b> The drawer's own doc-comment is the contract — <i>"Must be called
/// inside a two-column <c>BeginTable</c>/<c>EndTable</c> block"</i> — and <c>DrawLeafNode</c>'s first
/// statement is <c>TableNextRow()</c>. ⛔ <c>VariableEditModal.Draw</c> called it between two
/// <c>Separator</c>s with no table, which is BOTH reported failures at once: <i>"Edit value…"</i> drew
/// nothing *(its filtered document had zero children, so <c>TableNextRow</c> was never reached)* and
/// <i>"Properties…"</i> <b>aborted the editor natively</b> on the first row.</para>
///
/// <para>⭐⭐⭐ <b>Why the FAMILY and not the one file.</b> 📐 The graph plus a grep cross-check
/// enumerate <b>six</b> production call sites; ⭐ <b>five were already correct</b> and one was not.
/// ⇒ a rail naming only the modal would let the seventh caller repeat it. 📌 <c>R-74</c>: only an
/// enumeration can say <i>"and no others"</i>.</para>
///
/// <para>⛔⛔ <b>WHAT THIS RAIL CANNOT DO, stated plainly.</b> 📌 <c>R-21</c>/<c>R-62</c>: no headless
/// rail can drive ImGui, so <b>the DRAW ITSELF IS UNRAILED</b> — this asserts the SHAPE of the call
/// site in the sources, not that a row appears on screen. ⭐ It is the strongest thing available at
/// this layer and it is the layer six green batches kept shipping defects into; ⚠ it is not proof the
/// dialog renders, and the report says so.</para>
/// </summary>
public sealed class EveryDrawerCallSiteOpensItsTableTests
{
    /// <summary>
    /// ⭐ Every production file that calls <c>ComponentEditDrawer.DrawEditNode</c>.
    ///
    /// <para>⛔ <c>ComponentEditDrawer.cs</c> itself is excluded — its two calls are the drawer's own
    /// recursion, already inside whatever table the caller opened. ⛔ <c>ImGuiPropertyTreeAdapter.cs</c>
    /// is excluded because its <c>DrawEditNode</c> is an unrelated private method in GizmoMap that
    /// happens to share the name — ⚠ it is NOT this drawer, and the enumeration below is asserted to
    /// stay complete so a new file cannot slip past.</para>
    /// </summary>
    private static readonly string[] CallSiteFiles =
    {
        "FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditWindow.cs",
        "FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs",
        "FDP/Engine/Fdp.Presentation/ImGui/Panels/ReplayBrowser/ReplaySearchPanel.cs",
        // ⭐ S2 (BP-399): was Windows/InspectorWindow.cs — the node arms were EXTRACTED to a
        //   Details view (§7.6 ②). ⚠ Still TWO drawer calls in one file, both in tables.
        "Hrot/Editor/Hrot.Editor.AiShared/Shell/NodePropertiesDetailsView.cs",
        "Hrot/Editor/Hrot.Editor.AiShared/Variables/VariableEditModal.cs",
    };

    /// <summary>⭐ How far a <c>BeginTable</c>/<c>EndTable</c> may sit from the call it wraps. ⚠ Every
    /// real site is within a handful of lines; the slack is for comments, not for a different block.</summary>
    private const int Window = 40;

    // ══ the contract still says what we think it says ════════════════════════

    /// <summary>
    /// ⭐⭐ <b>The premise, asserted rather than assumed.</b> ⛔ If the drawer ever stops requiring a
    /// table, every assertion below becomes ceremony — this is the line that would tell us.
    /// </summary>
    [Fact]
    public void TheDrawerStillDocumentsThatItNeedsATable()
    {
        var drawer = RepoFiles.Read("FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditDrawer.cs");

        Assert.Contains("Must be called inside a two-column", drawer, StringComparison.Ordinal);
        Assert.Contains("TableNextRow", drawer, StringComparison.Ordinal);
    }

    // ══ the enumeration is complete ══════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>No SEVENTH call site.</b> 📌 A per-file rail cannot see a file it does not name, and the
    /// defect this batch fixes was exactly <i>"one caller nobody checked"</i>. ⇒ the sweep is over the
    /// repository, and a new caller fails here until it is listed — and therefore checked.
    /// </summary>
    [Fact]
    public void TheCallSiteEnumerationIsComplete()
    {
        var found = SweepForCallSites().ToArray();

        Assert.Equal(
            CallSiteFiles.OrderBy(f => f, StringComparer.Ordinal).ToArray(),
            found.OrderBy(f => f, StringComparer.Ordinal).ToArray());
    }

    // ══ THE RAIL ═════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Every call is inside a table, and the table is closed.</b>
    ///
    /// <para>🔴 <b>RED before this batch</b> on <c>VariableEditModal.cs</c> alone — which is precisely
    /// the shape the user hit: five callers correct, one not, and the one is the newest.</para>
    /// </summary>
    [Fact]
    public void EveryProductionCallToTheDrawerIsWrappedInATable()
    {
        var failures = new List<string>();

        foreach (var file in CallSiteFiles)
        {
            var lines = RepoFiles.Lines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                if (!IsCallSite(lines[i])) continue;

                bool opened = Enumerable.Range(Math.Max(0, i - Window), Math.Min(Window, i))
                    .Any(j => lines[j].Contains("BeginTable(", StringComparison.Ordinal));
                bool closed = Enumerable.Range(i + 1, Math.Min(Window, lines.Length - i - 1))
                    .Any(j => lines[j].Contains("EndTable(", StringComparison.Ordinal));

                if (!opened || !closed)
                    failures.Add($"{file}:{i + 1} — " +
                                 (opened ? "" : "no BeginTable above; ") +
                                 (closed ? "" : "no EndTable below; ") +
                                 "ComponentEditDrawer.DrawEditNode calls TableNextRow, which aborts " +
                                 "natively outside a table.");
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// ⭐⭐ <b>And the modal mirrors the REFERENCE caller specifically</b> — 📌 the handoff named
    /// <c>ComponentEditWindow</c> and asked which one was mirrored. ⭐ Two columns, and the rebuild
    /// asked BEFORE the table: ⛔ inside it, <c>DrawEditNode</c>'s own <c>RebuildRequired</c> early
    /// return would draw an empty table for ever.
    /// </summary>
    [Fact]
    public void TheModalMirrorsTheReferenceCaller()
    {
        var modal = RepoFiles.Read("Hrot/Editor/Hrot.Editor.AiShared/Variables/VariableEditModal.cs");

        Assert.Contains("BeginTable(TableId, 2", modal, StringComparison.Ordinal);
        Assert.Contains("TableSetupColumn(\"Property\"", modal, StringComparison.Ordinal);
        Assert.Contains("TableSetupColumn(\"Value\"",    modal, StringComparison.Ordinal);

        int rebuildAt = modal.IndexOf("RebuildDocument()", StringComparison.Ordinal);
        int tableAt   = modal.IndexOf("BeginTable(",       StringComparison.Ordinal);
        Assert.True(rebuildAt > 0, "the modal never rebuilds a stale document");
        Assert.True(rebuildAt < tableAt,
            "RebuildDocument must run BEFORE BeginTable, as ComponentEditWindow.DrawClientArea does.");
    }

    /// <summary>⭐ Three modals draw every frame, so the table id is instance-scoped for the same
    /// reason <c>PopupId</c> is. ⚠ Cheap, and this repo has paid for id confusion before.</summary>
    [Fact]
    public void TheTableIdIsInstanceScoped()
    {
        var a = new Hrot.Editor.AiShared.Variables.VariableEditModal(
            Binder(), () => Hrot.Editor.AiShared.Variables.VariableRunState.Planning, "btree");
        var b = new Hrot.Editor.AiShared.Variables.VariableEditModal(
            Binder(), () => Hrot.Editor.AiShared.Variables.VariableRunState.Planning, "hsm");

        Assert.NotEqual(a.TableId, b.TableId);
        Assert.NotEqual(a.PopupId, b.PopupId);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Hrot.Editor.AiShared.Variables.VariableEditGestureBinder Binder()
        => new(
            new Hrot.Editor.AiShared.Variables.VariableEditLauncher(
                new StructEdit.Reflection.ComponentEditServiceBuilder().Build()),
            entryResolver: _ => null,
            runState:      () => Hrot.Editor.AiShared.Variables.VariableRunState.Planning);

    /// <summary>⭐ A call, not the declaration and not a doc-comment mention.</summary>
    private static bool IsCallSite(string line)
        => line.Contains(".DrawEditNode(", StringComparison.Ordinal)
        && !line.TrimStart().StartsWith("///", StringComparison.Ordinal)
        && !line.TrimStart().StartsWith("//",  StringComparison.Ordinal);

    /// <summary>
    /// ⭐ Sweeps the repository for files that call the drawer, so <see cref="CallSiteFiles"/> cannot
    /// silently go stale. ⛔ Excludes the drawer's own recursion and GizmoMap's same-named private
    /// method — both stated in <see cref="CallSiteFiles"/>' remarks.
    /// </summary>
    private static IEnumerable<string> SweepForCallSites()
    {
        var root = System.IO.Path.GetDirectoryName(
            RepoFiles.Find("Hrot/Editor/Hrot.Editor.AiShared/Variables/VariableEditModal.cs"))!;
        for (int i = 0; i < 4; i++) root = System.IO.Directory.GetParent(root)!.FullName;

        foreach (var path in System.IO.Directory.EnumerateFiles(root, "*.cs",
                     System.IO.SearchOption.AllDirectories))
        {
            var rel = System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');
            if (rel.Contains("/obj/", StringComparison.Ordinal)) continue;
            if (rel.Contains("/bin/", StringComparison.Ordinal)) continue;
            if (rel.EndsWith("ComponentEditDrawer.cs",     StringComparison.Ordinal)) continue;
            if (rel.EndsWith("ImGuiPropertyTreeAdapter.cs", StringComparison.Ordinal)) continue;
            if (rel.Contains(".Tests/", StringComparison.Ordinal)) continue;
            // ⭐⭐ Batch 101 — `tools/` is EVIDENCE, not production. 📌 `R-124`'s probes render real
            //    sessions to prove a diagnosis *(tools/ui-probe/…)*, so they legitimately call
            //    DrawEditNode — ⛔ but a probe that ships a screenshot is not a call site this rail is
            //    about, and counting it would make the enumeration answer a different question.
            // ⚠ ARGUED, not silenced: the rail's subject is "every PRODUCTION call site opens its
            //    table" *(see the class remark)*, and `tools/` is neither shipped nor referenced by any
            //    assembly. ⭐ If a probe ever moves into a product, it leaves `tools/` and reappears here.
            if (rel.StartsWith("tools/", StringComparison.Ordinal)) continue;

            if (System.IO.File.ReadLines(path).Any(IsCallSite)) yield return rel;
        }
    }
}
