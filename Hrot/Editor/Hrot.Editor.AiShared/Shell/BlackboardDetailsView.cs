using System;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L3.3</c> — the Blackboard authoring panel becomes a Details VIEW.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L3</c>'s table, rows
/// <i>"Layout / byte budget · Asset settings | <c>BlackboardAuthoringWindow</c> | asset context"</i>
/// and <i>"Diagnostics | <c>VariablesPanelControl</c>'s host | asset context"</i>.
///
/// <para>⚠⚠ <b>STATED DEVIATION — §6 names THREE rows here and the code has ONE body.</b>
/// 📐 Measured: <c>BlackboardAuthoringWindow.DrawClientArea</c> is a single flowing body — comparison
/// toolbar → state banner → <c>VariablesPanelControl</c> → the <c>SUB-TREE ALLOCATIONS</c> collapsing
/// header — with <b>no seam</b> between <i>"layout / byte budget"</i>, <i>"asset settings"</i> and
/// <i>"diagnostics"</i>. ⭐ And <c>VariablesPanelControl</c>'s host <b>IS this window</b>
/// *(<c>:509</c>)*, so the Diagnostics row names the same object.
/// ⇒ ⭐ it ships as <b>ONE</b> view. ⛔ Splitting one body into three is a DECOMPOSITION, not a
/// delegation — 📌 §6 calls <c>L3</c> <i>"the delegation layer"</i>, and inventing the split here would
/// be design work inside an implementation batch.</para>
///
/// <para>⚠ <b>The window is BORROWED</b> — the registrar builds it and wires its refactor service,
/// comparison toolbar and sub-asset resolver. ⛔ <see cref="Dispose"/> must not touch it. ⭐ Same
/// contract as <c>L1.3</c>'s section, <c>L3.1</c>'s pane and <c>L3.2</c>'s window.</para>
/// </summary>
public sealed class BlackboardDetailsView : IDetailsViewInstance
{
    private readonly BlackboardAuthoringWindow _window;

    public BlackboardDetailsView(BlackboardAuthoringWindow window)
        => _window = window ?? throw new ArgumentNullException(nameof(window));

    /// <summary>⭐ Draws the window's own body through the <c>DrawContent</c> seam. ⛔ One body, two
    /// hosts.</summary>
    public void Draw(DetailsContext context, string idScope) => _window.DrawContent();

    /// <summary>⛔ Deliberately empty — the window is BORROWED. See the class remarks.</summary>
    public void Dispose() { }
}

/// <summary>⭐⭐ <b><c>L3.3</c> — the descriptor.</b></summary>
public static class BlackboardDetailsViewDescriptor
{
    /// <summary>⭐ The stable id — the layout key and §2's <i>"remember my pick"</i> key.</summary>
    public const string ViewId = "details.blackboard";

    /// <summary>
    /// ⭐ Rank <b>5</b> — <b>below</b> <c>Variables</c>' <c>10</c>. ⚠ Deliberate: its predicate is the
    /// weakest one there is *(any open document)*, so a higher rank would make it the default answer
    /// to almost every context and hide the views with something specific to say.
    /// 📌 <c>R-98</c> — rank decides only the DEFAULT; the toolbar's pick wins.
    /// </summary>
    public const int Rank = 5;

    public static DetailsViewDescriptor For(BlackboardAuthoringWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var instance = new BlackboardDetailsView(window);

        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Blackboard",
            Rank:      Rank,
            // ⭐ §6 L3's own words for these rows: "asset context".
            AppliesTo: DetailsViewPredicates.HasAsset,
            Create:    () => instance);
    }
}
