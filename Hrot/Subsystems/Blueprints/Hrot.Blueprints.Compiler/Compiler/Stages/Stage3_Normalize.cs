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
        var newGraphs = new List<Graph>(asset.Graphs.Count);
        foreach (var graph in asset.Graphs)
            newGraphs.Add(MaterializeDefaultPinLiteralsInGraph(graph, asset));
        asset.Graphs = newGraphs;
        return asset;
    }

    /// <summary>
    /// For each unconnected data-IN pin that carries a default value (from
    /// <c>Node.PinDefaults[pinName]</c> or <c>Pin.DefaultValue</c>), synthesize
    /// a <see cref="LiteralNode"/> whose <c>ValueJson</c> is a valid C# literal
    /// and wire it to that pin.  Pins with NO default are left unchanged so that
    /// Stage 5 still emits BP4001 for them.
    /// </summary>
    private static Graph MaterializeDefaultPinLiteralsInGraph(Graph graph, BlueprintAsset asset)
    {
        // Build a set of pin IDs that already have an incoming data link
        // (so we never synthesize a duplicate link for a connected pin).
        var connectedPinIds = new HashSet<Guid>(
            graph.Links.Select(l => l.ToPinId));

        var extraNodes = new List<Node>();
        var extraLinks = new List<Link>();

        foreach (var node in graph.Nodes)
        {
            foreach (var pin in node.Pins)
            {
                // Only materialize unconnected data-IN pins.
                if (pin.IsExec || pin.Direction != "In") continue;
                if (connectedPinIds.Contains(pin.Id)) continue;

                // Resolve default value: PinDefaults bag is the live editor path;
                // Pin.DefaultValue is an alternative storage.
                string? rawDefault = null;
                if (node.PinDefaults != null &&
                    node.PinDefaults.TryGetValue(pin.Name, out var bagValue) &&
                    !string.IsNullOrEmpty(bagValue))
                    rawDefault = bagValue;
                else if (!string.IsNullOrEmpty(pin.DefaultValue))
                    rawDefault = pin.DefaultValue;

                if (rawDefault is null) continue;   // no default → leave for BP4001

                // Format the raw default as a C# literal based on the pin's TypeId.
                var typeId = pin.TypeRef?.TypeId ?? "";
                var csharpLiteral = FormatDefaultLiteral(typeId, rawDefault);
                if (csharpLiteral is null) continue;  // unsupported type, skip silently

                // Synthesize a deterministic LiteralNode ID.
                var litNodeId  = SynthesizedGuid("default-literal", graph.Id, node.Id, pin.Id);
                var litPinId   = SynthesizedGuid("default-literal-pin", graph.Id, node.Id, pin.Id);

                var litNode = new LiteralNode
                {
                    Id        = litNodeId,
                    TypeId    = typeId,
                    ValueJson = csharpLiteral,
                    Pins = new List<Pin>
                    {
                        new Pin
                        {
                            Id        = litPinId,
                            Name      = "Value",
                            Direction = "Out",
                            IsExec    = false,
                            TypeRef   = pin.TypeRef ?? new BlueprintTypeRef(),
                        },
                    },
                };
                extraNodes.Add(litNode);
                extraLinks.Add(new Link
                {
                    FromNodeId = litNodeId,
                    FromPinId  = litPinId,
                    ToNodeId   = node.Id,
                    ToPinId    = pin.Id,
                });
            }
        }

        if (extraNodes.Count == 0) return graph;

        // Return a new graph with the synthesized nodes and links appended.
        return new Graph
        {
            Id             = graph.Id,
            Name           = graph.Name,
            Kind           = graph.Kind,
            Inputs         = graph.Inputs,
            Outputs        = graph.Outputs,
            EditorMetadata = graph.EditorMetadata,
            Nodes          = graph.Nodes.Concat(extraNodes).ToList(),
            Links          = graph.Links.Concat(extraLinks).ToList(),
        };
    }

    /// <summary>
    /// Converts a raw default-value string (as stored in <c>PinDefaults</c> / <c>Pin.DefaultValue</c>)
    /// to a valid C# literal for the given <paramref name="typeId"/>.
    /// Returns <c>null</c> if the type is unsupported or the value is empty.
    /// </summary>
    private static string? FormatDefaultLiteral(string typeId, string rawValue)
    {
        if (string.IsNullOrEmpty(rawValue)) return null;

        // Enum: TypeId starts with "global::" per AN2 convention.
        // ENUM-NAME: rawValue is normally a member name (e.g. "Crouching").
        //   → emit  global::{fqn}.{Name}   (one "global::", no cast)
        // Backward-compat: if rawValue is a pure integer string (old assets / fallback)
        //   → emit  (global::{fqn})N        (the original integer-cast form)
        // Either way the typeId is used directly; it already contains "global::" so the
        // FQN without the prefix is typeId["global::".Length..].  We never double-prefix.
        if (typeId.StartsWith("global::", StringComparison.Ordinal))
        {
            // Check whether the stored value is a pure integer (all-digit, optional leading '-').
            // Use Substring instead of slice syntax for netstandard2.0 compatibility.
            var isInteger = rawValue.Length > 0
                && (rawValue[0] == '-' ? rawValue.Length > 1 && rawValue.Substring(1).All(char.IsDigit)
                                       : rawValue.All(char.IsDigit));

            if (isInteger)
            {
                // Old-style: emit integer cast  (global::FQN)N
                return $"({typeId}){rawValue}";
            }
            else
            {
                // New-style: emit member-qualified name  global::FQN.MemberName
                // typeId is already "global::FQN" → we just append "." + name.
                return $"{typeId}.{rawValue}";
            }
        }

        switch (typeId)
        {
            // --- Signed / unsigned integer primitives ---
            case "System.Int32":
                return rawValue;                              // "42" is a valid int literal
            case "System.Int64":
                return $"{rawValue}L";
            case "System.UInt32":
                return $"{rawValue}u";
            case "System.UInt64":
                return $"{rawValue}ul";
            case "System.Int16":
                return $"(short){rawValue}";
            case "System.UInt16":
                return $"(ushort){rawValue}";
            case "System.Byte":
                return $"(byte){rawValue}";
            case "System.SByte":
                return $"(sbyte){rawValue}";

            // --- Floating-point ---
            case "System.Single":
                // Ensure the literal has an "f" suffix so C# infers float not double.
                if (rawValue.EndsWith("f", StringComparison.OrdinalIgnoreCase) ||
                    rawValue.EndsWith("F", StringComparison.OrdinalIgnoreCase))
                    return rawValue;
                return $"{rawValue}f";
            case "System.Double":
                if (rawValue.EndsWith("d", StringComparison.OrdinalIgnoreCase) ||
                    rawValue.EndsWith("D", StringComparison.OrdinalIgnoreCase))
                    return rawValue;
                return $"{rawValue}d";
            case "System.Decimal":
                return $"{rawValue}m";

            // --- Boolean ---
            case "System.Boolean":
                // Normalise to lowercase true/false.
                return rawValue.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";

            // --- Managed string ---
            case "System.String":
                // Escape backslashes and double-quotes, wrap in double-quotes.
                var escaped = rawValue.Replace("\\", "\\\\").Replace("\"", "\\\"");
                return $"\"{escaped}\"";

            // --- Fdp.Core fixed-length strings ---
            case "Fdp.Core.FixedString32":
            {
                var esc = rawValue.Replace("\\", "\\\\").Replace("\"", "\\\"");
                return $"new global::Fdp.Core.FixedString32(\"{esc}\")";
            }
            case "Fdp.Core.FixedString64":
            {
                var esc = rawValue.Replace("\\", "\\\\").Replace("\"", "\\\"");
                return $"new global::Fdp.Core.FixedString64(\"{esc}\")";
            }

            // --- Fallback: unknown / unresolved types ---
            // When CLR reflection fails in the netstandard2.0 MSBuild sandbox (Full Rebuild),
            // Stage0 assigns System.Object as a placeholder.  Pass the raw value through as-is
            // so the C# compiler infers the correct literal type.  Without this, inline pin
            // defaults (e.g. PinDefaults["b"] = "10") are silently skipped, causing CS7036
            // missing-argument errors at emit time.
            case "System.Object":
            case "":
                return rawValue;

            default:
                return null;   // unknown type — leave pin for BP4001
        }
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

            ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP3011,
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
            ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP3010,
                $"Orphan node '{orphan.Id}' in graph '{graph.Name}' was eliminated.",
                asset.AssetId, graph.Id, orphan.Id));

        // Return a NEW Graph with filtered Nodes/Links rather than mutating the
        // original Graph object (which may be referenced by the caller's asset variable).
        // Mutating in place would cause subsequent compiles of the same asset object to
        // see a pruned graph (e.g. hot-reload re-compiling an identical asset twice).
        var newGraph = new Graph
        {
            Id            = graph.Id,
            Name          = graph.Name,
            Kind          = graph.Kind,
            Inputs        = graph.Inputs,
            Outputs       = graph.Outputs,
            EditorMetadata = graph.EditorMetadata,
            Nodes = graph.Nodes.Where(n => !orphanIds.Contains(n.Id)).ToList(),
            Links = graph.Links
                .Where(l => !orphanIds.Contains(l.FromNodeId) && !orphanIds.Contains(l.ToNodeId))
                .ToList(),
        };
        return newGraph;
    }

    private static void CollectReachable(Graph graph, Guid startId, HashSet<Guid> visited)
    {
        if (!visited.Add(startId)) return;
        foreach (var link in graph.Links)
        {
            if (link.FromNodeId == startId)
                CollectReachable(graph, link.ToNodeId, visited);
            // Also follow links in reverse so that data-provider nodes (e.g. LiteralNode)
            // are not incorrectly eliminated as orphans when their only connection is an
            // outgoing data wire into a node that is exec-reachable.
            if (link.ToNodeId == startId)
                CollectReachable(graph, link.FromNodeId, visited);
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

