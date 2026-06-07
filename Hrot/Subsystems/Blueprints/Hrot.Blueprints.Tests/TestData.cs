using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests;

/// <summary>
/// Helpers for loading test assets from the TestAssets/ directory.
/// </summary>
public static class TestData
{
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
                throw new Exception(
                    $"Snapshot mismatch for '{relativePath}'.\n--- expected ---\n{expected}\n--- actual ---\n{actual}");
        }
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

    private static string ResolveSnapshotsDir()
    {
        var dir = AppContext.BaseDirectory;
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
