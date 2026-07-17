using System.Text.Json.Nodes;
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
        new V_WhenNodeRules(),
        new V_FlowForEachRules(),
        new V_ReadEqsResultNodeRules(),
        new V_SpawnEqsSensorNodeRules(),
        new V_SharedStateRules(),
        new V_FunctionGraphCallRules(),
        new V_ExecOutFanOut(),
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

            // Skip BP1601 check for multi-node linkless graphs.
            // These are documentation / recipe blueprints that were saved with nodes but no
            // wiring (projection-only JSON).  After Stage0 rehydrates pins the node count is
            // > 1 but Links is still empty, so exec-reachability would always fail.
            // Single-node linkless graphs (e.g. a lone EventEntryNode produced by a unit-test
            // builder) ARE checked because there is no WIP intent — the blueprint is structurally
            // complete but simply missing a return path.
            if (graph.Links.Count == 0 && graph.Nodes.Count > 1) continue;

            // Exec-reachability check
            var reachable = new HashSet<Guid>();
            CollectExecReachable(graph, entryNode.Id, reachable);

            bool hasReturn = graph.Nodes
                .Where(n => reachable.Contains(n.Id))
                .OfType<ReturnNode>()
                .Any();

            // BP1601 relaxed: implicit return is now synthesized at end-of-chain
            // by Stage5_Schedule.SealFallThrough, so an explicit ReturnNode is
            // optional (still supported for early-exit and non-default status/value).
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
                // AN8: ActionFqn-set nodes are non-channel behavior actions; skip catalog check.
                if (!string.IsNullOrEmpty(node.ActionFqn)) continue;

                bool found = entries.Any(e =>
                    Stage2Helpers.LastSegment(e.ChannelTypeFqn) == node.ChannelType
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
                        e.EventTypeFqn == node.EventTypeId
                        || Stage2Helpers.LastSegment(e.EventTypeFqn) == node.EventTypeId))
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

                bool found = entries.Any(e => Stage2Helpers.LastSegment(e.TargetTypeFqn) == channelType);
                if (!found)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1402,
                        $"Wait node references unknown wait target '{channelType}'.",
                        asset.AssetId, graph.Id, node.Id));
            }
        }
    }
}

internal static partial class Stage2Helpers
{
    internal static string LastSegment(string fqn)
    {
        int idx = fqn.LastIndexOf('.');
        return idx < 0 ? fqn : fqn.Substring(idx + 1);
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

// ---------------------------------------------------------------------------
// V_WhenNodeRules (BP2001-BP2015)
// ---------------------------------------------------------------------------

internal sealed class V_WhenNodeRules : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        var eventEntries = ctx.EngineEvents.GetEntries();

        foreach (var graph in asset.Graphs)
        {
            // A Function graph in an Instance blueprint is "pure" if it contains no
            // EventEntryNode (i.e., it is a user-defined pure helper function).
            // WhenNode is forbidden in pure helper functions.
            bool graphIsPureFunction = asset.Dispatch == BlueprintDispatchKind.Instance
                && graph.Kind == GraphKind.Function
                && !graph.Nodes.OfType<EventEntryNode>().Any();

            foreach (var node in graph.Nodes.OfType<WhenNode>())
            {
                // BP2001 -- unsupported dispatch (Library, AiPrimitive, or Instance pure-function)
                if (asset.Dispatch == BlueprintDispatchKind.Library
                    || asset.Dispatch == BlueprintDispatchKind.AiPrimitive
                    || graphIsPureFunction)
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2001,
                        $"WhenNode is not permitted in dispatch context '{asset.Dispatch}'.",
                        asset.AssetId, graph.Id, node.Id));
                }

