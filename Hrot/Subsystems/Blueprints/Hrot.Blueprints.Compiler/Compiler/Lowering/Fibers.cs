using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

/// <summary>
/// ⭐⭐ S6a/S6b (<c>DESIGN_Unified_Behaviour_Run</c> §4a) — <b>one fiber per suspending graph.</b> The Tick graph keeps the
/// shared <c>Cursor</c>; every suspending Event graph of a BEHAVIOUR runs as its own fiber: a generated record
/// <c>_Fiber_{G}</c> (its cursor, its promoted locals, a saved copy of its inputs), held in <c>Exec</c> as
/// <see cref="Copies"/> fields <c>{base}_{k}</c> — one per handler allowed to run at once (U-6 Parallel(N)). ⭐ The
/// copies ride <see cref="IrAsset.GraphLocalSlots"/>, so <c>FieldLayout</c> lays them out, <c>StructureHashComputation</c>
/// hashes them and the run's clear (CE-449) zeroes them — no new mechanism.
/// </summary>
internal static class Fibers
{
    /// <summary>The most copies one Event graph may run at once (BP1660).</summary>
    public const int MaxCapacity = 16;

    private static readonly IrTypeRef IntType = new() { FullName = "System.Int32", IsUnmanaged = true, SizeBytes = 4 };

    private static readonly IrTypeRef CursorType = new()
    {
        FullName = "Fdp.Toolkit.Blueprints.BlueprintLatentCursor", IsUnmanaged = true, SizeBytes = 16,
    };

    /// <summary>Whether this graph runs as a fiber of its own.</summary>
    public static bool IsOwnFiber(IrGraph? graph) => graph?.FiberBase is not null;

    /// <summary>The container a graph's cursor and promoted locals live in: its fiber record, or the shared execution state.</summary>
    public static string Container(EmissionContext ctx) => IsOwnFiber(ctx.CurrentGraph) ? "__f" : ctx.ExecVar;

    /// <summary>The cursor a graph's latent ops read and write.</summary>
    public static string CursorPath(EmissionContext ctx) => Container(ctx) + ".Cursor";

    /// <summary>A record field holding a saved input.</summary>
    public static string InputField(IrField input) => "In_" + Sanitizer.SanitizeName(input.Name);

    /// <summary>The generated record type of a fiber graph.</summary>
    public static string RecordType(IrGraph graph) => "_Fiber" + graph.FiberBase;

    /// <summary>The <c>Exec</c> field holding copy <paramref name="k"/> of a fiber graph.</summary>
    public static string CopyField(IrGraph graph, int k) => graph.FiberBase + "_" + k;

    /// <summary>⭐ S6b-2 — how many arrivals a Queue(N) graph holds while its one copy runs (0 for other policies).</summary>
    public static int QueueCapacity(IrGraph graph)
        => graph.FiberPolicy == EventFiberPolicy.Queue ? Math.Max(1, graph.FiberCapacity) : 0;

    /// <summary>The generated record type of one queued arrival (the graph's inputs).</summary>
    public static string QueueEntryType(IrGraph graph) => "_FiberIn" + graph.FiberBase;

    /// <summary>The <c>Exec</c> field holding queue entry <paramref name="k"/>.</summary>
    public static string QueueEntry(IrGraph graph, int k) => graph.FiberBase + "_q" + k;

    /// <summary>The queue's head index and count fields (a fixed circular buffer).</summary>
    public static string QueueHead(IrGraph graph) => graph.FiberBase + "_qHead";
    public static string QueueCount(IrGraph graph) => graph.FiberBase + "_qCount";

    /// <summary>The generated accessor returning a ref to queue entry <c>i</c>.</summary>
    public static string QueueAt(IrGraph graph) => "QueueAt" + graph.FiberBase;

    /// <summary>How many copies of the graph exist: Parallel(N) has N; Restart and Queue run one.</summary>
    public static int Copies(IrGraph graph)
        => graph.FiberPolicy == EventFiberPolicy.Parallel ? Math.Max(1, graph.FiberCapacity) : 1;

    /// <summary>
    /// Gives every suspending Event graph of a behaviour its own fiber: moves its promoted locals out of the shared
    /// execution state into its record, adds the cursor and the saved inputs, and lays <see cref="Copies"/> records out
    /// in <c>Exec</c>. ⛔ Instances keep BP1658 (S6a scope).
    /// </summary>
    public static IrAsset Assign(IrAsset asset)
    {
        if (asset.Dispatch != BlueprintDispatchKind.Behavior) return asset;

        var used    = new HashSet<string>(StringComparer.Ordinal);
        var graphs  = new List<IrGraph>(asset.Graphs.Count);
        var shared  = asset.GraphLocalSlots.ToList();
        var copies  = new List<IrField>();
        foreach (var graph in asset.Graphs)
        {
            if (graph.Kind != IrGraphKind.Event || !LocalStorage.CanSuspend(graph)) { graphs.Add(graph); continue; }

            var baseName = "__fib_" + Sanitizer.SanitizeName(graph.Name);
            var name = baseName;
            for (int n = 2; !used.Add(name); n++) name = baseName + n;

            // the graph's promoted locals move into its record (they were laid out as shared slots by LocalStorage)
            var record = new List<IrField> { new() { Name = "Cursor", Type = CursorType } };
            if (graph.LocalSlotPrefix is { } prefix)
            {
                var mine = shared.Where(f => f.Name.StartsWith(prefix, StringComparison.Ordinal)).ToList();
                foreach (var f in mine) shared.Remove(f);
                record.AddRange(mine);
            }
            foreach (var input in graph.Inputs)
                record.Add(input with { Name = InputField(input) });

            var fiber = graph with { FiberBase = name, FiberRecordFields = record };
            var recordType = new IrTypeRef
            {
                FullName     = RecordType(fiber),
                IsUnmanaged  = true,
                SizeBytes    = FieldLayout.RecordSize(record),
                SizeReliable = record.All(f => f.Type.SizeReliable),
            };
            for (int k = 0; k < Copies(fiber); k++)
                copies.Add(new IrField { Name = CopyField(fiber, k), Type = recordType });

            // ⭐ S6b-2 — Queue(N): a fixed circular buffer of the graph's INPUTS in Exec (recorded with it), plus head/count.
            //   An input-less graph needs only the count.
            int q = QueueCapacity(fiber);
            if (q > 0)
            {
                var entryFields = graph.Inputs.Select(i => i with { Name = InputField(i) }).ToList();
                if (entryFields.Count > 0)
                {
                    var entryType = new IrTypeRef
                    {
                        FullName     = QueueEntryType(fiber),
                        IsUnmanaged  = true,
                        SizeBytes    = FieldLayout.RecordSize(entryFields),
                        SizeReliable = entryFields.All(f => f.Type.SizeReliable),
                    };
                    for (int k = 0; k < q; k++)
                        copies.Add(new IrField { Name = QueueEntry(fiber, k), Type = entryType });
                }
                copies.Add(new IrField { Name = QueueHead(fiber),  Type = IntType });
                copies.Add(new IrField { Name = QueueCount(fiber), Type = IntType });
            }
            graphs.Add(fiber);
        }
        return copies.Count == 0
            ? asset
            : asset with { Graphs = graphs, GraphLocalSlots = shared.Concat(copies).ToList() };
    }
}
