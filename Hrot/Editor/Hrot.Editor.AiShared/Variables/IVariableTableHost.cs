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

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 100 (<c>100f</c>) — which row gestures THIS surface offers.</b>
    ///
    /// <para>📌 <b>User:</b> <i>"no one is interested in the other properties than the value in the
    /// Watch window."</i> ⇒ the Watch answers <see cref="VariableTableGestures.Watch"/>; every
    /// authoring surface answers <see cref="VariableTableGestures.Default"/>.</para>
    ///
    /// <para>⛔⛔ <b>NO DEFAULT BODY, deliberately</b> — 📌 <c>U-5</c>/<c>BP-230</c>:
    /// <i>"a default body is the interface volunteering to lie on an implementer's behalf."</i>
    /// ⚠ A <c>=&gt; VariableTableGestures.Default</c> here would silently give a new monitoring surface
    /// the authoring menu, which is the defect this member exists to end. ⭐ The cost is that every
    /// host must answer — <b>and that cost IS the feature.</b></para>
    /// </summary>
    VariableTableGestures Gestures { get; }

    /// <summary>
    /// ⭐⭐⭐ <b><c>W4</c> — the MODEL behind that table, or <c>null</c> when this host has none.</b>
    ///
    /// <para>📄 <c>DESIGN_Staged_Live_Write.md</c> §4 fork A needs ONE shared
    /// <see cref="StagedWriteView"/> to reach EVERY variable surface — ⛔ otherwise Details and Watch
    /// disagree about what is pending, which is the divergence §7 exists to remove.</para>
    ///
    /// <para>⭐⭐ <b>Why an interface member and not four assignments at the registrar.</b> 📌 The same
    /// reason <see cref="VariableTable"/> is one: the handoff for Batch 87 knew of THREE hosts and the
    /// graph found FOUR. ⚠ Today it is <b>SIX</b> — <c>VariableDetailsSection</c>,
    /// <c>AiVariablesWindow</c>, <c>AiWatchWindow</c>, <c>DetailsWindow</c>,
    /// <c>BlueprintDetailsWindow</c>, <c>WatchPanelWindow</c>. ⇒ ⭐ a seventh host added later is wired
    /// with NO new line anywhere, and the <c>2026-08-16</c> rule *(a production caller that HAS a
    /// dependency must PASS it)</c> is kept by construction instead of by care.</para>
    ///
    /// <para>⛔⛔ <b>NO DEFAULT BODY</b>, for the same reason <see cref="Gestures"/> has none —
    /// 📌 <c>U-5</c>/<c>BP-230</c>: <i>"a default body is the interface volunteering to lie on an
    /// implementer's behalf."</i> ⚠ A <c>=&gt; null</c> here would silently leave a new surface out of
    /// the shared yellow, and it would look exactly like a host that legitimately has no table.</para>
    ///
    /// <para>⭐ <b>A READ, not a setter</b> — the registrar does the forwarding, so a rail can assert on
    /// the CONSTRUCTED model *(<c>R-67</c>)</c> rather than on the registrar's source.</para>
    /// </summary>
    VariableTableModel? TableModel { get; }
}