                // BP2012 -- Edges set to None (check before mode-specific checks)
                if (node.Edges == WhenEdge.None)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2012,
                        "WhenNode has Edges set to None; at least one edge direction is required.",
                        asset.AssetId, graph.Id, node.Id));

                // BP2002 -- missing required payload
                bool missingPayload = node.Mode switch
                {
                    WhenMode.ValueChanged => node.ValueChanged == null,
                    WhenMode.EventFired   => node.EventFired == null,
                    WhenMode.ConditionMet => node.ConditionMet == null,
                    WhenMode.EqsResult    => node.EqsResult == null,
                    _                     => false,
                };
                if (missingPayload)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2002,
                        $"WhenNode Mode={node.Mode} requires a matching payload object.",
                        asset.AssetId, graph.Id, node.Id));

                // Mode-specific validation
                if (node.Mode == WhenMode.ValueChanged && node.ValueChanged != null)
                    ValidateValueChanged(asset, graph, node, node.ValueChanged, ctx);
                else if (node.Mode == WhenMode.EventFired && node.EventFired != null)
                    ValidateEventFired(asset, graph, node, node.EventFired, eventEntries, ctx);
                else if (node.Mode == WhenMode.ConditionMet && node.ConditionMet != null)
                    ValidateConditionMet(asset, graph, node, node.ConditionMet, ctx);
                else if (node.Mode == WhenMode.EqsResult && node.EqsResult != null)
                    ValidateEqsResult(asset, graph, node, node.EqsResult, ctx);

                // TODO BP2015: WhenNode downstream of a Branch -- deferred.
                // Exec pins are not materialized until Stage 3 (Normalize). When all
                // nodes in the graph have empty Pins lists, branch-successor detection
                // is not reliable. Implement after Stage 3 pin materialization.
            }
        }
    }

    private static void ValidateValueChanged(
        BlueprintAsset asset, Graph graph, WhenNode node,
        ValueChangedPayload vc, ValidationContext ctx)
    {
        // BP2003 -- invalid property path (not applicable for PeerBlueprintVariable source)
        if (vc.Source != ValueChangedSource.PeerBlueprintVariable
            && (string.IsNullOrEmpty(vc.ComponentTypeId) || string.IsNullOrEmpty(vc.PropertyPath)))
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2003,
                "WhenNode ValueChanged: ComponentTypeId and PropertyPath must not be empty.",
                asset.AssetId, graph.Id, node.Id));

        // BP2004 -- peer BP variable not declared
        if (vc.Source == ValueChangedSource.PeerBlueprintVariable)
        {
            if (vc.PeerBlueprintAssetId == null)
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2004,
                    "WhenNode ValueChanged Source=PeerBlueprintVariable: PeerBlueprintAssetId is null.",
                    asset.AssetId, graph.Id, node.Id));
            else if (!ctx.SiblingSignaturesById.ContainsKey(vc.PeerBlueprintAssetId.Value))
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2004,
                    $"WhenNode ValueChanged: peer blueprint {vc.PeerBlueprintAssetId} not in sibling signatures.",
                    asset.AssetId, graph.Id, node.Id));
        }

        // BP2014 -- epsilon on non-float field (warning, best-effort via reflection)
        // Only emit when the resolved property type is NOT a floating-point or vector type.
        // If the type cannot be resolved (e.g. unknown component), suppress BP2014 silently.
        if (vc.Epsilon != 0 && vc.Source != ValueChangedSource.PeerBlueprintVariable)
        {
            var resolvedType = TryResolvePropertyType(vc.ComponentTypeId, vc.PropertyPath);
            bool isFloatingPoint = resolvedType == typeof(float)
                || resolvedType == typeof(double)
                || resolvedType == typeof(System.Numerics.Vector2)
                || resolvedType == typeof(System.Numerics.Vector3);
            if (resolvedType != null && !isFloatingPoint)
                ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP2014,
                    "WhenNode ValueChanged: Epsilon is non-zero. "
                    + "Ensure the observed property is a floating-point type.",
                    asset.AssetId, graph.Id, node.Id));
        }
    }

    private static void ValidateEventFired(
        BlueprintAsset asset, Graph graph, WhenNode node,
        EventFiredPayload ef, IReadOnlyList<EngineEventCatalogEntry> catalogEntries,
        ValidationContext ctx)
    {
        // BP2005 -- event type not in catalog
        if (string.IsNullOrEmpty(ef.EventTypeId))
        {
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2005,
                "WhenNode EventFired: EventTypeId must not be empty.",
                asset.AssetId, graph.Id, node.Id));
        }
        else if (catalogEntries.Count > 0 && !catalogEntries.Any(e =>
            e.EventTypeFqn == ef.EventTypeId
            || Stage2Helpers.LastSegment(e.EventTypeFqn) == ef.EventTypeId))
        {
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2005,
                $"WhenNode EventFired: event type '{ef.EventTypeId}' is not in the engine event catalog.",
                asset.AssetId, graph.Id, node.Id));
        }

        // BP2006 -- Self filter without target field
        if (ef.TargetFilter == EventTargetFilter.Self && string.IsNullOrEmpty(ef.TargetFieldName))
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2006,
                "WhenNode EventFired TargetFilter=Self requires TargetFieldName to be specified.",
                asset.AssetId, graph.Id, node.Id));

        // BP2007 -- payload condition invalid
        if (ef.PayloadCheck != null
            && (string.IsNullOrEmpty(ef.PayloadCheck.PropertyPath)
                || string.IsNullOrEmpty(ef.PayloadCheck.TargetValueText)))
        {
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2007,
                "WhenNode EventFired PayloadCheck: PropertyPath and TargetValueText must not be empty.",
                asset.AssetId, graph.Id, node.Id));
        }

        // BP2013 -- FallingEdge on EventFired (warning: events have no falling edge)
        if ((node.Edges & WhenEdge.FallingEdge) != 0)
            ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP2013,
                "WhenNode EventFired with FallingEdge: events cannot have a falling edge; this edge will never fire.",
                asset.AssetId, graph.Id, node.Id));

        // Resolve the matched catalog entry for QoS / propagation checks (BP2016/BP2017).
        // Only run when EventTypeId is non-empty and was found in the catalog (BP2005 not fired).
        if (!string.IsNullOrEmpty(ef.EventTypeId) && catalogEntries.Count > 0)
        {
            var matchedEntry = catalogEntries.FirstOrDefault(e =>
                e.EventTypeFqn == ef.EventTypeId
                || Stage2Helpers.LastSegment(e.EventTypeFqn) == ef.EventTypeId);

            if (matchedEntry != null)
            {
                // BP2016 -- BestEffort event wired to a WhenNode (warning, non-blocking).
                // Designers who do this knowingly can suppress; we emit so it was an explicit choice.
                if (matchedEntry.QoS == EventQoS.BestEffort)
                    ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP2016,
                        $"WhenNode EventFired: event '{matchedEntry.Name}' has BestEffort QoS. "
                        + "This When-node may miss occurrences if the network drops the underlying "
                        + "UDP packet. Consider promoting the event to Reliable in its catalog entry, "
                        + "or restructure the dependent behavior to tolerate missed firings.",
                        asset.AssetId, graph.Id, node.Id));

                // BP2017 -- Brain Blueprint subscribing to a local-only (non-propagating) event (error).
                // Only fires when the compile context is explicitly Brain-targeted.
                if (!matchedEntry.PropagatesAcrossNodes
                    && ctx.ExecutionNode == ExecutionNodeHint.Brain)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2017,
                        $"WhenNode EventFired: event '{matchedEntry.Name}' is registered with "
                        + "PropagatesAcrossNodes=false and will never reach this Blueprint's "
                        + "execution node. Move the subscriber to the node where this event is "
                        + "locally published, or wrap the data in a cross-node typed event.",
                        asset.AssetId, graph.Id, node.Id));
            }
        }
    }

    private static void ValidateConditionMet(
        BlueprintAsset asset, Graph graph, WhenNode node,
        ConditionMetPayload cm, ValidationContext ctx)
    {
        // BP2008 -- predicate tree null or empty
        if (cm.Condition == null)
        {
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2008,
                "WhenNode ConditionMet: Condition predicate must not be null.",
                asset.AssetId, graph.Id, node.Id));
            return;
        }

        // BP2008 -- compound predicate with no children (inspect via JsonNode)
        if (IsEmptyCompoundPredicate(cm.Condition))
        {
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2008,
                "WhenNode ConditionMet: CompoundPredicateDto has no conditions.",
                asset.AssetId, graph.Id, node.Id));
        }

        // BP2009 -- predicate DTO references unknown type (inspect via JsonNode)
        if (HasUnresolvableComponentType(cm.Condition))
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2009,
                "WhenNode ConditionMet: predicate tree references a component type that could not be resolved.",
                asset.AssetId, graph.Id, node.Id));
    }

    /// <summary>
    /// Returns true if the JsonNode represents a CompoundPredicateDto with an empty conditions array.
    /// Works by inspecting the "$type" discriminator and "Conditions" array in the raw JSON node.
    /// </summary>
    private static bool IsEmptyCompoundPredicate(JsonNode? node)
    {
        if (node is not JsonObject obj) return false;
        var typeDiscriminator = obj["$type"]?.GetValue<string>();
        if (!string.Equals(typeDiscriminator, "Compound", StringComparison.OrdinalIgnoreCase))
            return false;
        // Try both "Conditions" (PascalCase from editor serialization) and "conditions" (camelCase)
        var conditions = (obj["Conditions"] ?? obj["conditions"]) as JsonArray;
        return conditions == null || conditions.Count == 0;
    }

    /// <summary>
    /// Returns true if any PropertyMatchDto node in the predicate tree has a null ComponentType.
    /// Works by inspecting the raw JsonNode tree without loading Fdp.Toolkits.
    /// </summary>
    private static bool HasUnresolvableComponentType(JsonNode? node)
    {
        if (node is not JsonObject obj) return false;
        var typeDiscriminator = obj["$type"]?.GetValue<string>();
        if (string.Equals(typeDiscriminator, "PropertyMatch", StringComparison.OrdinalIgnoreCase))
        {
            // ComponentType null or explicit JSON null means unresolvable.
            // Try both PascalCase (editor-serialized) and camelCase (fallback).
            var ct = obj["ComponentType"] ?? obj["componentType"];
            return ct == null; // null means key missing OR explicit JSON null
        }
        if (string.Equals(typeDiscriminator, "Compound", StringComparison.OrdinalIgnoreCase))
        {
            var conditions = (obj["Conditions"] ?? obj["conditions"]) as JsonArray;
            if (conditions == null) return false;
            foreach (var child in conditions)
            {
                if (HasUnresolvableComponentType(child)) return true;
            }
        }
        return false;
    }

    private static void ValidateEqsResult(
        BlueprintAsset asset, Graph graph, WhenNode node,
        EqsResultPayload er, ValidationContext ctx)
    {
        // BP2010 -- sensor variable not declared
        bool sensorDeclared = asset.Variables.Any(v =>
            v.Name == er.SensorVariableName
            && v.Type.TypeId == "FDP.Eqs.EqsSensorHandle");
        if (!sensorDeclared)
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2010,
                $"WhenNode EqsResult: sensor variable '{er.SensorVariableName}' "
                + "is not declared as EqsSensorHandle.",
                asset.AssetId, graph.Id, node.Id));

        // BP2011 -- trigger requires threshold/max-age
        if (er.Trigger == EqsTrigger.ScoreCrossed && er.ScoreThreshold == 0)
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2011,
                "WhenNode EqsResult Trigger=ScoreCrossed requires ScoreThreshold != 0.",
                asset.AssetId, graph.Id, node.Id));

        if (er.Trigger == EqsTrigger.BecomesStale && er.MaxAgeSeconds <= 0)
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2011,
                "WhenNode EqsResult Trigger=BecomesStale requires MaxAgeSeconds > 0.",
                asset.AssetId, graph.Id, node.Id));
    }

    // -----------------------------------------------------------------------
    // Reflection-based property type resolution for BP2014 (M10-T4)
    // -----------------------------------------------------------------------

    // Attempts to resolve the .NET System.Type of a component field/property.
    // Scans all loaded assemblies; returns null when resolution fails.
    private static System.Type? TryResolvePropertyType(string componentTypeId, string propertyPath)
    {
        if (string.IsNullOrEmpty(componentTypeId) || string.IsNullOrEmpty(propertyPath)) return null;

        System.Type? componentType = null;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            componentType = asm.GetType(componentTypeId);
            if (componentType != null) break;
        }
        if (componentType is null) return null;

        var field = componentType.GetField(propertyPath,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (field is not null) return field.FieldType;

        var prop = componentType.GetProperty(propertyPath,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        return prop?.PropertyType;
    }
}

