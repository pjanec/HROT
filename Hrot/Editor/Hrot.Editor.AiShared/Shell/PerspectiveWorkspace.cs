using System;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L6.1a</c> — THE GENERIC HALF OF A PERSPECTIVE, EXTRACTED.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §5 *(what the "registry" is)* · §2's
/// <c>classDiagram</c> *(<c>PerspectiveWorkspace</c>)* · §6 <c>L6</c> stage 1.
///
/// <para>📐 <b>The measurement this exists for</b> *(§5)*: <c>PerspectiveWorkspaceRegistrar</c> fuses a
/// <b>wiring hub</b> — fully generic — with a <b>21-parameter AI-authoring service bag</b>. ⛔ <i>"The
/// generic half is trapped inside the specific one — which is why Scenario got a bespoke scenario
/// branch."</i> ⇒ ⭐ this type IS the generic half, and a perspective that is not AI authoring
/// *(Scenario)* can have one without the bag.</para>
///
/// <para>⭐⭐ <b>Four things, and they are the four §5 names:</b> the <see cref="DetailsViews"/>
/// catalogue · the <see cref="BuildContext"/> builder · the <see cref="EntitySelection"/> source · the
/// <see cref="Contribute"/> claim chain. ⛔ Nothing about validators, breakpoints, blackboards or
/// variables — those stay in the registrar's bag.</para>
///
/// <para>⚠⚠ <b><c>L6.1a</c> IS A PURE REFACTOR, and the shape proves it:</b> the registrar now OWNS one
/// of these and forwards to it, so every existing caller of <c>registrar.DetailsViews</c> /
/// <c>.EntitySelection</c> / <c>.StagedWrites</c> is unchanged. ⭐ The <b>stage gate</b>
/// *(<c>TheAiOfferSetsAreUnchangedTests</c>, written and green BEFORE this type existed)* is what says
/// so.</para>
///
/// <para>⛔ <b>It does NOT rename the persisted perspective key.</b> 📌 §5/§6: that is <c>L6.1b</c>,
/// DEFERRED — <c>CurrentPerspective</c> and every <c>OwningPerspective</c> are persisted, and a bare
/// rename silently resets saved layouts. ⭐ <see cref="PerspectiveName"/> carries whatever key the host
/// already uses.</para>
/// </summary>
public sealed class PerspectiveWorkspace
{
    private readonly Func<VariableRunState> _runState;
    private readonly System.Collections.Generic.HashSet<IDetailsViewSource> _viewSources = new();

    /// <param name="perspectiveName">
    /// ⚠ The PERSISTED key, not a label. ⛔ Scenario's is still <c>"Editor"</c> today *(§5)* — this type
    /// does not care, and <c>L6.1b</c> is what changes it, with a migration.
    /// </param>
    /// <param name="selectionStore">The asset/sub-selection half of a context.</param>
    /// <param name="runState">⭐ Read at BUILD time, never cached — a context is one frame's answer.</param>
    /// <param name="entitySelection">
    /// ⭐⭐ <c>L0.4</c>'s World-backed entity source *(<c>R-122</c>)*. ⚠ Held as a field so every context
    /// this perspective builds reads the SAME source — which is what keeps §6 <c>L0.4</c>'s
    /// same-instance guarantee meaningful. ⛔ <c>null</c> is legal *(a headless host has no World)*.
    /// </param>
    /// <param name="stagedWrites">
    /// ⭐ <c>W4</c>'s shared staged-write query. ⚠ Not used by this type — it is carried so the ONE
    /// instance reaches every table host through whoever owns this workspace. ⛔ Kept here rather than
    /// re-plumbed, because §5's point is that the generic half travels together.
    /// </param>
    public PerspectiveWorkspace(
        string                  perspectiveName,
        EditorSelectionStore    selectionStore,
        Func<VariableRunState>  runState,
        IEntitySelectionSource? entitySelection = null,
        StagedWriteView?        stagedWrites    = null)
    {
        if (string.IsNullOrWhiteSpace(perspectiveName))
            throw new ArgumentException("A workspace must name its perspective.", nameof(perspectiveName));

        PerspectiveName = perspectiveName;
        SelectionStore  = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
        _runState       = runState       ?? throw new ArgumentNullException(nameof(runState));
        EntitySelection = entitySelection;
        StagedWrites    = stagedWrites;
    }

    /// <summary>⭐ The persisted perspective key this workspace belongs to.</summary>
    public string PerspectiveName { get; }

    /// <summary>⭐ The asset/sub-selection store a context is built from.</summary>
    public EditorSelectionStore SelectionStore { get; }

    /// <summary>⭐⭐ This perspective's catalogue of details views *(<c>L1.1</c>)*.</summary>
    public DetailsViewRegistry DetailsViews { get; } = new();

    /// <summary>⭐⭐ <c>L0.4</c>'s entity source, or <c>null</c>. ⭐ Exposed so a rail can assert the
    /// CONSTRUCTED workspace got a real one *(<c>R-67</c>)*.</summary>
    public IEntitySelectionSource? EntitySelection { get; }

    /// <summary>⭐ <c>W4</c>'s shared staged-write query, or <c>null</c>. See the constructor.</summary>
    public StagedWriteView? StagedWrites { get; }

    /// <summary>
    /// ⭐⭐⭐ <b>§2's <c>PerspectiveWorkspace.BuildContext()</c> — the one place a context is made.</b>
    ///
    /// <para>⚠ <b>This resolves <c>L2.1</c>'s stated deviation.</b> <c>DetailsWindow</c> was handed a
    /// <c>LiveContextSource</c> whose lambda lived in the registrar, with a comment saying
    /// *"<c>L6.1</c> moves one method."</c> ⭐ This is that move; ⛔ the body is unchanged.</para>
    /// </summary>
    public DetailsContext BuildContext()
        => DetailsContextBuilder.Build(
            SelectionStore, PerspectiveName, _runState(), EntitySelection);

    /// <summary>⭐ The live source a shell holds — re-asks <see cref="BuildContext"/> every frame.</summary>
    public LiveContextSource ContextSource() => new(BuildContext);

    /// <summary>
    /// ⭐⭐⭐ <b>The <c>IDetailsViewSource</c> CLAIM CHAIN — a window contributes its own views.</b>
    /// 📄 §6 <c>L1.2</c> *(<c>R-67</c>)*: windows self-wire, so the composition root passes nothing
    /// extra and has nothing to forget.
    ///
    /// <para>⭐ Read ONCE, at registration — a source that varies its offer does so in its predicates
    /// *(<c>R-116</c>)*, ⛔ not by being re-read every frame. ⭐ The <c>_viewSources</c> guard means a
    /// window reaching the chain twice contributes exactly once.</para>
    ///
    /// <para>⚠ <b>A non-source candidate is silently ignored, and that is the contract</b> — the chain
    /// is called for EVERY registered window, and most are not view sources.</para>
    /// </summary>
    public void Contribute(object? candidate)
    {
        if (candidate is not IDetailsViewSource source) return;
        if (!_viewSources.Add(source)) return;

        foreach (var descriptor in source.DetailsViews)
            DetailsViews.Add(descriptor);
    }
}
