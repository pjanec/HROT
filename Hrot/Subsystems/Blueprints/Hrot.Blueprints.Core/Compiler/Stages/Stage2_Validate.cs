using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Core.Compiler.Stages;

// ---------------------------------------------------------------------------
// Validator interface
// ---------------------------------------------------------------------------

internal interface IValidator
{
    void Validate(BlueprintAsset asset, ValidationContext ctx);
}

// ---------------------------------------------------------------------------
// Stage 2 pipeline
// ---------------------------------------------------------------------------

internal static class Stage2_Validate
{
    private static readonly IReadOnlyList<IValidator> Validators = new IValidator[]
    {
        new V_AssetStructure(),
        new V_DispatchKindCompatibility(),
        new V_NodeStructure(),
        new V_LinkStructure(),
        new V_GraphStructure(),
        new V_VariablesAndState(),
        new V_AiPrimitiveIntent(),
        new V_LatentRules(),
        new V_ChannelCommandReferences(),
        new V_EventGraphReferences(),
        new V_WaitNodeReferences(),
        new V_PeerReferences(),
        new V_TypeReferences(),
        new V_DeterminismOrdering(),
    };

    public static void Run(BlueprintAsset asset, ValidationContext ctx)
    {
        ctx.AssetId = asset.AssetId;
        foreach (var v in Validators)
        {
            v.Validate(asset, ctx);
            if (ctx.Diagnostics.HasFatalErrors) return;
        }
    }
}

// ---------------------------------------------------------------------------
// V_AssetStructure
// ---------------------------------------------------------------------------

internal sealed class V_AssetStructure : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        if (asset.AssetId == Guid.Empty)
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP0010_EmptyAssetId,
                "Asset has empty/zero AssetId."));

        if (string.IsNullOrEmpty(asset.Name))
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP0011_EmptyName,
                "Asset has empty Name.", asset.AssetId));
    }
}

// ---------------------------------------------------------------------------
// V_DispatchKindCompatibility
// ---------------------------------------------------------------------------

internal sealed class V_DispatchKindCompatibility : IValidator
{
    private static readonly AiPrimitiveHosting[] ActionHostings =
        { AiPrimitiveHosting.BTreeAction, AiPrimitiveHosting.HsmAction };
    private static readonly AiPrimitiveHosting[] ConditionHostings =
        { AiPrimitiveHosting.BTreeCondition, AiPrimitiveHosting.HsmGuard };

    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        switch (asset.Dispatch)
        {
            case BlueprintDispatchKind.Library:
                if (asset.Primitive is not null)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1010,
                        "Library asset has 'primitive' block, valid only for AiPrimitive.",
                        asset.AssetId));
                if (asset.Variables.Count > 0)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1011,
                        "Library asset must not declare member variables.", asset.AssetId));
                if (asset.CustomEvents.Count > 0)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1012,
                        "Library asset must not declare custom events.", asset.AssetId));
                if (asset.Graphs.Any(g => g.Kind == GraphKind.Event))
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1013,
                        "Library asset must not contain Event graphs.", asset.AssetId));
                break;

            case BlueprintDispatchKind.AiPrimitive:
                if (asset.Primitive is null)
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1020,
                        "AiPrimitive asset must have a 'primitive' block.", asset.AssetId));
                    return;
                }
                if (asset.Primitive.Hostings.Count == 0)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1021,
                        "AiPrimitive must declare at least one hosting.", asset.AssetId));

                foreach (var hosting in asset.Primitive.Hostings)
                {
                    if (asset.Primitive.Intent == AiPrimitiveIntent.Action
                        && ConditionHostings.Contains(hosting))
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1022,
                            $"Action intent incompatible with condition-shaped hosting '{hosting}'.",
                            asset.AssetId));
                    if (asset.Primitive.Intent == AiPrimitiveIntent.Condition
                        && ActionHostings.Contains(hosting))
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1023,
                            $"Condition intent incompatible with action-shaped hosting '{hosting}'.",
                            asset.AssetId));
                }
                if (asset.Variables.Count > 0)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1024,
                        "AiPrimitive uses 'parameters' and 'workingState', not 'variables'.",
                        asset.AssetId));
                if (asset.Graphs.Any(g => g.Kind == GraphKind.Event))
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1025,
                        "AiPrimitive does not subscribe to engine events.", asset.AssetId));
                break;

            case BlueprintDispatchKind.Instance:
                if (asset.Primitive is not null)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1030,
                        "Instance asset must not have a 'primitive' block.", asset.AssetId));
                if (asset.Parameters.Count > 0 || asset.WorkingState.Count > 0)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1031,
                        "Instance uses 'variables', not 'parameters'/'workingState'.",
                        asset.AssetId));
                break;
        }
    }
}

