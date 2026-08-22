using System;
using System.Linq;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L5</c>'s rail — the retired surfaces are GONE, and the ones that must SURVIVE are still
/// here.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L5</c> ·
/// 📄 <c>Architect_Question_38_One_Details_Panel.md</c>'s retire/stay table *(<c>R-113</c>,
/// <c>R-114</c>, ruled <c>2026-08-20</c>)*.
///
/// <para>⭐⭐ <b>Why a rail at all for a DELETION.</b> ⛔ A deleted class cannot be asserted by calling
/// it. ⚠ But the failure mode of a retirement is not "it still compiles" — it is <b>someone
/// reintroduces it</b>, or a merge resurrects it. ⇒ ⭐ this asserts over the loaded assemblies by NAME,
/// which is the only thing that stays true after the type is gone.</para>
///
/// <para>⛔⛔ <b>The SURVIVORS are asserted in the same file, deliberately.</b> 📌 <c>CLAUDE.md</c>:
/// <i>"duplicate SURFACE (usually keep — surfaces differ by context)"</i>. ⚠ A retirement rail that
/// lists only the dead invites the next reader to delete their neighbours too.</para>
/// </summary>
public sealed class TheRetiredSurfacesAreGoneTests
{
    /// <summary>⭐ Every editor assembly this batch could have touched, loaded by a type from each.</summary>
    private static Type[] EditorTypes() => new[]
    {
        typeof(Hrot.Editor.AiShared.Windows.DetailsWindow),
        typeof(Hrot.Blueprints.Editor.Windows.BlueprintNodeDetailsView),
        typeof(Hrot.BTree.Editor.Inspector.BTreeRuntimeInspectorPane),
    };

    private static bool Exists(string simpleName)
        => EditorTypes()
            .Select(t => t.Assembly)
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Any(t => t.Name == simpleName);

    // ══ RETIRED — Q38's list, per item ═══════════════════════════════════════

    /// <summary>
    /// ⛔ <b><c>LiveBlackboardPanel</c> is gone</b> — 📌 <c>R-114</c>, ruled <c>2026-08-20</c>:
    /// <i>"no feature the variable table lacks"</i>. 📐 Measured before deleting: <b>in-degree 0</b>
    /// in the graph AND no reference outside its own file — ⭐ nothing hosted it.
    /// </summary>
    [Fact]
    public void LiveBlackboardPanel_IsRetired() => Assert.False(Exists("LiveBlackboardPanel"));

    /// <summary>
    /// ⛔ <b>The Blueprints <c>InspectorWindow</c> is gone</b> — ⚠ the <b>second</b> class of that name
    /// *(70 lines)*. 📐 Measured: its Node tab drew the literal string <i>"Node inspector -- select a
    /// node in the graph editor."</i> ⇒ a placeholder, not a surface. ⭐ The real node inspector is
    /// <c>BlueprintNodeDetailsView</c> *(<c>BlueprintDetailsWindow</c>'s node arm, live since Batch 87,
    /// extracted by <c>S1</c>)
    /// </summary>
    [Fact]
    public void TheBlueprintsInspectorStub_IsRetired()
        => Assert.DoesNotContain(
            typeof(Hrot.Blueprints.Editor.Windows.BlueprintNodeDetailsView).Assembly.GetTypes(),
            t => t.Name == "InspectorWindow");

    /// <summary>
    /// ⛔⛔⛔ <b><c>S1</c> (<c>BP-399</c>) — <c>BlueprintDetailsWindow</c> IS RETIRED.</b>
    /// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.3 ①, verbatim: <i>"<c>DetailsWindow</c> is
    /// THE shell on all four perspectives. ⛔ <c>BlueprintDetailsWindow</c> <b>dissolves</b> — its arms
    /// become views."</i>
    ///
    /// <para>⭐⭐ <b>Its replacement is live, which is §6 <c>L5</c>'s precondition</b>, in two halves:
    /// the node arm is <see cref="Hrot.Blueprints.Editor.Windows.BlueprintNodeDetailsView"/> *(§7.4:
    /// content EXTRACTED, not wrapped)*, and the variables list, the Properties form, the run-state
    /// source and the edit gestures all come from the shared <c>DetailsWindow</c>.</para>
    ///
    /// <para>⚠ <b>The failure mode this guards is RESURRECTION</b>, not compilation — a merge or a
    /// well-meaning revert that brings the class back would give Blueprint two Details windows fighting
    /// over one persisted id, and <c>RegisterCore</c> would throw at startup.</para>
    /// </summary>
    [Fact]
    public void TheBlueprintDetailsWindow_IsRetired()
        => Assert.False(Exists("BlueprintDetailsWindow"));

