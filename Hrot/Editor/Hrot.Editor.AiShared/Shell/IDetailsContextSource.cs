using System;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L2.1</c> — WHERE A DETAILS WINDOW GETS ITS CONTEXT.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §2's <c>classDiagram</c>
/// *(<c>DetailsWindow o-- IDetailsContextSource</c> · <c>DetailsViewWindow o-- IDetailsContextSource</c>)*
/// and §2's hosting table.
///
/// <para>⭐⭐⭐ <b>This one interface is the ENTIRE difference between the three hosting modes</b> — §2,
/// verbatim: <i>"the two window classes differ ONLY in <c>IDetailsContextSource</c>."</i>
/// ⇒ <c>Live</c> ⇒ docked or contextual float · <c>Frozen</c> ⇒ a pin *(<c>R-119</c>)*. ⛔ There is no
/// mode flag and no <c>if (isPinned)</c> anywhere — a pin is a different SOURCE, not a different
/// window.</para>
///
/// <para>⚠ <see cref="FrozenContextSource"/> is <c>L4.3</c>'s and is deliberately NOT here yet — ⛔ a
/// snapshot type with no window to freeze for would be a guess about <c>L4</c>, and 📌 §6 puts the pin
/// three tasks away.</para>
/// </summary>
public interface IDetailsContextSource
{
    /// <summary>
    /// ⭐ This frame's context. ⛔ Never <see langword="null"/> — 📌 <c>R-117</c>: a window with no
    /// context would draw a blank, and <see cref="DetailsContext.Empty"/> is the honest answer to
    /// <i>"nothing is open"</i>.
    /// </summary>
    DetailsContext Current();
}

/// <summary>
/// ⭐⭐ <b><c>L2.1</c> — the LIVE source: re-read every frame, so the panel follows the selection.</b>
/// 📄 §2's classDiagram *(<c>LiveContextSource o-- PerspectiveWorkspace</c>)*.
///
/// <para>⚠ <b>DEVIATION, stated: this holds a delegate, not a <c>PerspectiveWorkspace</c>.</b>
/// 📐 That type is extracted in <b><c>L6.1</c></b> *(§6: "extract <c>PerspectiveWorkspace</c>, give
/// Scenario one, rename the key with a layout migration")*, ⛔ not in <c>L2</c>. ⇒ ⭐ the delegate is
/// supplied by <c>PerspectiveWorkspaceRegistrar</c> — §5's <i>"wiring hub"</i> half, which is the very
/// thing <c>L6.1</c> splits out — so <c>L6.1</c> replaces one constructor argument and nothing else.
/// ⚠ Same shape as <c>L0.3</c>'s builder deviation and <c>L1</c>'s registry home, and for the same
/// reason: the diagram describes the FINISHED design; the layer order decides when each box arrives.</para>
///
/// <para>⛔ <b>It does not cache.</b> §2b's pan sequence wants <i>"the same context object as the frame
/// before"</i> — ⭐ that guarantee belongs to the STORE's stable list instance and
/// <see cref="DetailsContext"/>'s value equality, ⛔ not to a memo here. A cache here would have to know
/// when to invalidate, and getting that wrong is how a panel goes stale.</para>
/// </summary>
public sealed class LiveContextSource : IDetailsContextSource
{
    private readonly Func<DetailsContext> _build;

    public LiveContextSource(Func<DetailsContext> build)
        => _build = build ?? throw new ArgumentNullException(nameof(build));

    /// <inheritdoc/>
    public DetailsContext Current()
        => _build() ?? throw new InvalidOperationException(
               "The context builder returned null. A Details window must always have a context — " +
               "use DetailsContext.Empty(perspective) for 'nothing is open' (R-117).");
}