// ---------------------------------------------------------------------------
// V_NodeStructure
// ---------------------------------------------------------------------------

internal sealed class V_NodeStructure : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var graph in asset.Graphs)
        {
            foreach (var node in graph.Nodes)
            {
                var seen = new HashSet<Guid>();
                foreach (var pin in node.Pins)
                {
                    if (!seen.Add(pin.Id))
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1601,
                            $"Node '{node.Id}' in graph '{graph.Name}' has duplicate pin Id {pin.Id}.",
                            asset.AssetId, graph.Id, node.Id));
                }
            }
        }
    }
}

// ---------------------------------------------------------------------------
// V_LinkStructure
// ---------------------------------------------------------------------------

internal sealed class V_LinkStructure : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var graph in asset.Graphs)
        {
            var nodeById = graph.Nodes.ToDictionary(n => n.Id);
            var seenLinks = new HashSet<(Guid, Guid, Guid, Guid)>();

            foreach (var link in graph.Links)
            {
                // Check duplicate links
                var key = (link.FromNodeId, link.FromPinId, link.ToNodeId, link.ToPinId);
                if (!seenLinks.Add(key))
                    ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP1601,
                        $"Duplicate link in graph '{graph.Name}'.",
                        asset.AssetId, graph.Id));

                // Check From references
                if (!nodeById.TryGetValue(link.FromNodeId, out var fromNode))
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1602,
                        $"Link references unknown FromNodeId {link.FromNodeId}.",
                        asset.AssetId, graph.Id));
                    continue;
                }
                // Skip pin-level checks when node has no pins: pins are resolved from the
                // node registry in later stages and are not stored in the JSON asset.
                if (fromNode.Pins.Count > 0 && !fromNode.Pins.Any(p => p.Id == link.FromPinId))
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1602,
                        $"Link references unknown FromPinId {link.FromPinId} on node {link.FromNodeId}.",
                        asset.AssetId, graph.Id));

                // Check To references
                if (!nodeById.TryGetValue(link.ToNodeId, out var toNode))
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1602,
                        $"Link references unknown ToNodeId {link.ToNodeId}.",
                        asset.AssetId, graph.Id));
                    continue;
                }
                if (toNode.Pins.Count > 0 && !toNode.Pins.Any(p => p.Id == link.ToPinId))
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1602,
                        $"Link references unknown ToPinId {link.ToPinId} on node {link.ToNodeId}.",
                        asset.AssetId, graph.Id));
            }
        }
    }
}

// ---------------------------------------------------------------------------
// V_GraphStructure
// ---------------------------------------------------------------------------

