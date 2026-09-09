using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

namespace Hrot.SystemTests.Goldens;

/// <summary>
/// ⭐⭐⭐ <b><c>N2</c> — where a panel golden lives, and the compare-or-write switch.</b>
/// 📄 <c>docs/DESIGN_Regression_Net.md</c> §7 <c>N2</c> · §4b *(the key is <c>PanelId</c>, not
/// <c>PanelKind</c>)*.
///
/// <para>⭐⭐ <b>It follows the house convention deliberately.</b> 📐 <c>EqsGolden</c>
/// *(<c>Hrot.ClusterRunner.Integration.Tests/Eqs/Golden/</c>)* already established
/// <c>&lt;FAMILY&gt;_GOLDEN_CAPTURE=1</c>, <c>[CallerFilePath]</c> for a machine-independent directory, and
/// *"compare returns a list of human-readable mismatches"*. ⇒ ⭐ this is <c>PANEL_GOLDEN_CAPTURE=1</c> with
/// the same three shapes. ⛔ §7 <c>N2</c>: *"do not invent a second mechanism."</para>
///
/// <para>⛔⛔ <b>DEVIATION FROM §4b's LAYOUT, and it is forced.</b> §4b writes
/// <c>Goldens/&lt;scenario&gt;/&lt;panelId&gt;.json</c>. 📐 Measured `2026-08-24`: <b>a panel id can contain
/// a slash</b> — <c>editor/_gizmo</c> is a real captured id, and using it verbatim threw
/// <c>DirectoryNotFoundException</c> on the very first capture attempt. ⇒ ⭐ the id is encoded
/// <c>/</c> → <c>~</c>, and <see cref="FileNameFor"/> is the single place that knows it.
/// ⭐⭐ <b><c>~</c> is chosen because it is injective here and CHECKED to be</b>: no captured panel id
/// contains it *(measured over all 41)*, and <c>PanelGoldenRails</c> asserts that for every budgeted id —
/// ⛔ an encoding that silently mapped two ids onto one file would overwrite one golden with another and be
/// undetectable from the file alone.</para>
/// </summary>
public static class GoldenStore
{
    /// <summary>
    /// ⭐ <c>PANEL_GOLDEN_CAPTURE=1</c> writes the goldens instead of asserting against them.
    /// ⚠ <b>Capture is never the default and must never run in CI</b> — a capture run is green by
    /// construction, which is §3's *"re-blessed in bulk"* failure mode with the safety off.
    /// </summary>
    public static bool CaptureMode =>
        string.Equals(Environment.GetEnvironmentVariable("PANEL_GOLDEN_CAPTURE"), "1", StringComparison.Ordinal);

    /// <summary>Absolute path of the committed <c>Goldens/</c> directory — this source file's own directory.</summary>
    public static string Dir([CallerFilePath] string thisFile = "")
        => Path.GetDirectoryName(thisFile)!;

    /// <summary>⭐ The one place that knows how a panel id becomes a filename. See the class remarks.</summary>
    public static string FileNameFor(string panelId) => panelId.Replace('/', '~') + ".json";

    /// <summary>⭐ <c>Goldens/&lt;scenario&gt;/&lt;encoded panel id&gt;.json</c> *(§4b, with the encoding above)*.</summary>
    public static string PathFor(string scenario, string panelId)
        => Path.Combine(Dir(), scenario, FileNameFor(panelId));

    /// <summary>
    /// ⭐⭐⭐ <b>Compare, or write in capture mode.</b> Returns the differing JSON paths — ⭐ empty means the
    /// panel is unchanged.
    /// </summary>
    /// <exception cref="FileNotFoundException">
    /// ⛔ No golden on disk and not in capture mode. ⚠ <b>Deliberately an ERROR, not an auto-capture:</b> a
    /// store that silently wrote a missing baseline would make every new panel pass on its first run and
    /// bless whatever it happened to publish — 📌 §3's first failure mode *(a golden nobody has seen fail)*
    /// arriving through the back door.
    /// </exception>
    public static IReadOnlyList<string> CompareOrWrite(string scenario, string panelId, JsonNode? model)
    {
        var path = PathFor(scenario, panelId);
        var text = PanelNormalizer.Canonical(model);

        if (CaptureMode)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return Array.Empty<string>();
        }

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"No golden for panel '{panelId}' of scenario '{scenario}' at {path}. "
              + "Run the suite once with PANEL_GOLDEN_CAPTURE=1 to capture it, INSPECT the produced file, "
              + "and commit it (DESIGN_Regression_Net.md §7 N2).", path);

        var golden = JsonNode.Parse(File.ReadAllText(path));
        return PanelNormalizer.Diff(golden, model);
    }

    /// <summary>⭐ The committed goldens for one scenario, as <c>(panelId-ish file name, parsed model)</c>.</summary>
    public static IEnumerable<(string File, JsonNode? Model)> Committed(string scenario)
    {
        var dir = Path.Combine(Dir(), scenario);
        if (!Directory.Exists(dir)) yield break;

        foreach (var f in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            yield return (Path.GetFileName(f), JsonNode.Parse(File.ReadAllText(f)));
    }
}
