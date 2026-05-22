using System.Security.Cryptography;
using System.Text;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Core.Compiler.Stages;

internal static class Stage3_Normalize
{
    public static BlueprintAsset Run(BlueprintAsset asset, ValidationContext ctx)
    {
        asset = MaterializeDefaultPinLiterals(asset, ctx);
        asset = InsertImplicitCasts(asset, ctx);
        asset = EliminateOrphanNodes(asset, ctx);
        return asset;
    }

    // -----------------------------------------------------------------------
    // Pass 1: Materialize default pin literals
    // -----------------------------------------------------------------------

    private static BlueprintAsset MaterializeDefaultPinLiterals(
        BlueprintAsset asset, ValidationContext ctx)
    {
        // Pin model has no DefaultLiteralJson in Slice 1; defaults come from
        // graph Inputs (ParameterDecl.DefaultValueJson). No-op for now.
        return asset;
    }

    // -----------------------------------------------------------------------
    // Pass 2: Insert implicit casts on links with coercible type mismatches
    // -----------------------------------------------------------------------

    private static BlueprintAsset InsertImplicitCasts(
        BlueprintAsset asset, ValidationContext ctx)
    {
        var newGraphs = new List<Graph>(asset.Graphs.Count);
        foreach (var graph in asset.Graphs)
        {
            var newGraph = InsertImplicitCastsInGraph(graph, asset, ctx);
            newGraphs.Add(newGraph);
        }
        asset.Graphs = newGraphs;
        return asset;
    }

    private static Graph InsertImplicitCastsInGraph(
        Graph graph, BlueprintAsset asset, ValidationContext ctx)
    {
        var extraNodes = new List<Node>();
        var extraLinks = new List<Link>();
        var removedLinks = new HashSet<(Guid, Guid, Guid, Guid)>();

        var pinOwner = graph.Nodes.ToDictionary(
            n => n.Id,
            n => n.Pins.ToDictionary(p => p.Id));

        foreach (var link in graph.Links)
        {
            // Skip exec links
            if (!pinOwner.TryGetValue(link.FromNodeId, out var fromPins)) continue;
            if (!fromPins.TryGetValue(link.FromPinId, out var fromPin)) continue;
            if (fromPin.IsExec) continue;

            if (!pinOwner.TryGetValue(link.ToNodeId, out var toPins)) continue;
            if (!toPins.TryGetValue(link.ToPinId, out var toPin)) continue;

            if (!ctx.TypeRegistry.TryResolve(fromPin.TypeRef, out var fromIr)) continue;
            if (!ctx.TypeRegistry.TryResolve(toPin.TypeRef, out var toIr)) continue;

            if (fromIr.FullName == toIr.FullName) continue;

            if (!ctx.TypeRegistry.TryGetCoercion(fromIr, toIr, out var coercionExpr)) continue;

            // Insert a CastNode between fromPin and toPin.
            var castNodeId = SynthesizedGuid("implicit-cast", graph.Id, link.FromPinId, link.ToPinId);
            var castNode = new CastNode
            {
                Id = castNodeId,
                TargetTypeId = toIr.FullName,
            };

            var castInPinId  = SynthesizedGuid("cast-in",  graph.Id, castNodeId);
            var castOutPinId = SynthesizedGuid("cast-out", graph.Id, castNodeId);

            castNode.Pins.Add(new Pin
            {
                Id = castInPinId,
                Name = "In",
                Direction = "In",
                TypeRef = fromPin.TypeRef,
                IsExec = false,
            });
            castNode.Pins.Add(new Pin
            {
                Id = castOutPinId,
                Name = "Out",
                Direction = "Out",
                TypeRef = toPin.TypeRef,
                IsExec = false,
            });

            extraNodes.Add(castNode);

            // Replace original link with two new links (source → cast-in, cast-out → dest).
            removedLinks.Add((link.FromNodeId, link.FromPinId, link.ToNodeId, link.ToPinId));
            extraLinks.Add(new Link
            {
                FromNodeId = link.FromNodeId, FromPinId = link.FromPinId,
                ToNodeId   = castNodeId,      ToPinId   = castInPinId,
            });
            extraLinks.Add(new Link
            {
                FromNodeId = castNodeId,   FromPinId = castOutPinId,
                ToNodeId   = link.ToNodeId, ToPinId  = link.ToPinId,
            });

            ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP2002,
                $"Implicit cast inserted from '{fromIr.FullName}' to '{toIr.FullName}'.",
                asset.AssetId, graph.Id));
        }

        if (extraNodes.Count == 0) return graph;

        var filteredLinks = graph.Links
            .Where(l => !removedLinks.Contains(
                (l.FromNodeId, l.FromPinId, l.ToNodeId, l.ToPinId)))
            .ToList();
        filteredLinks.AddRange(extraLinks);

        graph.Nodes.AddRange(extraNodes);
        graph.Links = filteredLinks;
        return graph;
    }

    // -----------------------------------------------------------------------
    // Pass 3: Eliminate orphan nodes
    // -----------------------------------------------------------------------

    private static BlueprintAsset EliminateOrphanNodes(
        BlueprintAsset asset, ValidationContext ctx)
    {
        var newGraphs = new List<Graph>(asset.Graphs.Count);
        foreach (var graph in asset.Graphs)
        {
            var newGraph = EliminateOrphanNodesInGraph(graph, asset, ctx);
            newGraphs.Add(newGraph);
        }
        asset.Graphs = newGraphs;
        return asset;
    }

    private static Graph EliminateOrphanNodesInGraph(
        Graph graph, BlueprintAsset asset, ValidationContext ctx)
    {
        var entryNode = V_GraphStructure.FindEntryNode(graph);
        if (entryNode is null) return graph;

        // Collect all nodes reachable from entry via exec OR data wires.
        var reachable = new HashSet<Guid>();
        CollectReachable(graph, entryNode.Id, reachable);

        var orphans = graph.Nodes
            .Where(n => !reachable.Contains(n.Id))
            .ToList();

        if (orphans.Count == 0) return graph;

        var orphanIds = new HashSet<Guid>(orphans.Select(n => n.Id));

        foreach (var orphan in orphans)
            ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP2001,
                $"Orphan node '{orphan.Id}' in graph '{graph.Name}' was eliminated.",
                asset.AssetId, graph.Id, orphan.Id));

        graph.Nodes = graph.Nodes.Where(n => !orphanIds.Contains(n.Id)).ToList();
        graph.Links = graph.Links
            .Where(l => !orphanIds.Contains(l.FromNodeId) && !orphanIds.Contains(l.ToNodeId))
            .ToList();
        return graph;
    }

    private static void CollectReachable(Graph graph, Guid startId, HashSet<Guid> visited)
    {
        if (!visited.Add(startId)) return;
        foreach (var link in graph.Links)
        {
            if (link.FromNodeId == startId)
                CollectReachable(graph, link.ToNodeId, visited);
        }
    }

    // -----------------------------------------------------------------------
    // Deterministic GUID synthesis (§6.4)
    // -----------------------------------------------------------------------

    internal static Guid SynthesizedGuid(string purpose, params object[] inputs)
    {
        using var sha = SHA256.Create();
        var sb = new StringBuilder(purpose);
        foreach (var x in inputs)
            sb.Append('|').Append(x);
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        byte[] guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        return new Guid(guidBytes);
    }
}