internal sealed class V_GraphStructure : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var graph in asset.Graphs)
        {
            // Skip structural checks when nodes have no pin data.
            // In the JSON asset format, pins are not stored in the node JSON --
            // they are resolved from the node registry in later stages (Stage3/4).
            // When all nodes have empty Pins, pin-based entry detection is not reliable.
            bool anyNodeHasPins = graph.Nodes.Any(n => n.Pins.Count > 0);
            if (!anyNodeHasPins) continue;

            var entryNode = FindEntryNode(graph);
            if (entryNode is null)
            {
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1602,
                    $"Graph '{graph.Name}' has no entry node (no node with exec-out and no incoming exec wire).",
                    asset.AssetId, graph.Id));
                continue;
            }

            // Exec-reachability check
            var reachable = new HashSet<Guid>();
            CollectExecReachable(graph, entryNode.Id, reachable);

            bool hasReturn = graph.Nodes
                .Where(n => reachable.Contains(n.Id))
                .OfType<ReturnNode>()
                .Any();

            if (!hasReturn && graph.Nodes.Count > 0)
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1601,
                    $"Graph '{graph.Name}': no ReturnNode is exec-reachable from entry.",
                    asset.AssetId, graph.Id));
        }
    }

    internal static Node? FindEntryNode(Graph graph)
    {
        // For Event graphs: EventEntryNode is always the entry.
        if (graph.Kind == GraphKind.Event)
            return graph.Nodes.OfType<EventEntryNode>().FirstOrDefault();

        // For Function/Construction graphs: EventEntryNode is an explicit entry indicator.
        // Real assets loaded from JSON have no pin data yet (pins are resolved from registry
        // in later stages), so pin-based detection must be skipped for those assets.
        var eventEntry = graph.Nodes.OfType<EventEntryNode>().FirstOrDefault();
        if (eventEntry is not null) return eventEntry;

        // Fallback: pin-based detection for builder-constructed assets where Pins are pre-populated.
        // Build set of node IDs that are the TARGET of any exec link.
        var nodesWithIncomingExec = new HashSet<Guid>();
        var pinOwner = graph.Nodes.ToDictionary(
            n => n.Id,
            n => n.Pins.ToDictionary(p => p.Id));

        foreach (var link in graph.Links)
        {
            if (!pinOwner.TryGetValue(link.ToNodeId, out var pins)) continue;
            if (!pins.TryGetValue(link.ToPinId, out var pin)) continue;
            if (pin.IsExec)
                nodesWithIncomingExec.Add(link.ToNodeId);
        }

        // Entry node: has exec-out pin, not targeted by any exec-in link.
        foreach (var node in graph.Nodes)
        {
            if (nodesWithIncomingExec.Contains(node.Id)) continue;
            if (node.Pins.Any(p => p.IsExec && p.Direction == "Out"))
                return node;
        }
        return null;
    }

    private static void CollectExecReachable(Graph graph, Guid startId, HashSet<Guid> visited)
    {
        if (!visited.Add(startId)) return;
        var node = graph.Nodes.FirstOrDefault(n => n.Id == startId);
        if (node is null) return;

        foreach (var link in graph.Links)
        {
            if (link.FromNodeId != startId) continue;
            var fromPin = node.Pins.FirstOrDefault(p => p.Id == link.FromPinId);
            if (fromPin is { IsExec: true })
                CollectExecReachable(graph, link.ToNodeId, visited);
        }
    }
}

// ---------------------------------------------------------------------------
// V_VariablesAndState
// ---------------------------------------------------------------------------

internal sealed class V_VariablesAndState : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        switch (asset.Dispatch)
        {
            case BlueprintDispatchKind.AiPrimitive:
                if (asset.Primitive is null) return;

                int paramsSize = ComputeStructSize(asset.Parameters.Select(p => p.Type), ctx);
                if (paramsSize > 100)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1200,
                        $"AiPrimitive Parameters total {paramsSize} bytes; max is 100.",
                        asset.AssetId));

                int workingSize = ComputeStructSize(asset.WorkingState.Select(v => v.Type), ctx);
                if (workingSize > 1024 - 8)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1201,
                        $"AiPrimitive WorkingState total {workingSize} bytes; max is {1024 - 8}.",
                        asset.AssetId));
                break;

            case BlueprintDispatchKind.Instance:
                int stateSize = ComputeStructSize(asset.Variables.Select(v => v.Type), ctx);
                int tierBudget = (asset.TierHint, stateSize) switch
                {
                    (BlackboardTierHint.Force1024,  _)               => 928,
                    (BlackboardTierHint.Force4096,  _)               => 3936,
                    (BlackboardTierHint.Force16384, _)               => 16096,
                    (BlackboardTierHint.Auto, _) when stateSize <= 928  => 928,
                    (BlackboardTierHint.Auto, _) when stateSize <= 3936 => 3936,
                    (BlackboardTierHint.Auto, _) when stateSize <= 16096 => 16096,
                    _ => 0
                };
                if (tierBudget == 0)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1210,
                        $"Instance state {stateSize} bytes exceeds largest tier (16384). "
                        + "Reduce variable count or split asset.",
                        asset.AssetId));
                else if (asset.TierHint != BlackboardTierHint.Auto && stateSize > tierBudget)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1211,
                        $"Instance state {stateSize} bytes exceeds requested tier "
                        + $"{asset.TierHint} budget {tierBudget} bytes.",
                        asset.AssetId));
                break;
        }
    }

    private static int ComputeStructSize(IEnumerable<BlueprintTypeRef> types, ValidationContext ctx)
    {
        int offset = 0;
        foreach (var typeRef in types)
        {
            if (!ctx.TypeRegistry.TryResolve(typeRef, out var resolved))
                continue;
            int align = resolved.SizeBytes switch { 1 => 1, 2 => 2, <= 4 => 4, _ => 8 };
            int sz = resolved.SizeBytes;
            offset = AlignUp(offset, align);
            offset += sz;
        }
        return AlignUp(offset, 8);
    }

    private static int AlignUp(int offset, int align) => (offset + align - 1) & ~(align - 1);
}

