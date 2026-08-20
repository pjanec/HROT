namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Anything that puts a <see cref="VariableTableControl"/> on screen — so the gesture binder
/// can be attached to ALL of them from ONE place.</b>
///
/// <para>🔴🔴 <b>The defect this closes.</b> 📐 Measured at <c>PerspectiveWorkspaceRegistrar</c>:
/// <c>EditGestures.Attach(Variables.Control)</c> — <b>the standalone Variables window, and nothing
/// else.</b> ⛔ The Details panel builds its own <see cref="VariableDetailsSection"/> and no one
/// attached to it, so <c>D2</c>/<c>D3</c>/<c>D11</c> failed the visual check with <b>NO MENU AT
/// ALL</b> — ⚠ which was being read as <c>BP-327</c>, *"the dialog has no OK button."* <b>Two defects
/// wearing one name.</b></para>
///
/// <para>⭐⭐⭐ <b>Enumerated with the graph, not grepped</b> *(📌 <c>R-74</c>: only the graph
/// enumerates)*. The handoff knew of THREE hosts; ⚠ <b>the query found FOUR</b> — the fourth is
/// <c>Hrot.Blueprints.Editor.Debug.WatchPanelWindow</c>, which builds its own table and already
/// exposes it. 📌 That is exactly why this is an INTERFACE and not three call sites: ⛔ a fifth host
/// added later must not depend on someone remembering a fourth <c>Attach</c> line.</para>
///
/// <para>⚠ <b>The property may be <c>null</c>, and that is not a defect.</b> <c>AiWatchWindow</c>
/// builds its table only when it was given a formatter and a source; a Watch with no variable panel
/// has no table to bind. ⭐ The registrar skips a null and RECORDS nothing, so the rail counts the
/// tables that exist rather than the hosts that might have had one.</para>
/// </summary>
public interface IVariableTableHost
{
    /// <summary>
    /// The table this host draws, or <c>null</c> when it has none.
    /// ⭐ The CONSTRUCTED object — 📌 <c>R-67</c>: <i>"a rail that builds its own composition root
    /// cannot see a composition-root defect"</i>, so the rail must be able to reach the real one.
    /// </summary>
    VariableTableControl? VariableTable { get; }
}
