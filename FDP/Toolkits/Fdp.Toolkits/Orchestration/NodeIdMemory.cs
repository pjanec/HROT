using System;
using System.IO;

namespace Fdp.Toolkit.Orchestration;

/// <summary>
/// ⭐ CE-4331 ⑥ (docs/DESIGN_Recording_Storage.md §9b) — what a node REMEMBERS of its id: one small file per kind under the staging root, <c>node-ids/&lt;subsystem-lower&gt;.id</c>, one integer. The runner reads it before asking the master
/// (the value goes out as the request's <c>PreferredId</c> — granted again when free) and writes it after a grant that was not refused, so a relaunched node comes back as the same node. ⛔ A remembered id is a PREFERENCE: the master decides.
/// </summary>
public static class NodeIdMemory
{
    /// <summary>The folder under the staging root.</summary>
    public const string DirectoryName = "node-ids";

    /// <summary>The file of <paramref name="subsystemName"/>'s remembered id.</summary>
    public static string PathOf(string stagingRoot, string subsystemName)
        => Path.Combine(stagingRoot, DirectoryName, subsystemName.Trim().ToLowerInvariant() + ".id");

    /// <summary>The remembered id, or 0 (none) when the file is absent, unreadable or not a positive integer.</summary>
    public static int Read(string stagingRoot, string subsystemName)
    {
        if (string.IsNullOrWhiteSpace(stagingRoot) || string.IsNullOrWhiteSpace(subsystemName)) return 0;
        try
        {
            var path = PathOf(stagingRoot, subsystemName);
            return File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) && id > 0 ? id : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return 0; }
    }

    /// <summary>Remembers <paramref name="nodeId"/> for the kind (best effort: a failure to write costs only the memory, never the launch).</summary>
    public static void Write(string stagingRoot, string subsystemName, int nodeId)
    {
        if (nodeId <= 0 || string.IsNullOrWhiteSpace(stagingRoot) || string.IsNullOrWhiteSpace(subsystemName)) return;
        try
        {
            var path = PathOf(stagingRoot, subsystemName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, nodeId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
    }
}