// ---------------------------------------------------------------------------
// V_ReadEqsResultNodeRules (BP2020-BP2021)
// ---------------------------------------------------------------------------

internal sealed class V_ReadEqsResultNodeRules : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var graph in asset.Graphs)
        {
            bool isUnsupported = asset.Dispatch != BlueprintDispatchKind.Instance
                || (graph.Kind == GraphKind.Function
                    && !graph.Nodes.OfType<EventEntryNode>().Any());

            foreach (var node in graph.Nodes.OfType<ReadEqsResultNode>())
            {
                // BP2020 -- unsupported dispatch
                if (isUnsupported)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2020,
                        $"ReadEqsResultNode is not permitted in dispatch context '{asset.Dispatch}'.",
                        asset.AssetId, graph.Id, node.Id));

                // BP2021 -- sensor variable not declared
                bool sensorDeclared = asset.Variables.Any(v =>
                    v.Name == node.SensorVariableName
                    && v.Type.TypeId == "FDP.Eqs.EqsSensorHandle");
                if (!sensorDeclared)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2021,
                        $"ReadEqsResultNode: sensor variable '{node.SensorVariableName}' "
                        + "is not declared as EqsSensorHandle.",
                        asset.AssetId, graph.Id, node.Id));
            }
        }
    }
}

