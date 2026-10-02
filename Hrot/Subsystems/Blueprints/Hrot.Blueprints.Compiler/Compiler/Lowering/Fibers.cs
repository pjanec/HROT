using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

/// <summary>
/// ⭐⭐ S6a (<c>DESIGN_Unified_Behaviour_Run</c> §4a) — <b>one fiber per suspending graph.</b> The Tick graph keeps the shared
/// <c>Cursor</c>; every suspending Event graph of a BEHAVIOUR gets its own cursor plus a saved copy of its inputs, so it can
/// wait across frames and be resumed with the event it started on. ⭐ Both ride <see cref="IrAsset.GraphLocalSlots"/>
/// beside the graph's promoted locals, so <c>FieldLayout</c> lays them out, <c>StructureHashComputation</c> hashes them and
/// the run's clear (CE-449) zeroes them — no new mechanism.
/// </summary>
internal static class Fibers
{
    private static readonly IrTypeRef CursorType = new()
    {
        FullName = "Fdp.Toolkit.Blueprints.BlueprintLatentCursor", IsUnmanaged = true, SizeBytes = 16,
    };

    /// <summary>The cursor field a graph's latent ops read and write.</summary>
    public static string CursorOf(IrGraph? graph) => graph?.CursorField ?? "Cursor";

    /// <summary>Whether this graph runs as a fiber of its own (it has its own cursor and saved inputs).</summary>
    public static bool IsOwnFiber(IrGraph graph) => graph.CursorField is not null;

    /// <summary>The slot holding a fiber's saved copy of one of its graph inputs.</summary>
    public static string InputSlot(IrGraph graph, IrField input)
        => graph.CursorField + "_in_" + Sanitizer.SanitizeName(input.Name);

    /// <summary>Gives every suspending Event graph of a behaviour its own fiber. ⛔ Instances keep BP1658 (S6a scope).</summary>
    public static IrAsset Assign(IrAsset asset)
    {
        if (asset.Dispatch != Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Behavior) return asset;

        var used   = new HashSet<string>(StringComparer.Ordinal);
        var graphs = new List<IrGraph>(asset.Graphs.Count);
        var slots  = new List<IrField>();
        foreach (var graph in asset.Graphs)
        {
            if (graph.Kind != IrGraphKind.Event || !LocalStorage.CanSuspend(graph)) { graphs.Add(graph); continue; }

            var baseName = "__fib_" + Sanitizer.SanitizeName(graph.Name);
            var name = baseName;
            for (int n = 2; !used.Add(name); n++) name = baseName + n;

            var fiber = graph with { CursorField = name };
            slots.Add(new IrField { Name = name, Type = CursorType });
            foreach (var input in graph.Inputs)
                slots.Add(input with { Name = InputSlot(fiber, input) });
            graphs.Add(fiber);
        }
        return slots.Count == 0
            ? asset
            : asset with { Graphs = graphs, GraphLocalSlots = asset.GraphLocalSlots.Concat(slots).ToList() };
    }
}
