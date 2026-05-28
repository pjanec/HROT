using System;
using System.IO;

namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Shared helpers for computing and parsing sidecar file paths.
/// Used by both <see cref="InMemoryMigrationStorage"/> and
/// <see cref="FileSystemMigrationStorage"/>.
/// </summary>
internal static class SidecarFileHelper
{
    internal static string GetSidecarDirectory(string originalPath)
        => Path.Combine(Path.GetDirectoryName(originalPath)!, ".migration-snapshots");

    internal static string GetSnapshotFileName(string originalPath, int version, string hash16)
        => $"{Path.GetFileNameWithoutExtension(originalPath)}.v{version}.{hash16}.snapshot.json";

    internal static string GetJournalFileName(string originalPath, int version, string hash16)
        => $"{Path.GetFileNameWithoutExtension(originalPath)}.v{version}.{hash16}.unknowns.json";

    internal static bool TryParseSnapshotFileName(string fileName, string baseName,
        out int version, out string? hash)
    {
        version = 0;
        hash = null;

        const string suffix = ".snapshot.json";
        if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        var stem = fileName[..^suffix.Length];
        return TryParseStem(stem, baseName, out version, out hash);
    }

    internal static bool TryParseJournalFileName(string fileName, string baseName,
        out int version, out string? hash)
    {
        version = 0;
        hash = null;

        const string suffix = ".unknowns.json";
        if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        var stem = fileName[..^suffix.Length];
        return TryParseStem(stem, baseName, out version, out hash);
    }

    // stem is "{baseName}.v{N}.{hash16}"
    private static bool TryParseStem(string stem, string baseName,
        out int version, out string? hash)
    {
        version = 0;
        hash = null;

        var prefix = baseName + ".";
        if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = stem[prefix.Length..]; // "v{N}.{hash16}"
        var lastDot = rest.LastIndexOf('.');
        if (lastDot < 0)
            return false;

        hash = rest[(lastDot + 1)..];
        var versionPart = rest[..lastDot]; // "v{N}"
        if (versionPart.Length < 2 || versionPart[0] != 'v')
            return false;

        return int.TryParse(versionPart[1..], out version);
    }
}