// ---------------------------------------------------------------------------
// V_SpawnEqsSensorNodeRules (BP2030-BP2031)
// ---------------------------------------------------------------------------

internal sealed class V_SpawnEqsSensorNodeRules : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        // BP2030 / BP2031 -- per-node checks (per graph)
        foreach (var graph in asset.Graphs)
        {
            bool isUnsupported = asset.Dispatch != BlueprintDispatchKind.Instance
                || (graph.Kind == GraphKind.Function
                    && !graph.Nodes.OfType<EventEntryNode>().Any());

            foreach (var node in graph.Nodes.OfType<SpawnEqsSensorNode>())
            {
                // BP2030 -- unsupported dispatch
                if (isUnsupported)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2030,
                        $"SpawnEqsSensorNode is not permitted in dispatch context '{asset.Dispatch}'.",
                        asset.AssetId, graph.Id, node.Id));

                // BP2031 -- template not found
                if (node.TemplateAssetId == Guid.Empty)
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2031,
                        "SpawnEqsSensorNode: TemplateAssetId must not be empty.",
                        asset.AssetId, graph.Id, node.Id));
                }
                else if (ctx.EqsTemplates != null
                    && !ctx.EqsTemplates.Contains(node.TemplateAssetId))
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2031,
                        $"SpawnEqsSensorNode: template '{node.TemplateAssetId}' is not in the EQS template catalog.",
                        asset.AssetId, graph.Id, node.Id));
                }
                // When ctx.EqsTemplates == null, no catalog is configured; BP2031 is suppressed.
            }
        }

        // BP2032: InstanceId collision between two SpawnEqsSensorNode instances in the same asset.
        // Runs once per asset (cross-graph) because InstanceId uniqueness must hold asset-wide.
        // An InstanceId collision means two sensors would share the same DDS replication key.
        var graphById = asset.Graphs.ToDictionary(g => g.Id);
        var allSpawnNodes = asset.Graphs
            .SelectMany(g => g.Nodes.OfType<SpawnEqsSensorNode>().Select(n => (Graph: g, Node: n)))
            .ToList();

        if (allSpawnNodes.Count > 1)
        {
            var instanceIdGroups = allSpawnNodes
                .GroupBy(x => BlueprintIdHash.Compute(x.Node.Id))
                .Where(g => g.Count() > 1);

            foreach (var collision in instanceIdGroups)
            {
                foreach (var (graph, collider) in collision)
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2032,
                        $"SpawnEqsSensorNode has InstanceId collision (hash {collision.Key}) with another SpawnEqsSensorNode in this asset. Use distinct node IDs.",
                        asset.AssetId, graph.Id, collider.Id));
                }
            }
        }
    }
}

