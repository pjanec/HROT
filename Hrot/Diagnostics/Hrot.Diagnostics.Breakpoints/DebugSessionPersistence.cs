using System.Text.Json;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

// ──────────────────────────────────────────────────────────────────────────
// DTOs — full debug session file model
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// Top-level container for a persisted debug session.
/// </summary>
public sealed class DebugSessionFile
{
    public List<NodeBreakpointEntry> NodeBreakpoints { get; set; } = new();
    public List<DataBreakpointEntry> DataBreakpoints { get; set; } = new();
    public List<WatchEntry> Watches { get; set; } = new();
}

/// <summary>
/// Persisted node breakpoint — keyed by authored node id, not probe id.
/// The authored id is the durable key; it re-translates via BreakpointTargets on restore.
/// </summary>
public sealed class NodeBreakpointEntry
{
    public Guid AssetId { get; set; }
    public Guid GraphId { get; set; }
    public Guid NodeId { get; set; }  // authored node id
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Persisted data breakpoint. Stores the predicate DTO so it can be recompiled
/// via <see cref="PredicateCompiler"/> on load — never serializes the compiled delegate.
/// <see cref="Breakpoint.FilterEntity"/> is NOT persisted (entity references are runtime-only).
/// </summary>
public sealed class DataBreakpointEntry
{
    public SearchPredicateDto? Condition { get; set; }
    public string DisplayName { get; set; } = "";
    public Guid? SourceElementId { get; set; }
    public bool Enabled { get; set; } = true;
    public bool IsWatch { get; set; }
}

/// <summary>
/// Persisted watch — extends the old WatchPersistence concept with full identity for restore.
/// </summary>
public sealed class WatchEntry
{
    public Guid AssetId { get; set; }
    public Guid GraphId { get; set; }
    public Guid PinId { get; set; }
    public string DisplayName { get; set; } = "";
    /// <summary>Assembly-qualified name of <see cref="System.Type"/> for <c>Type.GetType()</c>.</summary>
    public string ExpectedTypeName { get; set; } = "";
}

/// <summary>
/// Persists and restores the full debug session: node breakpoints, data breakpoints (with
/// their JIT-compiled conditions as DTOs), and watches. Generalizes the older
/// <see cref="WatchPersistence"/> which only saved watch-flagged entries.
/// </summary>
public static class DebugSessionPersistence
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        IncludeFields = true,
    };

    /// <summary>
    /// Serializes the complete debug session to <paramref name="path"/>.
    /// Creates or overwrites the file.
    /// </summary>
    /// <param name="nodeBreakpoints">Node breakpoints from <c>BlueprintDebugSession.GetBreakpoints()</c>.</param>
    /// <param name="watches">Watches from <c>BlueprintDebugSession.GetWatches()</c>.</param>
    /// <param name="dbmBreakpoints">All breakpoints from <c>DataBreakpointManager.AllBreakpoints</c>.</param>
    public static void Save(
        IReadOnlyList<Hrot.Blueprints.Core.Debug.Breakpoint> nodeBreakpoints,
        IReadOnlyList<Hrot.Blueprints.Core.Debug.Watch> watches,
        IReadOnlyList<Breakpoint> dbmBreakpoints,
        string path)
    {
        var file = new DebugSessionFile();

        // Collect node breakpoints.
        foreach (var bp in nodeBreakpoints)
        {
            if (!Guid.TryParse(bp.NodeId, out var authoredNodeId))
                continue;

            file.NodeBreakpoints.Add(new NodeBreakpointEntry
            {
                AssetId = bp.AssetId,
                GraphId = bp.GraphId,
                NodeId  = authoredNodeId,
                Enabled = bp.Enabled,
            });
        }

        // Collect watches. ExpectedType is System.Type — not serializable.
        // Store AssemblyQualifiedName as string.
        foreach (var w in watches)
        {
            file.Watches.Add(new WatchEntry
            {
                AssetId          = w.AssetId,
                GraphId          = w.GraphId,
                PinId            = w.PinId,
                DisplayName      = w.DisplayName,
                ExpectedTypeName = w.ExpectedType.AssemblyQualifiedName ?? w.ExpectedType.FullName ?? "",
            });
        }

        // Collect DBM breakpoints — filter out ExternalHitTagPredicateDto-only entries.
        // Those are node-breakpoint forwards created by BlueprintDebugSession.SetBreakpoint;
        // they will be recreated on restore via session.SetBreakpoint.
        foreach (var bp in dbmBreakpoints)
        {
            if (bp.Condition is ExternalHitTagPredicateDto)
                continue;

            file.DataBreakpoints.Add(new DataBreakpointEntry
            {
                Condition       = bp.Condition,
                DisplayName     = bp.DisplayName,
                SourceElementId = bp.SourceElementId,
                Enabled         = bp.Enabled,
                IsWatch         = bp.IsWatch,
            });
        }

        var json = JsonSerializer.Serialize(file, s_options);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Deserializes a debug session file from <paramref name="path"/>.
    /// Returns <c>null</c> if the file does not exist or is malformed.
    /// </summary>
    public static DebugSessionFile? TryLoad(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var json   = File.ReadAllText(path);
            var result = JsonSerializer.Deserialize<DebugSessionFile>(json, s_options);
            return result;
        }
        catch
        {
            return null;
        }
    }
}
