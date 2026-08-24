using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hrot.SystemTests.Goldens;

/// <summary>
/// ⭐⭐⭐ <b><c>N2</c> — the ONE canonical form a panel dump is stored and compared in, plus a diff that
/// NAMES THE JSON PATH.</b>
/// 📄 <c>docs/DESIGN_Regression_Net.md</c> §7 <c>N2</c> · §4b *(the golden key)* · §3 *(the failure modes)*.
///
/// <para>⭐⭐⭐ <b>THE IGNORE-LIST IS EMPTY, AND THAT IS A MEASUREMENT — not an omission.</b> 📐 Measured
/// `2026-08-24` over all <b>41</b> captured dumps of <c>hill-attack</c> across the four editor perspectives:
/// <list type="bullet">
/// <item>an absolute filesystem path appears in <b>exactly one</b> panel — <c>fdp_message_log</c>, whose kind
/// is already declared volatile by <c>N1</c> and is therefore never goldened;</item>
/// <item>a <c>"timestamp"</c> field appears in <b>that same panel only</b>; a <c>"frame"</c> field in
/// <c>editor_fdp_events</c> — also a declared-volatile kind.</item>
/// </list>
/// ⇒ ⭐⭐ <b>every panel eligible for a golden is free of wall-clock, machine-path and frame-counter
/// content</b>, so there is nothing to exempt. ⛔ <c>D6</c> caveat ① *(never normalise to hide
/// non-determinism)* is honoured by construction rather than by good intentions.</para>
///
/// <para>⭐⭐ <b>And the emptiness is CONTROLLED, not asserted in a comment</b> —
/// <c>PanelGoldenRails.No_golden_carries_machine_or_wall_clock_content</c> re-derives it from the committed
/// goldens on every run. ⇒ ⛔ the day a panel starts publishing a path or a timestamp, that rail reddens and
/// names it, instead of the ignore-list quietly growing. 📌 The same inversion <c>N1</c> used for
/// <c>VolatileKinds</c>, which caught its own author.</para>
///
/// <para>⭐ <b>What normalisation DOES do:</b> sort object keys ordinally, so a dictionary-order change in the
/// serializer is not a diff. ⛔ Array ORDER is preserved — an array's order is a claim about the world
/// *(entity rows, tree nodes, section order)* and reordering it is exactly the kind of change a golden
/// exists to catch.</para>
/// </summary>
public static class PanelNormalizer
{
    /// <summary>
    /// ⭐⭐⭐ <b>EMPTY, measured.</b> See the class remarks for the measurement and the rail that keeps it
    /// honest. ⛔ Adding an entry here is a design decision that needs the same argument
    /// <c>DeterminismRails.VolatileKinds</c> carries — not a way to make a red go away.
    /// </summary>
    public static readonly IReadOnlyList<string> IgnoredPaths = System.Array.Empty<string>();

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// ⭐ The stored/compared form: object keys sorted ordinally, arrays left alone, indented, LF endings.
    /// <para>⚠ <b>LF is forced.</b> The goldens are committed and read on both Linux *(this harness)* and
    /// Windows *(the developer's machine)* — ⛔ a CRLF round-trip would diff every line of every golden and
    /// teach everyone to re-bless in bulk, which is §3's second failure mode.</para>
    /// </summary>
    public static string Canonical(JsonNode? model)
        => (Sorted(model)?.ToJsonString(Indented) ?? "null").Replace("\r\n", "\n") + "\n";