// ---------------------------------------------------------------------------
// V_SharedStateRules (BP2040-BP2042 -- Slice 2a-2 GetShared/SetShared)
// ---------------------------------------------------------------------------

/// <summary>
/// Validates <see cref="GetSharedNode"/>/<see cref="SetSharedNode"/> nodes.
/// <list type="bullet">
///   <item>BP2040 -- <c>SharedTypeId</c> is empty.</item>
///   <item>BP2041 -- <c>SharedTypeId</c> does not look like a well-formed dotted CLR type FQN
///     (e.g. contains whitespace, is a bare/malformed identifier, or has an empty segment).
///     <para>
///     NOTE: this is a syntactic check, not full type resolution. The compiler's
///     <see cref="ITypeRegistry"/> accepts ANY "global::"-prefixed TypeId unconditionally (the AN2
///     "trust the FQN, let the downstream C# compiler catch a bad reference" strategy -- see
///     <see cref="StaticTypeRegistry.TryResolve"/>), and reflection-based resolution is unreliable in
///     the analyzer/generator host (the shared struct commonly lives in the very assembly being
///     compiled, per the same reasoning documented on <c>FunctionCallNode</c>'s CLR-reflection
///     fallback in <c>Stage0_Rehydrate</c>). A deterministic, host-independent syntax check is the
///     only meaningful signal available at this stage; genuine "type does not exist" errors surface
///     later as ordinary C# compiler errors on the emitted <c>global::{SharedTypeFqn}</c> reference.
///     </para>
///   </item>
///   <item>BP2042 -- node appears in a Library-dispatch asset, which has no <c>self</c> Entity in
///     scope (the generated call is <c>BlueprintSharedState.TryGetShared/TrySetShared(world, self,
///     ...)</c> -- <c>self</c> does not exist in a stateless Library function).</item>
/// </list>
/// Cross-entity checks are moot for 2a-2 -- there is no target-Entity pin (Slice 2b).
/// </summary>
internal sealed class V_SharedStateRules : IValidator
{
    // One-or-more dot/plus-separated C# identifier segments, optional "global::" prefix.
    // Rejects whitespace, empty segments, punctuation other than '.'/'+', etc.
    private static readonly System.Text.RegularExpressions.Regex FqnPattern = new(
        @"^(global::)?[A-Za-z_][A-Za-z0-9_]*([.+][A-Za-z_][A-Za-z0-9_]*)*$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var graph in asset.Graphs)
        {
            foreach (var node in graph.Nodes)
            {
                string? sharedTypeId = node switch
                {
                    GetSharedNode gsn => gsn.SharedTypeId,
                    SetSharedNode ssn => ssn.SharedTypeId,
                    _                 => null,
                };
                if (sharedTypeId is null) continue;

                if (asset.Dispatch == BlueprintDispatchKind.Library)
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2042,
                        $"{node.GetType().Name} is not permitted in a Library-dispatch asset -- " +
                        "there is no `self` Entity in scope for the shared-state accessor call.",
                        asset.AssetId, graph.Id, node.Id));

                if (string.IsNullOrEmpty(sharedTypeId))
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2040,
                        $"{node.GetType().Name}: SharedTypeId must not be empty.",
                        asset.AssetId, graph.Id, node.Id));
                    continue;
                }

                if (!FqnPattern.IsMatch(sharedTypeId))
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2041,
                        $"{node.GetType().Name}: SharedTypeId '{sharedTypeId}' does not resolve to a " +
                        "known unmanaged/blittable struct type (not a well-formed type name).",
                        asset.AssetId, graph.Id, node.Id));
            }
        }
    }
}

