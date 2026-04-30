using System.Linq;
using CarKinem.Road;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Modules.Geographic.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Services;

using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Renderers;

// ── GlobalTime ────────────────────────────────────────────────────────────────

/// <summary>
/// Read-only renderer for the <see cref="GlobalTime"/> singleton.
/// Editing simulation time would break deterministic execution, so this renderer
/// returns <c>true</c> to block the default editable property tree entirely.
/// </summary>
[ImGuiRenderer(typeof(GlobalTime))]
public sealed class GlobalTimeRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var t = (GlobalTime)value;
        return $"Frame: {t.FrameNumber}  |  {t.TotalTime:F2}s";
    }

    public bool RenderValue(object value)
    {
        var t = (GlobalTime)value;
        ImGuiApi.TextUnformatted($"Frame Number         : {t.FrameNumber}");
        ImGuiApi.TextUnformatted($"Total Time           : {t.TotalTime:F3} s");
        ImGuiApi.TextUnformatted($"Delta Time           : {t.DeltaTime:F4} s");
        ImGuiApi.TextUnformatted($"Unscaled Delta       : {t.UnscaledDeltaTime:F4} s");
        ImGuiApi.TextUnformatted($"Time Scale           : {t.TimeScale:F2}x");
        // Returning true blocks ImGuiPropertyTree from generating editable leaves.
        return true;
    }
}

// ── PathfindingBatchData ──────────────────────────────────────────────────────

/// <summary>
/// Read-only renderer for the <see cref="PathfindingBatchData"/> singleton.
/// Editing live NativeArray pointers or the frame counter would cause
/// out-of-bounds memory corruption.
/// </summary>
[ImGuiRenderer(typeof(PathfindingBatchData))]
public sealed class PathfindingBatchDataRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var b = (PathfindingBatchData)value;
        return $"{b.Count} active  /  {b.Requests.Length} capacity";
    }

    public bool RenderValue(object value)
    {
        var b = (PathfindingBatchData)value;
        ImGuiApi.TextDisabled("Unmanaged Batch Buffers (read-only)");
        ImGuiApi.Separator();
        ImGuiApi.TextUnformatted($"Active this frame    : {b.Count}");
        ImGuiApi.TextUnformatted($"Requests capacity    : {b.Requests.Length}");
        ImGuiApi.TextUnformatted($"Results  capacity    : {b.Results.Length}");
        return true;
    }
}

// ── TerrainQueryBatchData ─────────────────────────────────────────────────────

/// <summary>
/// Read-only renderer for the <see cref="TerrainQueryBatchData"/> singleton.
/// </summary>
[ImGuiRenderer(typeof(TerrainQueryBatchData))]
public sealed class TerrainQueryBatchDataRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var b = (TerrainQueryBatchData)value;
        return $"{b.Count} active  /  {b.Requests.Length} capacity";
    }

    public bool RenderValue(object value)
    {
        var b = (TerrainQueryBatchData)value;
        ImGuiApi.TextDisabled("Unmanaged Batch Buffers (read-only)");
        ImGuiApi.Separator();
        ImGuiApi.TextUnformatted($"Active this frame    : {b.Count}");
        ImGuiApi.TextUnformatted($"Requests capacity    : {b.Requests.Length}");
        ImGuiApi.TextUnformatted($"Results  capacity    : {b.Results.Length}");
        return true;
    }
}

// ── RaycastBatchData ──────────────────────────────────────────────────────────

/// <summary>
/// Read-only renderer for the <see cref="RaycastBatchData"/> singleton.
/// </summary>
[ImGuiRenderer(typeof(RaycastBatchData))]
public sealed class RaycastBatchDataRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var b = (RaycastBatchData)value;
        return $"{b.Count} active  /  {b.Requests.Length} capacity";
    }

    public bool RenderValue(object value)
    {
        var b = (RaycastBatchData)value;
        ImGuiApi.TextDisabled("Unmanaged Batch Buffers (read-only)");
        ImGuiApi.Separator();
        ImGuiApi.TextUnformatted($"Active this frame    : {b.Count}");
        ImGuiApi.TextUnformatted($"Requests capacity    : {b.Requests.Length}");
        ImGuiApi.TextUnformatted($"Hits     capacity    : {b.Hits.Length}");
        return true;
    }
}

// ── ZoneEnvironmentData ───────────────────────────────────────────────────────

/// <summary>
/// Read-only renderer for the <see cref="ZoneEnvironmentData"/> singleton.
/// Displays road-network statistics rather than dumping native array pointers.
/// </summary>
[ImGuiRenderer(typeof(ZoneEnvironmentData))]
public sealed class ZoneEnvironmentDataRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var d = (ZoneEnvironmentData)value;
        ref readonly RoadNetworkBlob rn = ref d.RoadNetwork;
        int nodes = rn.Nodes.IsCreated    ? rn.Nodes.Length    : 0;
        int segs  = rn.Segments.IsCreated ? rn.Segments.Length : 0;
        return $"Nodes: {nodes}  |  Segments: {segs}";
    }

    public bool RenderValue(object value)
    {
        var d = (ZoneEnvironmentData)value;
        ref readonly RoadNetworkBlob rn = ref d.RoadNetwork;

        ImGuiApi.TextDisabled("Road Network (read-only)");
        ImGuiApi.Separator();
        ImGuiApi.TextUnformatted($"Nodes       : {(rn.Nodes.IsCreated    ? rn.Nodes.Length.ToString()    : "not loaded")}");
        ImGuiApi.TextUnformatted($"Segments    : {(rn.Segments.IsCreated ? rn.Segments.Length.ToString() : "not loaded")}");
        ImGuiApi.TextUnformatted($"Grid size   : {rn.Width} x {rn.Height} cells");
        ImGuiApi.TextUnformatted($"Cell size   : {rn.CellSize:F1} m");
        return true;
    }
}