    /// <summary>⛔ <b>The two Blueprint variables windows are gone</b> — their replacement
    /// *(the shared <c>DetailsWindow</c> hosting the SHARED <c>VariableDetailsSection</c>, <c>U-6</c> /
    /// Batch 82, and since <c>S1</c> on Blueprint too)* is live, which is §6 <c>L5</c>'s
    /// precondition.</summary>
    [Fact]
    public void TheBlueprintVariablesWindows_AreRetired()
    {
        Assert.False(Exists("BlueprintVariablesWindow"));
        Assert.False(Exists("BlueprintVariablesManagedWindow"));
    }

    // ══ SURVIVORS — the other half of the ruling ═════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b><c>AiWatchWindow</c> SURVIVES</b> — 📌 <c>R-112</c>/<c>R-113</c>: <i>"a curated list
    /// kept open ACROSS selections ⇒ standalone."</i> ⛔ It is **not** a Details view and must never
    /// become one.
    /// </summary>
    [Fact]
    public void TheAiWatchWindow_Survives() => Assert.True(Exists("AiWatchWindow"));

    /// <summary>
    /// ⭐⭐ <b>The 697-line <c>AiShared.InspectorWindow</c> SURVIVES</b> — ⚠ same NAME as the retired
    /// stub, different type, and §6 <c>L3</c> says of it: <i>"⛔ do not delegate this one."</i>
    /// ⭐ Railed because two classes sharing a name is exactly how the wrong one gets deleted next time.
    /// </summary>
    [Fact]
    public void TheAiSharedInspectorWindow_Survives()
        => Assert.NotNull(typeof(Hrot.Editor.AiShared.Windows.InspectorWindow));

    /// <summary>
    /// ⭐⭐⭐ <b>The two types that SHARED <c>BlueprintVariablesWindow</c>'s FILE survive.</b>
    /// ⚠⚠ 📐 Measured before deleting: <c>BlueprintEditableAssetAdapter</c> is used by
    /// <c>BlueprintNewAssetService</c> at <b>5</b> sites, and <c>BlueprintVariableSchemaSource</c> by
    /// <c>BlueprintMyBlueprintWindow:533</c> in <b>production</b>. ⛔ Deleting the FILE would have taken
    /// both — 📌 <c>CLAUDE.md</c>: <b>the file is not the unit, the class is.</b>
    /// </summary>
    [Fact]
    public void TheFileMatesOfTheRetiredWindow_Survive()
    {
        Assert.True(Exists("BlueprintEditableAssetAdapter"));
        Assert.True(Exists("BlueprintVariableSchemaSource"));
    }

    // ══ NOT retired, and why ═════════════════════════════════════════════════

    /// <summary>
    /// 🛑 <b><c>WatchPanelWindow</c> IS STILL HERE, and that is deliberate.</b>
    ///
    /// <para>📄 <c>Q38</c>, verbatim: <i>"<c>Q44-B</c> (send the breakpoint rows home) now runs
    /// <b>BEFORE</b> <c>Q38-E</c> step 1 — ⛔ otherwise step 1 merges a heterogeneous surface."</i>
    /// 📐 Measured: <c>AiBreakpointsWindow</c> has <b>no watch list</b>, so <c>Q44-B</c> has not run.
    /// ⇒ ⛔ retiring this window today would delete the <b>only</b> surface showing
    /// <c>IBlueprintDebugSession.GetWatches()</c>.</para>
    ///
    /// <para>⭐ §6 <c>L5</c>'s own condition is <i>"per item, <b>after its replacement is live</b>"</i>
    /// — ⚠ this rail is that condition, asserted, so the retirement cannot be done by accident before
    /// the move.</para>
    /// </summary>
    [Fact]
    public void WatchPanelWindow_IsNotRetiredYet_BecauseQ44BHasNotRun()
        => Assert.True(Exists("WatchPanelWindow"));
}