// ---------------------------------------------------------------------------
// V_FunctionGraphCallRules  (BATCH-03A + BATCH-03B)
// ---------------------------------------------------------------------------

/// <summary>
/// Validates all FunctionCallNode.TargetGraphId references across all graphs in the asset.
///
/// BP1650 — Latent node inside a called Function graph (BATCH-03A).
/// BP1651 — TargetGraphId does not resolve to a GraphKind.Function graph (BATCH-03B).
/// BP1652 — Caller data-IN pin count ≠ target graph Inputs.Count (BATCH-03B).
/// BP1653 — Positional argument type mismatch (conservative TypeId string comparison; BATCH-03B).
/// BP1654 — Function-graph call cycle (direct self-recursion or transitive A→B→A; BATCH-03B).
///
/// Type-compat mechanism (BP1653): Stage 2 runs before Stage 4 (TypeResolve), so full type
/// resolution is not yet available.  We compare BlueprintTypeRef.TypeId strings directly.
/// An empty TypeId or "System.Object" is treated as a wildcard (no flag).  This is
/// deliberately conservative: we only flag clear mismatches like "System.Int32" vs
/// "System.Single".  Limitation: generic type arguments and array wrapping are not compared
/// (only the top-level TypeId is checked), so e.g. List&lt;int&gt; vs List&lt;float&gt; would
/// not be caught unless TypeId itself differs.
/// </summary>
internal sealed class V_FunctionGraphCallRules : IValidator
{
    // Wildcards: an empty TypeId or System.Object means "any type accepted" – do not flag.
    private static bool IsWildcard(string typeId) =>
        string.IsNullOrEmpty(typeId) || typeId == "System.Object";

    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        // Build a lookup: graphId → Graph for all graphs in the asset.
        var graphById = asset.Graphs.ToDictionary(g => g.Id);

        // ---------------------------------------------------------------
        // Pass 1: per-node checks (BP1651, BP1652, BP1653) and
        //         build the directed call graph for cycle detection.
        // ---------------------------------------------------------------

        // callEdges[callerGraphId] = set of resolved target Graph.Id values
        var callEdges = new Dictionary<Guid, HashSet<Guid>>();

