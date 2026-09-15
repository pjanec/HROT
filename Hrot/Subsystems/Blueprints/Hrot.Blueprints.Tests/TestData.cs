using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests;

/// <summary>
/// Helpers for loading test assets from the TestAssets/ directory.
/// </summary>
public static class TestData
{
    /// <summary>
    /// <b>U-15 — the node discriminators in a serialized asset, read from the DOM.</b>
    ///
    /// <para>
    /// ⭐ <b>Why this exists.</b> Four tests asserted <c>Assert.Contains("\"kind\":\"When\"", json)</c>
    /// — a substring of the <b>compact</b> spelling. ⛔ Making the canonical on-disk form indented
    /// turned that into <c>"kind": "When"</c> and reddened 57 test cases across 5 methods, none of
    /// which was about formatting. ⚠ <b>A test that asserts a JSON substring is coupled to whitespace
    /// for no reason it ever chose;</b> re-coupling them to the new spelling would only move the trap.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> NodeDiscriminatorsIn(string json)
    {
        var found = new List<string>();
        Walk(System.Text.Json.Nodes.JsonNode.Parse(json));
        return found;

        void Walk(System.Text.Json.Nodes.JsonNode? node)
        {
            switch (node)
            {
                case System.Text.Json.Nodes.JsonObject o:
                    if (o.TryGetPropertyValue("kind", out var k) && k is not null)
                        found.Add(k.GetValue<string>());
                    foreach (var (_, v) in o) Walk(v);
                    return;
                case System.Text.Json.Nodes.JsonArray a:
                    foreach (var v in a) Walk(v);
                    return;
            }
        }
    }

    public static class SampleAssets
    {
        public const string LibraryMath                  = "LibraryMath";
        public const string InstanceCounter              = "InstanceCounter";
        public const string InstanceCounterV1ModifiedBody = "InstanceCounterV1ModifiedBody";
        public const string InstanceCounterV2WithBonus   = "InstanceCounterV2WithBonus";
        public const string HealthRegen                  = "HealthRegen";
        public const string HasVisibleTarget             = "HasVisibleTarget";
        public const string MoveToAndFire                = "MoveToAndFire";
        public const string DoorActor                    = "DoorActor";
        public const string DoorSensor                   = "DoorSensor";
        public const string CountingDemo                 = "CountingDemo";
        // Frozen copy of the debugger CF-test asset. Decoupled from the user's editable
        // scratch Count4.bp.json under Hrot.AI.Behaviors/Blueprints (see TestAssets/Count4.bp.json).
        public const string Count4                       = "Count4";
    }

    public static BlueprintAsset LoadAsset(string name)
    {
        var path = Path.Combine(ResolveTestAssetsDir(), name + ".bp.json");
        var json = File.ReadAllText(path);
        return BlueprintJsonServices.Deserialize(json)
            ?? throw new InvalidDataException($"Deserialized null from '{path}'");
    }