    private static JsonNode? Sorted(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
            {
                var result = new JsonObject();
                foreach (var kv in o.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    result[kv.Key] = Sorted(kv.Value?.DeepClone());
                return result;
            }
            case JsonArray a:
            {
                var result = new JsonArray();
                foreach (var item in a) result.Add(Sorted(item?.DeepClone()));
                return result;
            }
            default:
                return node?.DeepClone();
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A diff that names the JSON PATH, which is <c>N2</c>'s whole done-when condition.</b>
    /// 📄 §7 <c>N2</c>: *"a deliberate one-field change produces a diff naming the JSON path, not a wall of
    /// text."*
    ///
    /// <para>⭐⭐ <b>Every difference, not the first</b> — 📌 the same rule <c>N1</c>'s
    /// <c>DifferingKeys</c> follows and §8 applies to the mutation proof: <i>"a mutation that reddens 40
    /// files is itself the finding."</i> One path and forty paths are different findings and must not print
    /// the same.</para>
    ///
    /// <para>⚠ A missing or extra KEY is reported as such rather than as a value change — a panel that
    /// stopped publishing a field is a different defect from one that publishes it differently.</para>
    /// </summary>
    public static IReadOnlyList<string> Diff(JsonNode? golden, JsonNode? actual)
    {
        var diffs = new List<string>();
        Walk("$", Sorted(golden), Sorted(actual), diffs);
        return diffs;
    }

    private const int MaxDiffs = 40;

    private static void Walk(string path, JsonNode? g, JsonNode? a, List<string> diffs)
    {
        if (diffs.Count >= MaxDiffs) return;

        if (g is null || a is null)
        {
            if (g?.ToJsonString() != a?.ToJsonString())
                diffs.Add($"{path}: golden={Short(g)} actual={Short(a)}");
            return;
        }

        if (g is JsonObject go && a is JsonObject ao)
        {
            foreach (var key in go.Select(kv => kv.Key).Except(ao.Select(kv => kv.Key), StringComparer.Ordinal))
                diffs.Add($"{path}.{key}: MISSING from actual (golden={Short(go[key])})");
            foreach (var key in ao.Select(kv => kv.Key).Except(go.Select(kv => kv.Key), StringComparer.Ordinal))
                diffs.Add($"{path}.{key}: NEW in actual (actual={Short(ao[key])})");

            foreach (var key in go.Select(kv => kv.Key).Intersect(ao.Select(kv => kv.Key), StringComparer.Ordinal)
                                  .OrderBy(k => k, StringComparer.Ordinal))
                Walk($"{path}.{key}", go[key], ao[key], diffs);
            return;
        }

        if (g is JsonArray ga && a is JsonArray aa)
        {
            if (ga.Count != aa.Count)
                diffs.Add($"{path}: length golden={ga.Count} actual={aa.Count}");

            for (int i = 0; i < Math.Min(ga.Count, aa.Count); i++)
                Walk($"{path}[{i}]", ga[i], aa[i], diffs);
            return;
        }

        if (!string.Equals(g.ToJsonString(), a.ToJsonString(), StringComparison.Ordinal))
            diffs.Add($"{path}: golden={Short(g)} actual={Short(a)}");
    }

    private static string Short(JsonNode? n)
    {
        var s = n?.ToJsonString() ?? "null";
        return s.Length <= 120 ? s : s[..117] + "...";
    }

    // ── the control's raw material ────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ Field names whose presence in a golden would mean the ignore-list can no longer be empty.
    /// ⛔ Deliberately a SHORT, named list — 📌 it is the claim <c>N1</c>'s <c>VolatileKinds</c> makes,
    /// pushed down to the field level.
    /// </summary>
    public static readonly string[] WallClockFieldNames = { "timestamp", "timestampUtc", "wallClock", "nowUtc" };

    /// <summary>⭐ Cheap textual probe for an absolute path — the one machine-dependency measured in the corpus.</summary>
    public static bool LooksLikeAbsolutePath(string value)
        => value.StartsWith("/home/", StringComparison.Ordinal)
        || value.StartsWith("/tmp/", StringComparison.Ordinal)
        || value.StartsWith("/var/", StringComparison.Ordinal)
        || (value.Length > 2 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/'));

    /// <summary>⭐ Every <c>path → value</c> leaf in a dump, for the control rail to inspect.</summary>
    public static IEnumerable<(string Path, JsonNode? Value)> Leaves(JsonNode? node, string path = "$")
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var kv in o)
                    foreach (var leaf in Leaves(kv.Value, $"{path}.{kv.Key}"))
                        yield return leaf;
                break;
            case JsonArray a:
                for (int i = 0; i < a.Count; i++)
                    foreach (var leaf in Leaves(a[i], $"{path}[{i}]"))
                        yield return leaf;
                break;
            default:
                yield return (path, node);
                break;
        }
    }
}
