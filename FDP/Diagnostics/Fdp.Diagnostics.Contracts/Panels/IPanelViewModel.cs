using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Fdp.Diagnostics.Contracts.Panels;

/// <summary>
/// ⭐⭐⭐ <b>The whole description of what one panel SHOWS, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §APIs — approach <b>C</b>, <c>U-obs-1</c>.
///
/// <para>⛔⛔ <b>THE LOAD-BEARING INVARIANT: the draw renders ONLY from this.</b> ⚠ A panel that draws a
/// value it read straight from a source — <c>ImGui.Text(someSource)</c> rather than
/// <c>ImGui.Text(vm.SomeField)</c> — makes that value <b>invisible to the dump</b>, ⇒ ⭐ a test can go
/// GREEN while the UI is wrong. ⭐⭐ <b>The reviewable smell is exactly that: any drawn value that did not
/// come from the view-model.</b></para>
///
/// <para>⭐ <b>Why a whole model and not a per-element capture facade</b> *(approach B, rejected)*: a flat
/// item list is low fidelity, and a <b>separate</b> emit call drifts from what is actually drawn ⇒ false
/// greens. ⭐⭐ Here the capture cannot drift, because the model <b>IS</b> what the draw reads.</para>
///
/// <para>⭐ <b>The proven precedent:</b> <c>VariableTableModel</c> *(<c>Hrot.Editor.AiShared/Variables/</c>)* —
/// an immutable model rebuilt every frame that the table renders from. ⇒ this contract is that pattern,
/// generalised and made dumpable.</para>
/// </summary>
public interface IPanelViewModel
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE ADDRESS — unique among panels that are live at the same time.</b>
    /// ⛔⛔ This is what <c>GET /panels/{id}</c> resolves and what the snapshot is keyed by, so ⚠ **two live
    /// panels sharing it means an agent cannot say WHICH one it wants, and one silently overwrites the
    /// other in the dump.** ⇒ ⭐ a per-perspective window uses **its own window-manager registration id**
    /// *(`ai_watch_btree`)*, which is unique by construction; ⭐ a singleton panel uses its declared literal.
    /// 📌 <see cref="PanelKind"/> is the other half — read both before choosing either.
    /// </summary>
    string PanelId { get; }

    /// <summary>
    /// ⭐⭐⭐ <b>THE KIND — the stable logical name, IDENTICAL across hosts and perspectives.</b>
    /// ⭐ <c>watch</c> · <c>variables</c> · <c>entity-blueprints</c>. ⛔ **Cross-host conformance groups by
    /// THIS, never by <see cref="PanelId"/>** — 📌 the address is unique *by construction*, which is exactly
    /// the property that makes it useless for saying *"these two are the same panel."*
    ///
    /// <para>⚠⚠ <b>The two are separate FIELDS because they are opposite requirements</b>, and collapsing
    /// them was a real mistake in this contract's first cut: an address that is stable across hosts cannot
    /// disambiguate two live panels, and a kind that is unique per instance cannot be diffed. ⭐ For a
    /// singleton panel they simply carry the same string, which is why the mistake was invisible on the
    /// pilot.</para>
    /// </summary>
    string PanelKind { get; }

    /// <summary>
    /// ⭐ The model as JSON — ⛔ <b>structured, never a pre-formatted string blob</b>: a test asserts a
    /// FIELD, and a conformance diff names the exact diverging path.
    /// </summary>
    JsonNode Dump();
}

/// <summary>
/// ⭐⭐ <b>The default <see cref="IPanelViewModel.Dump"/> — serialise the view-model itself.</b>
/// 📄 §"Open questions" ① resolved to the design's own lean: <i>"STJ over the VM … with a hook for custom
/// cases"</i>. ⇒ ⭐ a VM implements <c>Dump()</c> as <c>PanelDump.Of(this)</c> and gets its whole public
/// shape for free; ⛔ a VM with a genuinely custom shape simply writes its own <c>JsonNode</c> instead.
/// </summary>
public static class PanelDump
{
    /// <summary>⭐ camelCase to match the rest of the MCP surface and the design's §Example payload.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented          = false,
    };

    /// <summary>
    /// ⭐ Serialise <paramref name="viewModel"/> to a <see cref="JsonNode"/>.
    /// ⚠ Never returns <see langword="null"/>: a model that serialises to JSON <c>null</c> comes back as an
    /// empty object, ⛔ because <i>"the panel dumped nothing"</i> and <i>"the panel is not instrumented"</i>
    /// must stay distinguishable — 📌 that distinction is <c>U1b</c>'s whole subject.
    /// </summary>
    public static JsonNode Of<T>(T viewModel) where T : IPanelViewModel
        => JsonSerializer.SerializeToNode(viewModel, Options) ?? new JsonObject();
}