    public static string LoadSnapshot(string relativePath)
    {
        var path = Path.Combine(ResolveSnapshotsDir(), relativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Snapshot not found: '{path}'", path);
        return File.ReadAllText(path);
    }

    /// <summary>
    /// When BLUEPRINT_REGENERATE_SNAPSHOTS=1 env var is set, writes the snapshot.
    /// Otherwise compares the content against the stored snapshot.
    /// Does NOT use Xunit.Assert -- throws Exception on mismatch so it works outside test context too.
    /// </summary>
    public static void ReadOrRegenerateSnapshot(string relativePath, string actual)
    {
        var path = Path.Combine(ResolveSnapshotsDir(), relativePath);
        if (Environment.GetEnvironmentVariable("BLUEPRINT_REGENERATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }
        else
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"Snapshot not found: '{path}'. Set BLUEPRINT_REGENERATE_SNAPSHOTS=1 to create.", path);
            var expected = File.ReadAllText(path);
            // Normalize line endings on both sides so the comparison is
            // robust regardless of git checkout settings (CRLF vs LF).
            var expectedNormalized = expected.Replace("\r\n", "\n");
            var actualNormalized   = actual.Replace("\r\n", "\n");
            if (expectedNormalized != actualNormalized)
                throw new Exception(DescribeMismatch(relativePath, expectedNormalized, actualNormalized));
        }
    }

    /// <summary>
    /// U-1 — the mismatch report. ⭐ <b>Leads with the FIRST DIFFERING LINE and its context</b>, then
    /// inlines both files only when they are small enough to read.
    ///
    /// <para>
    /// ⚠ <b>Why this changed.</b> It used to inline both files unconditionally. Fine for a 3 KB
    /// emit snapshot; ⛔ for the 42-asset golden sweep (~250 KB of baseline) a single failure floods
    /// the test output and buries the one line that moved. <i>A failure message nobody can read is a
    /// harness that reports its finding to nobody.</i>
    /// </para>
    /// </summary>
    private static string DescribeMismatch(string relativePath, string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');

        int i = 0;
        while (i < e.Length && i < a.Length && e[i] == a[i]) i++;

        var sb = new System.Text.StringBuilder();
        sb.Append("Snapshot mismatch for '").Append(relativePath).Append("'.\n");
        sb.Append("First difference at line ").Append(i + 1)
          .Append(" (expected ").Append(e.Length).Append(" lines, actual ").Append(a.Length).Append(").\n");

        for (int c = Math.Max(0, i - 3); c < Math.Min(Math.Max(e.Length, a.Length), i + 4); c++)
        {
            var marker = c == i ? ">>" : "  ";
            if (c < e.Length) sb.Append(marker).Append(" expected[").Append(c + 1).Append("]: ").Append(e[c]).Append('\n');
            if (c < a.Length) sb.Append(marker).Append(" actual  [").Append(c + 1).Append("]: ").Append(a[c]).Append('\n');
        }

        // Small snapshots keep the old behaviour — for a 3 KB file the whole text IS the useful report.
        const int InlineBudget = 4000;
        if (expected.Length + actual.Length <= InlineBudget)
            sb.Append("--- expected ---\n").Append(expected).Append("\n--- actual ---\n").Append(actual);
        else
            sb.Append("(both files elided: ").Append(expected.Length + actual.Length)
              .Append(" chars exceeds the ").Append(InlineBudget)
              .Append("-char inline budget. Set BLUEPRINT_REGENERATE_SNAPSHOTS=1 and diff with git.)");

        return sb.ToString();
    }

    /// <summary>
    /// Walk up from the current directory to find TestAssets/.
    /// Works both in bin/ output and when run from repo root.
    /// </summary>
    public static string ResolveTestAssetsDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "TestAssets");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            "TestAssets directory not found. Ensure CopyToOutputDirectory=PreserveNewest in .csproj.");
    }

    /// <summary>
    /// The committed <c>Snapshots/</c> directory — ⭐ <b>the one in the SOURCE tree, not the copy in
    /// <c>bin/</c></b>.
    ///
    /// <para>
    /// ⛔ <b>The defect this fixes, found while building U-1.</b> This walked up from
    /// <see cref="AppContext.BaseDirectory"/> and stopped at the first <c>Snapshots</c> it found —
    /// which is always <c>bin/Debug/net8.0/Snapshots</c>, the build's own copy. Reading from it was
    /// harmless (<c>PreserveNewest</c> keeps it in step), but <b>regenerating wrote there</b>: a
    /// baseline created with <c>BLUEPRINT_REGENERATE_SNAPSHOTS=1</c> landed in <c>bin/</c>, the
    /// subsequent test run compared against it and passed, and <b>nothing was ever added to git</b>.
    /// ⚠ For a NEW baseline the failure mode is silent and total — green locally, *"snapshot not
    /// found"* on a clean checkout, which reads as a missing file rather than a broken path. A
    /// 42-asset sweep is exactly the size at which nobody notices by eye.
    /// </para>
    ///
    /// <para>
    /// ⭐ Resolved by finding the test project's own directory (the <c>.csproj</c> is the anchor) and
    /// falling back to the historical bin-walk if that is ever not on the path.
    /// </para>
    /// </summary>
    public static string ResolveSnapshotsDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Hrot.Blueprints.Tests.csproj")))
            {
                var source = Path.Combine(dir, "Snapshots");
                if (Directory.Exists(source))
                    return source;
            }
            dir = Path.GetDirectoryName(dir);
        }

        // Fallback: the pre-existing behaviour, for a layout where the project dir is not an ancestor.
        dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "Snapshots");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Snapshots directory not found.");
    }
}
