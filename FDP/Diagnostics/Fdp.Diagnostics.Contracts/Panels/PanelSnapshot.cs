using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Fdp.Diagnostics.Contracts.Panels;

/// <summary>
/// ⭐⭐⭐ <b>The per-frame snapshot every instrumented panel writes into, and tests / MCP read from.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §APIs + §"Perf &amp; correctness" — <c>U-obs-1</c>.
///
/// <para>⭐⭐ <b>A static singleton on purpose.</b> ⛔ Panels are constructed by a dozen different hosts and
/// registrars; threading a snapshot service through every one of them would be a composition-root change in
/// every assembly — 📌 and the handoff is explicit that this needs <b>no <c>EditorSubsystem</c> edit</b>.
/// ⚠ The cost is stated rather than hidden: this is process-global mutable state, so tests that read it must
/// <see cref="Clear"/> first.</para>
///
/// <para>⭐⭐⭐ <b>TWO SETS, and the difference is the point</b> *(<c>U1b</c>)*:
/// <list type="bullet">
///   <item>⭐ <see cref="RegisteredPanels"/> — <b>every panel that is INSTRUMENTED AT ALL</b>, declared once at
///   construction and <b>independent of <see cref="CaptureEnabled"/></b>;</item>
///   <item>⭐ <see cref="CapturedPanels"/> — those that actually <b>dumped a model</b>.</item>
/// </list>
/// ⛔⛔ <b>Collapsing them produces FALSE GREENS</b>: a panel that is not converted at all and a panel whose
/// window is simply closed would look identical, and <i>"the assertion found nothing"</i> would read as
/// <i>"the UI showed nothing"</i>. ⇒ 📄 this is exactly why §"Perf &amp; correctness" requires the opt-in
/// registry, and why <c>GET /panels</c> is specified to return <b>both</b> lists.</para>
///
/// <para>⚠⚠ <b>A LIMIT, stated rather than papered over — there is NO FRAME BOUNDARY here.</b>
/// 📐 Entries are <b>latest-wins</b> and persist until overwritten or <see cref="Clear"/>ed ⇒ ⛔ a panel that
/// stops drawing *(its window closed)* leaves its LAST model visible, which a reader could mistake for a live
/// one. ⭐ Clearing per frame needs a call site in the frame loop — <c>EditorSubsystem</c> — which this lane
/// must not touch. ⇒ 📌 recorded as a finding for whoever owns the loop; ⭐ tests call <see cref="Clear"/>.</para>
/// </summary>
public static class PanelSnapshot
{
    private static readonly ConcurrentDictionary<string, IPanelViewModel> Captured = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte>            Instrumented = new(StringComparer.Ordinal);

    /// <summary>
    /// ⭐⭐ <b>Gates the DUMP, not the BUILD.</b> ⛔ Production still builds every view-model — it must, the
    /// draw renders from it — it simply does not <see cref="Register"/>. ⇒ 📐 the cost when off is <b>one
    /// branch per panel per frame</b>, which is why the invariant is affordable everywhere.
    /// </summary>
    public static bool CaptureEnabled { get; set; }

    /// <summary>
    /// ⭐⭐⭐ <b>Declare that this panel is instrumented — call it ONCE, at construction, ALWAYS.</b>
    /// ⛔⛔ <b>Not inside the draw, and not gated on <see cref="CaptureEnabled"/>.</b> ⚠ A panel whose window
    /// is never opened never draws; if instrumentation were declared by drawing, that panel would be
    /// indistinguishable from one nobody has converted — 📌 the false green <c>U1b</c> exists to prevent.
    /// </summary>
    public static void DeclareInstrumented(string panelId)
    {
        if (string.IsNullOrWhiteSpace(panelId)) throw new ArgumentException("A panel id is required.", nameof(panelId));
        Instrumented[panelId] = 0;
    }

    /// <summary>
    /// ⭐ Publish this frame's model. ⚠ The caller is expected to have checked <see cref="CaptureEnabled"/>
    /// *(so the argument is not even built when off)*; ⛔ this also re-checks, so a forgetful caller costs
    /// correctness rather than a silent production dump.
    /// ⭐⭐ Registering also marks the panel instrumented — ⛔ a panel that dumps but never declared is still
    /// instrumented **by evidence**, and that must not read as "not converted".
    /// </summary>
    public static void Register(IPanelViewModel viewModel)
    {
        if (viewModel is null) throw new ArgumentNullException(nameof(viewModel));

        string id = viewModel.PanelId;
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A panel view-model must carry a unique PanelId.", nameof(viewModel));
        if (string.IsNullOrWhiteSpace(viewModel.PanelKind))
            throw new ArgumentException("A panel view-model must carry a stable PanelKind.", nameof(viewModel));

        Instrumented[id] = 0;
        if (!CaptureEnabled) return;

        Captured[id] = viewModel;
    }

    /// <summary>
    /// ⭐⭐ <b>The live panel ids of one KIND</b> — what a cross-host conformance check groups by.
    /// ⛔ Returns ids *(addresses)*, not models, because the caller's next question is always
    /// <i>"…and which one"</i>: 📐 three perspectives can each host a <c>watch</c>, and the whole point of
    /// the two-field split is that they stay individually addressable while still being comparable.
    /// </summary>
    public static IReadOnlyList<string> PanelsOfKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return Array.Empty<string>();

        var matches = new List<string>();
        foreach (var kv in Captured)
            if (string.Equals(kv.Value.PanelKind, kind, StringComparison.Ordinal))
                matches.Add(kv.Key);
        matches.Sort(StringComparer.Ordinal);   // ⭐ deterministic — a conformance diff must not reorder
        return matches;
    }

    /// <summary>⭐ The latest model for <paramref name="panelId"/>, or <see langword="null"/>. ⚠ <b>Null means
    /// "nothing captured"</b> — ⛔ ask <see cref="RegisteredPanels"/> whether that is because the panel is not
    /// instrumented or because it did not draw.</summary>
    public static IPanelViewModel? TryGet(string panelId)
        => string.IsNullOrWhiteSpace(panelId) ? null
         : Captured.TryGetValue(panelId, out var vm) ? vm
         : null;

    /// <summary>
    /// ⭐ Every captured model, keyed by panel id: <c>{ "&lt;panelId&gt;": { … } }</c>.
    /// ⚠ Captured panels ONLY — ⛔ the instrumented-but-silent ones are <see cref="RegisteredPanels"/>'s job,
    /// deliberately kept a separate list so a reader cannot confuse an empty model with an absent one.
    /// </summary>
    public static JsonObject DumpAll()
    {
        var root = new JsonObject();
        foreach (var kv in Captured)
            root[kv.Key] = kv.Value.Dump();
        return root;
    }

    /// <summary>⭐ Every panel that is instrumented at all — ⛔ <b>independent of whether it drew.</b></summary>
    public static IReadOnlyCollection<string> RegisteredPanels => (IReadOnlyCollection<string>)Instrumented.Keys;

    /// <summary>⭐ Those that actually published a model. ⚠ A subset of <see cref="RegisteredPanels"/>.</summary>
    public static IReadOnlyCollection<string> CapturedPanels => (IReadOnlyCollection<string>)Captured.Keys;

    /// <summary>
    /// ⭐ Drop everything — <b>both</b> sets. ⚠ For tests: this is process-global state, so a test that reads
    /// the snapshot must clear it first or inherit another test's panels.
    /// </summary>
    public static void Clear()
    {
        Captured.Clear();
        Instrumented.Clear();
    }
}