        foreach (var callerGraph in asset.Graphs)
        {
            foreach (var node in callerGraph.Nodes.OfType<FunctionCallNode>())
            {
                if (string.IsNullOrEmpty(node.TargetGraphId)) continue;

                // ----- BP1651: resolve target graph -----
                if (!Guid.TryParse(node.TargetGraphId, out var targetId)
                    || !graphById.TryGetValue(targetId, out var targetGraph)
                    || targetGraph.Kind != GraphKind.Function)
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1651,
                        $"FunctionCallNode (id={node.Id}) in graph '{callerGraph.Name}' references " +
                        $"TargetGraphId='{node.TargetGraphId}' which does not resolve to a " +
                        $"GraphKind.Function graph in this asset.",
                        asset.AssetId, callerGraph.Id, node.Id));
                    continue; // skip BP1652/BP1653 for this node
                }

                // Record call edge for cycle detection (BP1654).
                if (!callEdges.TryGetValue(callerGraph.Id, out var targets))
                {
                    targets = new HashSet<Guid>();
                    callEdges[callerGraph.Id] = targets;
                }
                targets.Add(targetGraph.Id);

                // ----- BP1652: argument count -----
                var dataInPins = node.Pins
                    .Where(p => !p.IsExec && string.Equals(p.Direction, "In", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (dataInPins.Count != targetGraph.Inputs.Count)
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1652,
                        $"FunctionCallNode (id={node.Id}) in graph '{callerGraph.Name}' passes " +
                        $"{dataInPins.Count} data-IN arg(s) but target Function graph " +
                        $"'{targetGraph.Name}' (id={targetGraph.Id}) declares " +
                        $"{targetGraph.Inputs.Count} input(s).",
                        asset.AssetId, callerGraph.Id, node.Id));
                    // Still check latent nodes (BP1650) below; skip type check.
                    continue;
                }

                // ----- BP1653: positional argument types (conservative) -----
                for (int i = 0; i < dataInPins.Count; i++)
                {
                    var callerTypeId = dataInPins[i].TypeRef?.TypeId ?? "";
                    var targetTypeId = targetGraph.Inputs[i].Type?.TypeId ?? "";

                    // Skip if either side is a wildcard / unresolved.
                    if (IsWildcard(callerTypeId) || IsWildcard(targetTypeId)) continue;

                    if (!string.Equals(callerTypeId, targetTypeId, StringComparison.Ordinal))
                    {
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1653,
                            $"FunctionCallNode (id={node.Id}) in graph '{callerGraph.Name}': " +
                            $"argument {i} (pin '{dataInPins[i].Name}') has type '{callerTypeId}' " +
                            $"but target input '{targetGraph.Inputs[i].Name}' of graph " +
                            $"'{targetGraph.Name}' expects '{targetTypeId}'.",
                            asset.AssetId, callerGraph.Id, node.Id));
                    }
                }
            }
        }

        // ---------------------------------------------------------------
        // Pass 2: BP1650 — latent nodes inside any called Function graph.
        // (Preserved from BATCH-03A; re-implemented inline to share graphById.)
        // ---------------------------------------------------------------
        var calledGraphIds = new HashSet<Guid>(
            callEdges.Values.SelectMany(s => s));

        foreach (var targetGraph in asset.Graphs.Where(g => calledGraphIds.Contains(g.Id)))
        {
            foreach (var node in targetGraph.Nodes)
            {
                if (node is LatentDelayNode or WaitForChannelNode or WaitForEventNode)
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1650,
                        $"Function graph '{targetGraph.Name}' (id={targetGraph.Id}) is called via FunctionCallNode " +
                        $"but contains latent node '{node.GetType().Name}' (id={node.Id}). " +
                        $"A function graph invoked by FunctionCall must not contain latent nodes; " +
                        $"latent execution is only supported in the top-level Tick/event graphs.",
                        asset.AssetId, targetGraph.Id, node.Id));
                }
            }
        }

        // ---------------------------------------------------------------
        // Pass 3: BP1654 — cycle detection via DFS over call graph.
        // Only Function graphs participate (non-Function graphs cannot be
        // called via FunctionCallNode after BP1651 has been checked).
        // ---------------------------------------------------------------
        if (callEdges.Count == 0) return;

        // Three-colour DFS: 0=white(unvisited), 1=grey(in-stack), 2=black(done).
        var colour  = new Dictionary<Guid, int>();
        var parent  = new Dictionary<Guid, Guid>();
        var emittedCycles = new HashSet<string>(); // deduplicate cycle reports

        // Initialise all Function graphs to white.
        foreach (var g in asset.Graphs)
            if (g.Kind == GraphKind.Function)
                colour[g.Id] = 0;

        foreach (var startId in colour.Keys.ToList())
        {
            if (colour[startId] != 0) continue;
            DfsVisit(startId, graphById, callEdges, colour, parent,
                     asset.AssetId, ctx, emittedCycles);
        }
    }

    private static void DfsVisit(
        Guid nodeId,
        Dictionary<Guid, Graph> graphById,
        Dictionary<Guid, HashSet<Guid>> callEdges,
        Dictionary<Guid, int> colour,
        Dictionary<Guid, Guid> parent,
        Guid assetId,
        ValidationContext ctx,
        HashSet<string> emittedCycles)
    {
        colour[nodeId] = 1; // grey

        if (callEdges.TryGetValue(nodeId, out var neighbours))
        {
            foreach (var neighbourId in neighbours)
            {
                if (!colour.TryGetValue(neighbourId, out var nc))
                    continue; // not a Function graph — skip

                if (nc == 1)
                {
                    // Back-edge: reconstruct cycle path.
                    var cyclePath = BuildCyclePath(nodeId, neighbourId, parent, graphById);
                    var key = string.Join("→", cyclePath.OrderBy(s => s)); // canonical key
                    if (emittedCycles.Add(key))
                    {
                        var path = string.Join(" → ", cyclePath);
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1654,
                            $"Function-graph call cycle detected: {path}. " +
                            $"Function graphs compile to synchronous C# methods; a cycle would " +
                            $"cause a stack overflow at runtime.",
                            assetId));
                    }
                }
                else if (nc == 0)
                {
                    parent[neighbourId] = nodeId;
                    DfsVisit(neighbourId, graphById, callEdges, colour, parent,
                             assetId, ctx, emittedCycles);
                }
            }
        }

        colour[nodeId] = 2; // black
    }

    /// <summary>
    /// Reconstructs the cycle path from the back-edge (currentId → cycleStartId)
    /// by walking parent pointers from currentId back to cycleStartId.
    /// </summary>
    private static List<string> BuildCyclePath(
        Guid currentId,
        Guid cycleStartId,
        Dictionary<Guid, Guid> parent,
        Dictionary<Guid, Graph> graphById)
    {
        var path = new List<string>();
        var visited = new HashSet<Guid>();
        var id = currentId;

        while (id != cycleStartId && !visited.Contains(id))
        {
            visited.Add(id);
            path.Add(graphById.TryGetValue(id, out var g) ? g.Name : id.ToString());
            if (!parent.TryGetValue(id, out id)) break;
        }

        // Add the cycle-start node name at both ends to show the loop.
        var startName = graphById.TryGetValue(cycleStartId, out var sg) ? sg.Name : cycleStartId.ToString();
        path.Add(startName);
        path.Reverse();
        path.Add(startName); // close the cycle notation: A → B → A
        return path;
    }
}