// ── SpatialGridData ───────────────────────────────────────────────────────────

/// <summary>
/// Read-only renderer for the <see cref="SpatialGridData"/> singleton.
/// The grid is rebuilt every frame by SpatialHashSystem; edits would be immediately overwritten.
/// </summary>
[ImGuiRenderer(typeof(SpatialGridData))]
public sealed class SpatialGridDataRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var d = (SpatialGridData)value;
        return $"{d.Grid.Width}x{d.Grid.Height} cells  |  {d.Grid.EntityCount} entities";
    }

    public bool RenderValue(object value)
    {
        var d = (SpatialGridData)value;
        var g = d.Grid;
        ImGuiApi.TextDisabled("Spatial Hash Grid (read-only, rebuilt every frame)");
        ImGuiApi.Separator();
        ImGuiApi.TextUnformatted($"Dimensions  : {g.Width} x {g.Height} cells");
        ImGuiApi.TextUnformatted($"Cell size   : {g.CellSize:F1} m");
        ImGuiApi.TextUnformatted($"Origin      : ({g.OriginX:F1}, {g.OriginY:F1})");
        ImGuiApi.TextUnformatted($"Entities    : {g.EntityCount}");
        ImGuiApi.TextUnformatted($"Free slots  : {g.FreeListCount}");
        return true;
    }
}

// ── ITkbDatabase ──────────────────────────────────────────────────────────────

/// <summary>
/// Read-only renderer for the <see cref="ITkbDatabase"/> managed singleton.
/// Lists all registered blueprint names and their TKB type IDs.
/// </summary>
[ImGuiRenderer(typeof(ITkbDatabase))]
public sealed class ITkbDatabaseRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var db = (ITkbDatabase)value;
        int count = db.GetAll().Count();
        return $"{count} blueprints registered";
    }

    public bool RenderValue(object value)
    {
        var db = (ITkbDatabase)value;
        var templates = db.GetAll().OrderBy(t => t.Name).ToList();
        ImGuiApi.TextDisabled($"TKB Blueprint Database  ({templates.Count} entries, read-only)");
        ImGuiApi.Separator();
        foreach (var tpl in templates)
        {
            ImGuiApi.TextUnformatted($"[{tpl.TkbType,6}]  {tpl.Name}");
        }
        return true;
    }
}

// ── INetworkTopology ──────────────────────────────────────────────────────────

/// <summary>
/// Read-only renderer for the <see cref="INetworkTopology"/> managed singleton.
/// Shows local node ID and all known peer nodes.
/// </summary>
[ImGuiRenderer(typeof(INetworkTopology))]
public sealed class INetworkTopologyRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var t = (INetworkTopology)value;
        return $"Local Node #{t.LocalNodeId}";
    }

    public bool RenderValue(object value)
    {
        var t = (INetworkTopology)value;
        ImGuiApi.TextDisabled("Network Topology (read-only)");
        ImGuiApi.Separator();
        ImGuiApi.TextUnformatted($"Local Node ID : {t.LocalNodeId}");
        ImGuiApi.TextUnformatted("Known Nodes   :");
        foreach (int nodeId in t.GetAllNodes())
        {
            bool isLocal = nodeId == t.LocalNodeId;
            if (isLocal)
                ImGuiApi.TextUnformatted($"  Node {nodeId}  (local)");
            else
                ImGuiApi.TextUnformatted($"  Node {nodeId}");
        }
        return true;
    }
}

// ── BlockIdManager ────────────────────────────────────────────────────────────

/// <summary>
/// Read-only renderer for the <see cref="BlockIdManager"/> managed singleton.
/// Shows the current ID pool size. The pool is managed by the network layer.
/// </summary>
[ImGuiRenderer(typeof(BlockIdManager))]
public sealed class BlockIdManagerRenderer : IImGuiRenderer
{
    public string? GetSummary(object value)
    {
        var m = (BlockIdManager)value;
        return $"{m.AvailableCount} IDs available";
    }

    public bool RenderValue(object value)
    {
        var m = (BlockIdManager)value;
        ImGuiApi.TextDisabled("Network ID Block Allocator (read-only)");
        ImGuiApi.Separator();
        ImGuiApi.TextUnformatted($"Available IDs : {m.AvailableCount}");
        return true;
    }
}

// ── ISerializationRegistry ────────────────────────────────────────────────────

/// <summary>
/// Read-only renderer for the <see cref="ISerializationRegistry"/> managed singleton.
/// There is no enumeration API on the interface, so only presence is confirmed.
/// </summary>
[ImGuiRenderer(typeof(ISerializationRegistry))]
public sealed class ISerializationRegistryRenderer : IImGuiRenderer
{
    public string? GetSummary(object value) => "Ghost-protocol registry";

    public bool RenderValue(object value)
    {
        ImGuiApi.TextDisabled("Serialization Registry (read-only)");
        ImGuiApi.Separator();
        ImGuiApi.TextUnformatted($"Type : {value.GetType().Name}");
        ImGuiApi.TextDisabled("(No enumeration API available on ISerializationRegistry)");
        return true;
    }
}
