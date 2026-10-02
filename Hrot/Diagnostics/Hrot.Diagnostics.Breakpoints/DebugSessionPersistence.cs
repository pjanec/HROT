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

    /// <summary>
    /// ⭐⭐ <b><c>BP-502</c> — the PINNED VARIABLE rows of the Watch window.</b>
    /// 📄 <c>DESIGN_Variable_Watch_Pinning.md</c> §5.
    ///
    /// <para>⛔ A FOURTH list, deliberately — ⚠ <see cref="Watches"/> is the blueprint PIN watch
    /// *(<c>AssetId/GraphId/PinId</c>)* and <see cref="DataBreakpoints"/> carries the breakpoint watches
    /// *(<c>IsWatch</c>)*. 📌 <c>AiWatchWindow</c>'s own remarks count <b>three</b> watch-shaped things in
    /// this codebase; a pinned variable row is the fourth, and merging any of them is a design question,
    /// not a persistence one.</para>
    ///
    /// <para>⭐ Same file, no new one — §5: <i>"do not invent a second file"</i>.</para>
    /// </summary>
    public List<PinnedVariableEntry> PinnedVariables { get; set; } = new();
}

/// <summary>
/// ⭐⭐⭐ <b>One pinned variable row — <c>BP-502</c>, keyed by what SURVIVES a session.</b>
/// 📄 §5 *(<c>R-75</c>)* · §3 *(the two binding kinds)*.
///
/// <para>⛔⛔ <b>No <c>Entity</c> here, and that is the whole design.</b> An <c>Entity</c> is a
/// slot+generation handle that the repository <b>recycles</b> — writing one to a file and reading it back
/// would point the row at whatever now occupies that slot. ⇒ ⭐ a concrete pin stores
/// <see cref="NetworkId"/>; a chameleon stores nothing at all, because it is bound to a ROLE
/// *("whoever is selected")* rather than to an entity.</para>
///
/// <para>⚠⚠ <b>A concrete pin does NOT yet survive a scenario RESTART.</b> §5 keys it on the STAGING id
/// and re-resolves through <c>StagingEntityExtractor</c>'s <c>oldToNewMap</c>; that map is still a local
/// inside the extractor. ⇒ this stores the RUNTIME <c>NetworkIdentity</c>, which survives a save/reload of
/// the session but not a re-load of the scenario. ⭐ Stated here because a reader must not mistake
/// *"persisted"* for *"restart-proof"*.</para>
/// </summary>
public sealed class PinnedVariableEntry
{
    public Guid   AssetId      { get; set; }
    public string Section      { get; set; } = "";
    public string VariablePath { get; set; } = "";

    /// <summary>⚠ Display text, restored so a stale row can still name itself. ⛔ Not identity.</summary>
    public string AssetName    { get; set; } = "";

    /// <summary>⭐ <c>"Concrete"</c> or <c>"Chameleon"</c> — the string, so an unknown future kind
    /// round-trips instead of silently becoming the enum's zero value.</summary>
    public string BindingKind  { get; set; } = "Concrete";

    /// <summary>⭐ The durable entity id for a concrete pin; <c>0</c> for a chameleon.</summary>
    public long   NetworkId    { get; set; }
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
    /// <param name="pinnedVariables">
    /// ⭐⭐ <b><c>BP-502</c> — the Watch window's pinned variable rows</b>, from
    /// <c>PinnedVariableRowSource.PinnedWithBindings()</c> mapped to <see cref="PinnedVariableEntry"/>.
    /// ⚠ Optional so the existing three-list callers keep working unchanged — ⛔ but a caller that HAS a
    /// pin source must pass it *(the silent-default rule)*.
    /// </param>
    public static void Save(
        IReadOnlyList<Hrot.Blueprints.Core.Debug.Breakpoint> nodeBreakpoints,
        IReadOnlyList<Hrot.Blueprints.Core.Debug.Watch> watches,
        IReadOnlyList<Breakpoint> dbmBreakpoints,
        string path,
        IReadOnlyList<PinnedVariableEntry>? pinnedVariables = null)
    {
        var file = new DebugSessionFile();

        if (pinnedVariables != null)
            file.PinnedVariables.AddRange(pinnedVariables);

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