// ---------------------------------------------------------------------------
// V_ExecOutFanOut  (EXEC1 -- BF-BATCH-EXECFANOUT)
// ---------------------------------------------------------------------------

/// <summary>
/// BP1411 -- Each exec-output pin must drive at most one successor.
/// Fan-out from a single exec-out pin is silently dropped by the scheduler, so
/// this validator makes it a hard error before scheduling runs.
/// <para>
/// The rule is <em>per pin</em>, not per node:
/// <see cref="BranchNode"/> (True/False), <see cref="SequenceNode"/> (Then0/Then1), and
/// <see cref="WhenNode"/> (OnFired/OnEnded/Out) each have multiple exec-out pins, but each
/// individual pin must still drive at most one successor.  The correct way to run two
/// branches off a single pin is an explicit <see cref="SequenceNode"/>.
/// </para>
/// </summary>
internal sealed class V_ExecOutFanOut : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var graph in asset.Graphs)
        {
            // Skip graphs where no pin data is present; pins are populated from the node
            // registry in later stages and are not stored in the raw JSON asset.
            if (!graph.Nodes.Any(n => n.Pins.Count > 0)) continue;

            foreach (var node in graph.Nodes)
            {
                foreach (var pin in node.Pins)
                {
                    if (!pin.IsExec || pin.Direction != "Out") continue;

                    int count = graph.Links.Count(l =>
                        l.FromNodeId == node.Id && l.FromPinId == pin.Id);

                    if (count > 1)
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1411,
                            $"Exec output pin '{pin.Name}' on node '{node.Id}' " +
                            $"({node.GetType().Name}) drives {count} successors; " +
                            $"an exec output drives exactly one. " +
                            $"Use a Sequence node to fan out.",
                            asset.AssetId, graph.Id, node.Id));
                }
            }
        }
    }
}

// ---------------------------------------------------------------------------
// V_FlowForEachRules (P1 -- GAP-1, BP2050)
// ---------------------------------------------------------------------------

/// <summary>
/// P1 (GAP-1): a <see cref="FlowForEachNode"/>'s "Body" exec-subgraph must be a synchronous,
/// latent-free sub-DAG -- and (P1a) branch-free. The body lowers to an inline C# <c>for</c> whose
/// statements are scheduled inline (not BFS blocks), so a latent node (which needs a suspend/resume
/// block split) or a Branch (which needs a block split, deferred to P1b) cannot appear inside it.
/// </summary>
internal sealed class V_FlowForEachRules : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var graph in asset.Graphs)
        {
            var nodeById = graph.Nodes.ToDictionary(n => n.Id);
            foreach (var fe in graph.Nodes.OfType<FlowForEachNode>())
            {
                var bodyPin = fe.Pins.FirstOrDefault(p => p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Body", StringComparison.OrdinalIgnoreCase));
                if (bodyPin is null) continue;

                var visited = new HashSet<Guid>();
                var queue = new Queue<Node>();
                foreach (var start in ExecTargets(graph, fe.Id, bodyPin.Id, nodeById))
                    queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    var n = queue.Dequeue();
                    if (!visited.Add(n.Id)) continue;

                    if (n is BranchNode)
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2050,
                            "FlowForEach body must be branch-free (P1a): a Branch node is reachable from the loop 'Body'.",
                            asset.AssetId, graph.Id, n.Id));
                    else if (n is LatentDelayNode or WaitForChannelNode or WaitForEventNode or WhenNode)
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2050,
                            $"FlowForEach body must be latent-free: a latent '{n.GetType().Name}' is reachable from the loop 'Body'.",
                            asset.AssetId, graph.Id, n.Id));

                    foreach (var outPin in n.Pins.Where(p => p.IsExec && p.Direction == "Out"))
                        foreach (var succ in ExecTargets(graph, n.Id, outPin.Id, nodeById))
                            queue.Enqueue(succ);
                }
            }
        }
    }

    private static IEnumerable<Node> ExecTargets(
        Graph graph, Guid fromNode, Guid fromPin, Dictionary<Guid, Node> nodeById)
    {
        foreach (var link in graph.Links)
            if (link.FromNodeId == fromNode && link.FromPinId == fromPin
                && nodeById.TryGetValue(link.ToNodeId, out var target))
                yield return target;
    }
}
