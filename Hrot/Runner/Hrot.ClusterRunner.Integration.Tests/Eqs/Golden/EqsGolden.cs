using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs.Golden;

/// <summary>
/// Serializable Top-K row captured from an <see cref="EqsCognitiveBuffer"/>.
///
/// <para>P3D-001 / P3D-403 (Axis-1, flat-terrain parity gate). Records only the fields that
/// must be invariant across the 3D promotion on flat terrain: <see cref="EntityId"/>,
/// <see cref="PositionX"/>, <see cref="PositionY"/>, <see cref="Score"/>, <see cref="Flags"/>,
/// <see cref="FlagsMeaningful"/>. <c>PositionZ</c> is intentionally NOT recorded — it is the
/// only new information the promotion adds, and on flat ground it is a constant ≈0 (asserted
/// separately by the parity gate).</para>
/// </summary>
public sealed class EqsGoldenRow
{
    public long EntityId { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float Score { get; set; }
    public int Flags { get; set; }
    public int FlagsMeaningful { get; set; }
}

/// <summary>Golden capture for a single EQS starter template.</summary>
public sealed class EqsGoldenTemplate
{
    public string Name { get; set; } = "";
    public uint BlueprintId { get; set; }
    public int Count { get; set; }
    public List<EqsGoldenRow> Rows { get; set; } = new();
    /// <summary>Max absolute PositionZ observed across rows (flat-terrain expectation: ≈0).</summary>
    public float MaxAbsPositionZ { get; set; }
}

/// <summary>
/// Shared infrastructure for the flat-terrain golden baseline (P3D-001) and the parity
/// assertion (P3D-403). Locates the committed golden directory via <see cref="CallerFilePathAttribute"/>
/// so the path is machine-independent, and provides capture/compare with float tolerance.
/// </summary>
public static class EqsGolden
{
    /// <summary>
    /// Float tolerance for the parity comparison. On flat terrain the 3D promotion only adds a
    /// constant Z, so X/Y/Score must match to within ordinary float round-trip noise.
    /// </summary>
    public const float Tolerance = 1e-4f;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// True when the harness should (re)write the golden artifacts instead of asserting against
    /// them. Set the environment variable <c>EQS_GOLDEN_CAPTURE=1</c> to capture (P3D-001).
    /// </summary>
    public static bool CaptureMode =>
        string.Equals(Environment.GetEnvironmentVariable("EQS_GOLDEN_CAPTURE"), "1", StringComparison.Ordinal);

    /// <summary>Absolute path to the committed golden directory (next to this source file).</summary>
    public static string GoldenDir([CallerFilePath] string thisFile = "")
        => Path.GetDirectoryName(thisFile)!;

    private static string GoldenPath(string templateName)
        => Path.Combine(GoldenDir(), $"{templateName}.flat.golden.json");

    /// <summary>
    /// Discovers every type annotated with <see cref="EqsTemplateAttribute"/> in the loaded
    /// toolkit assemblies. The count is NOT hardcoded (P3D-001 success condition 1): adding a new
    /// starter template surfaces here and forces a matching scenario + golden.
    /// </summary>
    public static IReadOnlyList<Type> DiscoverEqsTemplateTypes()
    {
        // Force the toolkit assembly to load by touching a known template type.
        _ = typeof(FindCoverFromTarget);

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
            })
            .Where(t => t!.GetCustomAttribute<EqsTemplateAttribute>() != null)
            .Distinct()
            .OrderBy(t => t!.FullName, StringComparer.Ordinal)
            .ToList()!;
    }

    /// <summary>Writes a golden artifact (capture mode, P3D-001).</summary>
    public static void Write(EqsGoldenTemplate golden)
    {
        var json = JsonSerializer.Serialize(golden, JsonOpts);
        File.WriteAllText(GoldenPath(golden.Name), json, new UTF8Encoding(false));
    }

    /// <summary>Reads a committed golden artifact (compare mode, P3D-403).</summary>
    public static EqsGoldenTemplate Read(string templateName)
    {
        var path = GoldenPath(templateName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Golden baseline '{templateName}' not found at {path}. " +
                "Run with EQS_GOLDEN_CAPTURE=1 to capture it (P3D-001).", path);
        return JsonSerializer.Deserialize<EqsGoldenTemplate>(File.ReadAllText(path))!;
    }

    /// <summary>
    /// Compares a freshly-captured result against the committed golden, returning a list of
    /// human-readable mismatches (empty = parity holds). EntityId/Flags/Count compared exactly;
    /// X/Y/Score compared within <see cref="Tolerance"/>.
    /// </summary>
    public static List<string> Compare(EqsGoldenTemplate golden, EqsGoldenTemplate actual)
    {
        var diffs = new List<string>();
        if (golden.Count != actual.Count)
            diffs.Add($"Count: golden={golden.Count} actual={actual.Count}");

        int n = Math.Min(golden.Rows.Count, actual.Rows.Count);
        for (int i = 0; i < n; i++)
        {
            var g = golden.Rows[i];
            var a = actual.Rows[i];
            if (g.EntityId != a.EntityId)
                diffs.Add($"[{i}] EntityId: golden={g.EntityId} actual={a.EntityId}");
            if (MathF.Abs(g.PositionX - a.PositionX) > Tolerance)
                diffs.Add($"[{i}] PositionX: golden={g.PositionX:R} actual={a.PositionX:R}");
            if (MathF.Abs(g.PositionY - a.PositionY) > Tolerance)
                diffs.Add($"[{i}] PositionY: golden={g.PositionY:R} actual={a.PositionY:R}");
            if (MathF.Abs(g.Score - a.Score) > Tolerance)
                diffs.Add($"[{i}] Score: golden={g.Score:R} actual={a.Score:R}");
            if (g.Flags != a.Flags)
                diffs.Add($"[{i}] Flags: golden={g.Flags} actual={a.Flags}");
            if (g.FlagsMeaningful != a.FlagsMeaningful)
                diffs.Add($"[{i}] FlagsMeaningful: golden={g.FlagsMeaningful} actual={a.FlagsMeaningful}");
        }
        return diffs;
    }
}