// ---------------------------------------------------------------------------
// V_AiPrimitiveIntent
// ---------------------------------------------------------------------------

internal sealed class V_AiPrimitiveIntent : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        if (asset.Dispatch != BlueprintDispatchKind.AiPrimitive) return;
        if (asset.Primitive?.Intent != AiPrimitiveIntent.Condition) return;

        foreach (var graph in asset.Graphs)
        {
            foreach (var node in graph.Nodes)
            {
                switch (node)
                {
                    case ReturnNode rn when rn.Status == NodeStatus.Running:
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1100,
                            "Condition intent: Return Running is forbidden. "
                            + "Conditions must be instantaneous.",
                            asset.AssetId, graph.Id, node.Id));
                        break;

                    case LatentDelayNode:
                    case WaitForChannelNode:
                    case WaitForEventNode:
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1101,
                            "Condition intent: latent nodes are forbidden. "
                            + "Condition graphs must be synchronous.",
                            asset.AssetId, graph.Id, node.Id));
                        break;
                }
            }
        }
    }
}

// ---------------------------------------------------------------------------
// V_LatentRules
// ---------------------------------------------------------------------------

internal sealed class V_LatentRules : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        if (asset.Dispatch == BlueprintDispatchKind.Library)
        {
            foreach (var graph in asset.Graphs)
            {
                foreach (var node in graph.Nodes)
                {
                    if (node is LatentDelayNode or WaitForChannelNode or WaitForEventNode)
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1101,
                            $"Library assets must not contain latent nodes (node {node.Id} in '{graph.Name}').",
                            asset.AssetId, graph.Id, node.Id));
                }
            }
        }
    }
}

// ---------------------------------------------------------------------------
// V_ChannelCommandReferences
// ---------------------------------------------------------------------------

internal sealed class V_ChannelCommandReferences : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        var entries = ctx.ChannelCommands.GetEntries();
        // Empty catalog means no catalog is configured -- skip validation (opt-in).
        if (entries.Count == 0) return;

        foreach (var graph in asset.Graphs)
        {
            foreach (var node in graph.Nodes.OfType<ChannelCommandNode>())
            {
                bool found = entries.Any(e =>
                    e.ChannelType.Name == node.ChannelType
                    && e.Name == node.ActionId);
                if (!found)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1401,
                        $"ChannelCommandNode references unknown command '{node.ChannelType}.{node.ActionId}'.",
                        asset.AssetId, graph.Id, node.Id));
            }
        }
    }
}

// ---------------------------------------------------------------------------
// V_EventGraphReferences
// ---------------------------------------------------------------------------

