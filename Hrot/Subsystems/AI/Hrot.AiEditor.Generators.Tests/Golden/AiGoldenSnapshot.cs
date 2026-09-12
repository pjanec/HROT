using System;
using System.IO;
using System.Linq;

namespace Hrot.AiEditor.Generators.Tests.Golden;

/// <summary>
/// ⭐ Read-or-regenerate for the AI golden baselines, mirroring
/// <c>Hrot.Blueprints.Tests.TestData.ReadOrRegenerateSnapshot</c>.
///
/// <para>
/// ⛔ <b>A DISTINCT environment variable, deliberately.</b> Reusing
/// <c>BLUEPRINT_REGENERATE_SNAPSHOTS</c> would mean regenerating the blueprint set also silently
/// rewrote the AI baselines — two corpora moving on one intent, which is exactly the *"regenerate and
/// move on"* habit a golden gate exists to prevent.
/// </para>
/// </summary>
public static class AiGoldenSnapshot
{
    public const string RegenerateVariable = "AI_REGENERATE_SNAPSHOTS";

    /// <summary>The <c>Snapshots/</c> directory beside this test project's sources.</summary>
    public static string ResolveSnapshotsDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(
                dir, "Hrot", "Subsystems", "AI", "Hrot.AiEditor.Generators.Tests", "Snapshots");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Path.Combine(
                dir, "Hrot", "Subsystems", "AI", "Hrot.AiEditor.Generators.Tests");
            if (Directory.Exists(parent))
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            "Snapshots directory not found — expected "
            + "Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Snapshots.");
    }

    public static void ReadOrRegenerate(string relativePath, string actual)
    {
        var path = Path.Combine(ResolveSnapshotsDir(), relativePath);
        if (Environment.GetEnvironmentVariable(RegenerateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Baseline not found: '{path}'. Set {RegenerateVariable}=1 to create it.", path);

        // Normalise line endings so the comparison survives CRLF/LF checkout settings.
        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        var got      = actual.Replace("\r\n", "\n");
        if (expected == got) return;

        throw new Exception(DescribeMismatch(relativePath, expected, got));
    }

    /// <summary>
    /// ⭐ <b>Leads with the FIRST DIFFERING LINE.</b> A failure message nobody can read trains everyone
    /// to regenerate — the one outcome a golden gate must not produce.
    /// </summary>
    private static string DescribeMismatch(string relativePath, string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        int i = 0;
        while (i < e.Length && i < a.Length && e[i] == a[i]) i++;

        var context = string.Join("\n", Enumerable
            .Range(Math.Max(0, i - 3), Math.Min(7, Math.Max(e.Length, a.Length) - Math.Max(0, i - 3)))
            .Select(n => $"  {n + 1,5} | expected: {(n < e.Length ? e[n] : "<eof>")}\n"
                       + $"        |   actual: {(n < a.Length ? a[n] : "<eof>")}"));

        return $"⛔ The AI golden baseline '{relativePath}' moved.\n\n"
             + $"First difference at line {i + 1} (expected {e.Length} lines, got {a.Length}):\n"
             + context
             + $"\n\nIf the change was INTENDED, regenerate with {RegenerateVariable}=1 and say so in "
             + "the commit — and show that the diff is only what you meant to change.";
    }
}
