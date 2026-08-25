using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.SystemTests.Conformance;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-016</c> — the main toolbar on a runtime node carries the TIME TRANSPORT, as it does in
/// the editor.</b>
///
/// <para>📄 <c>docs/PROGRAMME_Cgf_Equals_Editor_Gap_Map.md</c> *(the <c>main-toolbar</c> row)* ·
/// slice-2 design §7 *("a feature CONTROLLED FROM THE TOOLBAR must be wired AND instrumented on CGF
/// too")*.</para>
///
/// <para>🔴🔴 <b><c>CE-016</c>'s stated premise was ALREADY STALE when this was dispatched.</b> It reads
/// *"the CGF main-toolbar is EMPTY — <c>EditorSubsystem</c> is the only caller of
/// <c>MainToolbar.RegisterEntry</c>"*. 📐 Measured <c>2026-08-25</c>: slice 3 already registered
/// <c>SaveAllAiDocuments</c> + <c>QuickReloadAiAsset</c> on CGF *(<c>CE-022</c>)</b>, and the shared
/// conformance rail already asserts both by id and visibility. ⇒ ⭐ the REMAINING gap is narrower and
/// sharper, and it is a silent default rather than a missing feature.</para>
///
/// <para>⭐⭐⭐ <b>The real gap this closes.</b> 📐 The editor registers
/// <c>MainToolbarTimeControlSection</c> on its toolbar *(<c>EditorSubsystem:4715</c>)</b> AND a
/// status-bar section. CGF built <c>ClusterTimeTransportAdapter</c> — the very
/// <c>ITimeTransportFacade</c> that section takes — and passed it ONLY to the status bar, two lines
/// away. 📌 *"A production caller that HAS a dependency must PASS it."* ⇒ nothing was invented here: the
/// same shared section, the same seam, the same entry id and sort order as the editor.</para>
///
/// <para>⚠ <b>Why this lives in its own file.</b> The handoff asked for it: the shared
/// <c>ClusterConformanceRails</c> is being edited by a concurrent session, so a new assertion there
/// would be a merge race. ⛔ It deliberately does NOT re-assert what that file already covers.</para>
/// </summary>
public sealed class TheRuntimeNodeCarriesTheTransportRails
{
    private readonly ITestOutputHelper _out;
    public TheRuntimeNodeCarriesTheTransportRails(ITestOutputHelper output) => _out = output;

    private const string TransportEntryId = "TimeControlGroup";

    private static JsonArray EntriesOf(string model)
        => (JsonNode.Parse(model)!["entries"] as JsonArray)!;

    /// <summary>
    /// ⭐⭐⭐ <b>Both hosts offer the transport, and on the cluster it is VISIBLE.</b>
    ///
    /// <para>⛔ Visibility is asserted separately from presence on purpose: an entry bound to a
    /// perspective the host never shows satisfies an id check and offers the operator nothing — the same
    /// distinction the slice-3 affordance rail had to make.</para>
    ///
    /// <para>⭐ The EDITOR side is the anti-vacuity guard: if the editor stopped publishing the transport
    /// the shared class would be broken for both, and asserting only the cluster would hide that.</para>
    /// </summary>
    [SystemSmokeFact]
    public async Task The_time_transport_is_on_the_toolbar_of_both_hosts()
    {
        await using var editor  = await EditorProcess.StartAsync("ce016-transport-editor");
        await using var cluster = await EditorProcess.StartAsync("ce016-transport-all", mode: "all");

        var a = await ClusterConformanceRails.CaptureByKindAsync(editor,  _out);
        var b = await ClusterConformanceRails.CaptureByKindAsync(cluster, _out);

        Assert.True(a.ContainsKey("main-toolbar"), "the EDITOR does not publish 'main-toolbar'.");
        Assert.True(b.ContainsKey("main-toolbar"), "--mode all does not publish 'main-toolbar'.");

        var editorEntries  = EntriesOf(a["main-toolbar"].Model);
        var clusterEntries = EntriesOf(b["main-toolbar"].Model);

        string[] editorIds  = editorEntries.Select(e => e!["id"]!.GetValue<string>()).ToArray();
        string[] clusterIds = clusterEntries.Select(e => e!["id"]!.GetValue<string>()).ToArray();

        _out.WriteLine($"[CE-016] editor toolbar ids : [{string.Join(", ", editorIds)}]");
        _out.WriteLine($"[CE-016] cluster toolbar ids: [{string.Join(", ", clusterIds)}]");

        Assert.Contains(TransportEntryId, editorIds, StringComparer.Ordinal);

        Assert.True(clusterIds.Contains(TransportEntryId, StringComparer.Ordinal),
            $"--mode all's main toolbar does not offer '{TransportEntryId}'. ⭐ CGF holds a "
          + "ClusterTimeTransportAdapter and gave it only to the status bar — the toolbar section takes "
          + "the same ITimeTransportFacade, so this is a dependency the caller HAD and did not PASS.");

        var onCluster = clusterEntries.First(
            e => e!["id"]!.GetValue<string>() == TransportEntryId);

        Assert.True(onCluster["visible"]!.GetValue<bool>(),
            $"'{TransportEntryId}' is registered on --mode all but NOT visible in the active perspective "
          + "— the affordance is in the table and not on screen.");
    }

    /// <summary>
    /// ⚠ <b>And the toolbar is not empty on the runtime node</b> — the plain form of the gap-map row,
    /// kept because that row is what a later reader will look up. ⭐ It is now true for three entries,
    /// two of them from slice 3.
    /// </summary>
    [SystemSmokeFact]
    public async Task The_runtime_nodes_toolbar_is_not_empty()
    {
        await using var cluster = await EditorProcess.StartAsync("ce016-nonempty-all", mode: "all");

        var b = await ClusterConformanceRails.CaptureByKindAsync(cluster, _out);
        Assert.True(b.ContainsKey("main-toolbar"), "--mode all does not publish 'main-toolbar'.");

        var entries = EntriesOf(b["main-toolbar"].Model);
        _out.WriteLine($"[CE-016] cluster toolbar entry count: {entries.Count}");

        Assert.True(entries.Count > 0,
            "the runtime node published a main toolbar with ZERO entries — CE-016's original state.");
    }
}