internal sealed class V_EventGraphReferences : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        var catalogEntries = ctx.EngineEvents.GetEntries();
        var customEventIds = new HashSet<Guid>(
            asset.CustomEvents.Select(e => e.Id));

        foreach (var graph in asset.Graphs)
        {
            foreach (var node in graph.Nodes.OfType<EventEntryNode>())
            {
                if (string.IsNullOrEmpty(node.EventTypeId)) continue;

                // Check against custom events (by parsed Guid match)
                if (Guid.TryParse(node.EventTypeId, out var eventGuid)
                    && customEventIds.Contains(eventGuid))
                    continue;

                // Empty catalog means no catalog is configured -- skip validation (opt-in).
                if (catalogEntries.Count == 0) continue;

                // Check against engine event catalog (by type FQN or short name)
                if (catalogEntries.Any(e =>
                        e.EventType.FullName == node.EventTypeId
                        || e.EventType.Name == node.EventTypeId))
                    continue;

                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1400,
                    $"EventEntryNode references unknown event type '{node.EventTypeId}'.",
                    asset.AssetId, graph.Id, node.Id));
            }
        }
    }
}

// ---------------------------------------------------------------------------
// V_WaitNodeReferences
// ---------------------------------------------------------------------------

internal sealed class V_WaitNodeReferences : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        var entries = ctx.WaitPrimitives.GetEntries();
        // Empty catalog means no catalog is configured -- skip validation (opt-in).
        if (entries.Count == 0) return;

        foreach (var graph in asset.Graphs)
        {
            foreach (var node in graph.Nodes)
            {
                string? channelType = node switch
                {
                    WaitForChannelNode wfc => wfc.ChannelType,
                    WaitForEventNode wfe   => wfe.EventTypeId,
                    _                       => null
                };
                if (channelType is null) continue;

                bool found = entries.Any(e => e.TargetType.Name == channelType);
                if (!found)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1402,
                        $"Wait node references unknown wait target '{channelType}'.",
                        asset.AssetId, graph.Id, node.Id));
            }
        }
    }
}

// ---------------------------------------------------------------------------
// V_PeerReferences (Patch 1: uses SiblingSignaturesById, not SiblingsById)
// ---------------------------------------------------------------------------

internal sealed class V_PeerReferences : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var graph in asset.Graphs)
        {
            foreach (var node in graph.Nodes.OfType<CallPeerBlueprintNode>())
            {
                // PeerBlueprintId is a string -- parse as Guid.
                if (!Guid.TryParse(node.PeerBlueprintId, out var targetId))
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1300,
                        $"CallPeerBlueprintNode has invalid PeerBlueprintId '{node.PeerBlueprintId}'.",
                        asset.AssetId, graph.Id, node.Id));
                    continue;
                }

                if (!asset.CallablePeers.Contains(targetId))
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1300,
                        $"CallPeerBlueprintNode targets asset {targetId}, "
                        + "which is not in CallablePeers list.",
                        asset.AssetId, graph.Id, node.Id));
                    continue;
                }

                if (!ctx.SiblingSignaturesById.TryGetValue(targetId, out var peer))
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1301,
                        $"CallablePeer {targetId} not found among compiled assets. "
                        + "Add as <AdditionalFiles> or remove from CallablePeers.",
                        asset.AssetId, graph.Id, node.Id));
                    continue;
                }

                if (!peer.ExportedFunctionNames.Contains(node.FunctionRef))
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1302,
                        $"CallablePeer '{peer.Name}' has no function graph named '{node.FunctionRef}'.",
                        asset.AssetId, graph.Id, node.Id));
            }
        }
    }
}

// ---------------------------------------------------------------------------
// V_TypeReferences
// ---------------------------------------------------------------------------

internal sealed class V_TypeReferences : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var graph in asset.Graphs)
        {
            foreach (var node in graph.Nodes)
            {
                foreach (var pin in node.Pins.Where(p => !p.IsExec))
                {
                    if (string.IsNullOrEmpty(pin.TypeRef.TypeId)) continue;
                    if (!ctx.TypeRegistry.TryResolve(pin.TypeRef, out _))
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1500,
                            $"Pin type '{pin.TypeRef.TypeId}' does not resolve.",
                            asset.AssetId, graph.Id, node.Id, pin.Id));
                }
            }
        }
    }
}

// ---------------------------------------------------------------------------
// V_DeterminismOrdering  (no-op for Slice 1)
// ---------------------------------------------------------------------------

internal sealed class V_DeterminismOrdering : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        // Slice 2 implementation.
    }
}

