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
        new V_ValueNodeReferences(),   // BP-15
        new V_CustomEventHandlers(),   // BP-12c
        new V_UnloweredNodeKinds(),    // BP-16
        new V_PeerReferences(),
        new V_TypeReferences(),
        new V_DeterminismOrdering(),
        new V_WhenNodeRules(),
        new V_FlowForEachRules(),
        new V_ReadEqsResultNodeRules(),
        new V_SpawnEqsSensorNodeRules(),
        new V_SharedStateRules(),
        new V_ComponentAccessRules(),
        new V_ListVariableRules(),
        new V_FunctionGraphCallRules(),
        new V_MacroCallRules(),             // BP-81/BP-82: BP1660/BP1661/BP1662/BP1663
        new V_LocalVariableRules(),         // BP-57/Q27: BP1664/BP1669
        new V_FunctionGraphReturnValue(),   // BP-71 (BP1655) + BP-73 gate (BP1656)
        new V_ExecOutFanOut(),
        new V_FormatStringRules(),   // BP-108 (BP2072)
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

            // Q#13-B (architect): a WIRED "OnFailure" chain must terminate in an explicit Return — no
            // implicit-return fall-off on the failure branch. Any node reachable from OnFailure that is
            // a dead end (no wired exec-out) and is not a ReturnNode is a fall-off ⇒ BP1102. Applies to
            // any node exposing an "OnFailure" exec-out (WaitForChannel + WaitForEvent + future nodes).
            // (Runs only on pin-ful graphs, like every V_GraphStructure check — the editor/authoring
            // path, where designers create the wiring.)
            foreach (var wfc in graph.Nodes)
            {
                var onFailPin = wfc.Pins.FirstOrDefault(
                    p => p.IsExec && p.Direction == "Out"
                      && string.Equals(p.Name, "OnFailure", StringComparison.OrdinalIgnoreCase));
                if (onFailPin is null) continue;

                var failLink = graph.Links.FirstOrDefault(
                    l => l.FromNodeId == wfc.Id && l.FromPinId == onFailPin.Id);
                if (failLink is null) continue; // OnFailure unwired ⇒ auto-Failure, nothing to enforce.

                var failReachable = new HashSet<Guid>();
                CollectExecReachable(graph, failLink.ToNodeId, failReachable);

                foreach (var reachedId in failReachable)
                {
                    var reached = graph.Nodes.FirstOrDefault(n => n.Id == reachedId);
                    if (reached is null or ReturnNode) continue;

                    bool hasWiredExecOut = reached.Pins.Any(
                        p => p.IsExec && p.Direction == "Out"
                          && graph.Links.Any(l => l.FromNodeId == reached.Id && l.FromPinId == p.Id));
                    if (!hasWiredExecOut)
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1102,
                            $"WaitForChannel 'OnFailure' chain must terminate in an explicit Return node; " +
                            $"'{reached.GetType().Name}' (id={reached.Id}) is a dead end.",
                            asset.AssetId, graph.Id, reached.Id));
                }
            }
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
                            $"A Function Library cannot contain latent nodes: its graphs compile to plain " +
                            $"static methods, which have nowhere to suspend. Remove " +
                            $"'{FriendlyNodeName(node)}' from graph '{graph.Name}', or move this logic into " +
                            $"an Event graph on an Instance blueprint.",
                            asset.AssetId, graph.Id, node.Id));
                }
            }
        }
    }

    /// <summary>
    /// The node's palette-facing name (its CLR type name without the <c>Node</c> suffix), so the
    /// diagnostic names what the designer sees on the canvas rather than a raw GUID.
    /// </summary>
    private static string FriendlyNodeName(Node node)
    {
        var name = node.GetType().Name;
        // netstandard2.0 is one of this project's targets — no range operator here.
        return name.EndsWith("Node", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - 4)
            : name;
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

                // Q#14: a fully-qualified custom-event identity (contains '.') is a baked [BlueprintEvent]
                // subscription the compiler cannot verify (netstandard2.0 can't reflect game assemblies) —
                // trust it, mirroring the baked PublishEvent FQN path. Non-FQN unknown names still error
                // (typo guard for hand-referenced catalog events). (IndexOf: netstandard2.0 has no char Contains.)
                if (node.EventTypeId.IndexOf('.') >= 0) continue;

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

// ---------------------------------------------------------------------------
// V_UnloweredNodeKinds  (BP-16)
// ---------------------------------------------------------------------------

/// <summary>
/// BP-16 — rejects node kinds that reach codegen with no Stage5 lowering.
///
/// <para>
/// <c>ArrayMakeNode</c> and <c>ArrayGetNode</c> have no <c>Stage5_Schedule</c> case. On the exec path
/// they fall to the generic <c>default:</c> branch, which emits a BP4004 <b>warning</b> and no IR. But
/// reading their output pin goes through the separate pure-data-value resolver, whose <c>default:</c>
/// branch emits <c>IrOp_Const("default", pinType)</c> with <b>no diagnostic at all</b> — so the asset
/// compiles clean and returns wrong data at runtime. <c>NodeCoverageTests</c> documents this asymmetry
/// verbatim ("worse than the BP4004 case").
/// </para>
///
/// <para>
/// Erroring here converts silent data corruption into a build failure. Deliberately an <b>error</b>, not
/// a warning: BP4004's warning still lets the asset "succeed", which is the behaviour that hid the bug.
/// Fixed-capacity list variables are the supported vehicle for collection storage.
/// </para>
/// </summary>
internal sealed class V_UnloweredNodeKinds : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var graph in asset.Graphs)
        {
            foreach (var node in graph.Nodes)
            {
                string? kind = node switch
                {
                    ArrayMakeNode => nameof(ArrayMakeNode),
                    ArrayGetNode  => nameof(ArrayGetNode),
                    _             => null
                };
                if (kind is null) continue;

                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1420,
                    $"'{kind}' has no compiler lowering. Its exec path emits no IR, and reading its "
                    + "output pin silently yields default(T) with no diagnostic — the asset would "
                    + "compile clean and return wrong data. Remove the node; use a fixed-capacity "
                    + "list variable for collection storage.",
                    asset.AssetId, graph.Id, node.Id));
            }
        }
    }
}

// ---------------------------------------------------------------------------
// V_ValueNodeReferences  (BP-15)
// ---------------------------------------------------------------------------

/// <summary>
/// BP-15 — reference checks for four node kinds that previously had no validator at all, so a typo or
/// an unset field passed Stage 2 silently.
///
/// <list type="bullet">
///   <item><c>CallCustomEventNode.EventId</c> — mirrors <see cref="V_EventGraphReferences"/>, which
///   validates only the <i>subscribe</i> side (<c>EventEntryNode</c>); the <i>call</i> side was
///   unchecked. Same escape hatches: a custom-event GUID, a catalog match, or a dotted FQN the
///   compiler cannot verify.</item>
///   <item><c>ScoreDecisionNode.AssetId</c> — must be a well-formed GUID. No
///   <c>UtilityDecisionDef</c> catalog exists editor-side (see BP-27), so existence cannot be checked
///   here; shape is the strongest available guard.</item>
///   <item><c>ReadRankedResultNode.Rank</c> — documented as a 0-based index, so a negative rank can
///   never match.</item>
///   <item><c>CastNode.TargetTypeId</c> — empty only. An <i>unresolvable</i> target is already caught
///   as BP1500 by <see cref="V_TypeReferences"/>, because <c>BuiltInNodeRegistry</c> projects the
///   out-pin type from this field. The empty case escapes that check: the registry substitutes
///   <c>System.Object</c>, which resolves fine and makes the cast a silent no-op.</item>
/// </list>
/// </summary>
internal sealed class V_ValueNodeReferences : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        // Mirror Stage5's FindCustomEventIndex, which resolves an EventId against asset.CustomEvents
        // by parsed Guid OR by Name. Matching only on Guid here would reject the ordinary authoring
        // shape -- CallCustomEvent("OnFire") against .WithCustomEvent("OnFire").
        var customEventIds   = new HashSet<Guid>(asset.CustomEvents.Select(e => e.Id));
        var customEventNames = new HashSet<string>(
            asset.CustomEvents.Select(e => e.Name), StringComparer.Ordinal);
        var engineEvents     = ctx.EngineEvents.GetEntries();

        foreach (var graph in asset.Graphs)
        {
            foreach (var node in graph.Nodes)
            {
                switch (node)
                {
                    case CallCustomEventNode call:
                        ValidateCustomEventCall(
                            call, asset, graph, ctx, customEventIds, customEventNames, engineEvents);
                        break;

                    // Non-empty only. Decision asset ids are NOT parseable Guids by convention -- the
                    // shipped CombatPostureDecision uses "3c6f9e42-5d10-6f3a-ac23-posture0000001", a
                    // deliberately human-readable pseudo-GUID. Requiring Guid.TryParse would reject
                    // real production assets. No UtilityDecisionDef catalog exists editor-side
                    // (see BP-27), so existence cannot be checked here either.
                    case ScoreDecisionNode score when string.IsNullOrWhiteSpace(score.AssetId):
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1404,
                            "ScoreDecision has no target decision asset (AssetId is empty).",
                            asset.AssetId, graph.Id, node.Id));
                        break;

                    case ReadRankedResultNode ranked when ranked.Rank < 0:
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1405,
                            $"ReadRankedResult.Rank is {ranked.Rank}; rank is a 0-based index and "
                            + "cannot be negative.",
                            asset.AssetId, graph.Id, node.Id));
                        break;

                    case CastNode cast when string.IsNullOrWhiteSpace(cast.TargetTypeId):
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1406,
                            "Cast has no TargetTypeId; the node would silently degrade to a "
                            + "System.Object no-op cast.",
                            asset.AssetId, graph.Id, node.Id));
                        break;
                }
            }
        }
    }

    private static void ValidateCustomEventCall(
        CallCustomEventNode call,
        BlueprintAsset asset,
        Graph graph,
        ValidationContext ctx,
        HashSet<Guid> customEventIds,
        HashSet<string> customEventNames,
        IReadOnlyList<EngineEventCatalogEntry> engineEvents)
    {
        if (string.IsNullOrWhiteSpace(call.EventId))
        {
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1403,
                "CallCustomEvent has no target event (EventId is empty).",
                asset.AssetId, graph.Id, call.Id));
            return;
        }

        // An asset-authored custom event, matched by GUID or by name -- both are what
        // Stage5's FindCustomEventIndex accepts.
        if (Guid.TryParse(call.EventId, out var eventGuid) && customEventIds.Contains(eventGuid))
            return;
        if (customEventNames.Contains(call.EventId))
            return;

        // A dotted identity is a baked [BlueprintEvent] the compiler cannot verify
        // (netstandard2.0 cannot reflect game assemblies) -- trust it, as V_EventGraphReferences does.
        if (call.EventId.IndexOf('.') >= 0) return;

        // Empty catalog means none is configured -- skip the unknown-name check (opt-in),
        // matching V_EventGraphReferences.
        if (engineEvents.Count == 0) return;

        if (engineEvents.Any(e =>
                e.EventTypeFqn == call.EventId
                || Stage2Helpers.LastSegment(e.EventTypeFqn) == call.EventId))
            return;

        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1403,
            $"CallCustomEvent references unknown event '{call.EventId}'.",
            asset.AssetId, graph.Id, call.Id));
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
// V_CustomEventHandlers  (BP-12c)
// ---------------------------------------------------------------------------

/// <summary>
/// BP-12c — a declared custom event is only half a custom event.
///
/// <para>
/// The <i>declaration</i> lives on the asset (<see cref="BlueprintAsset.CustomEvents"/>) and gives
/// the call node its argument pins. The <i>body</i> is an <see cref="GraphKind.Event"/> graph whose
/// <c>Name</c> matches: <c>InstanceEmitter.EmitEventMethod</c> emits
/// <c>Event_{graph.Name}(…)</c> with one C# parameter per <c>graph.Inputs</c> entry, and
/// <c>StatementEmitter</c> lowers <c>IrOp_RaiseCustomEvent</c> to a direct call to that method with
/// one argument per <i>declaration</i> parameter.
/// </para>
///
/// <para>
/// So a call to an event with no matching Event graph — or to one whose graph takes a different
/// number of inputs — produces generated C# that does not compile. <c>V_ValueNodeReferences</c>
/// (BP-15) already rejects a call to an event that isn't declared at all; this validator covers the
/// declared-but-unhandled case, which until now surfaced only as a Roslyn error naming a method the
/// designer never wrote.
/// </para>
///
/// <para>
/// <b>Call sites only.</b> Declaring a custom event and never calling it is legal and silent — the
/// editor's create path (BP-12c) produces exactly that, and there is no way to author the paired
/// Event graph in the editor yet (BP-24). Erroring on the declaration would make the new create
/// button emit a broken asset on first use.
/// </para>
/// </summary>
internal sealed class V_CustomEventHandlers : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        if (asset.CustomEvents.Count == 0) return;

        var eventGraphsByName = new Dictionary<string, Graph>(StringComparer.Ordinal);
        foreach (var g in asset.Graphs)
        {
            if (g.Kind != GraphKind.Event) continue;
            if (string.IsNullOrEmpty(g.Name)) continue;
            // First wins; a duplicate Event-graph name is V_GraphStructure's business.
            if (!eventGraphsByName.ContainsKey(g.Name))
                eventGraphsByName[g.Name] = g;
        }

        foreach (var graph in asset.Graphs)
        {
            foreach (var call in graph.Nodes.OfType<CallCustomEventNode>())
            {
                var decl = ResolveDecl(asset, call.EventId);

                // Unresolved ids are BP1403's job (and a dotted FQN is a baked engine event that
                // never routes through Event_{Name} at all).
                if (decl is null) continue;

                if (!eventGraphsByName.TryGetValue(decl.Name, out var handler))
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1407,
                        $"Custom event '{decl.Name}' is declared but has no handler: the call lowers "
                        + $"to Event_{decl.Name}(...), which is emitted from an Event graph named "
                        + $"'{decl.Name}'. Add one, or remove the call.",
                        asset.AssetId, graph.Id, call.Id));
                    continue;
                }

                if (handler.Inputs.Count != decl.Parameters.Count)
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1408,
                        $"Custom event '{decl.Name}' declares {decl.Parameters.Count} parameter(s) "
                        + $"but its handler graph takes {handler.Inputs.Count} input(s); the emitted "
                        + "call would not match Event_" + decl.Name + "'s signature.",
                        asset.AssetId, graph.Id, call.Id));
                }
            }
        }
    }

    /// <summary>
    /// Mirrors Stage5's <c>FindCustomEventIndex</c> — a GUID (what the editor writes) or a bare
    /// Name (hand-authored assets). Anything else is not an asset-scoped custom event.
    /// </summary>
    private static CustomEventDecl? ResolveDecl(BlueprintAsset asset, string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return null;

        if (Guid.TryParse(eventId, out var guid))
        {
            foreach (var e in asset.CustomEvents)
                if (e.Id == guid) return e;
            return null;
        }

        foreach (var e in asset.CustomEvents)
            if (string.Equals(e.Name, eventId, StringComparison.Ordinal)) return e;
        return null;
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
// V_ComponentAccessRules (BP2060-BP2065 -- CA-03/CA-05/CA-06: SetComponent/GetComponent access)
// ---------------------------------------------------------------------------

/// <summary>
/// Validates <see cref="SetComponentNode"/> and <see cref="GetComponentNode"/> nodes.
/// <list type="bullet">
///   <item>BP2060 -- <see cref="SetComponentNode"/>.<c>ComponentTypeFqn</c> is empty.</item>
///   <item>BP2061 -- <see cref="SetComponentNode"/>.<c>ComponentTypeFqn</c> does not look like a
///     well-formed dotted CLR type FQN (same syntactic-only check as <see cref="V_SharedStateRules"/>'s
///     BP2041 -- see that validator's doc comment for why a full-resolution check is not meaningful
///     here).</item>
///   <item>BP2062 -- the node carries a "Target" pin. <see cref="SetComponentNode"/> is SELF-ONLY
///     by construction (Q#16) -- <c>Stage0_Rehydrate.EnrichSetComponentPins</c> never projects
///     one, so a "Target" pin here can only come from a hand-authored/legacy asset; flagged
///     regardless of whether the pin is actually linked.</item>
///   <item>BP2063 (CA-05, Slice 1b) -- a <see cref="GetComponentNode"/> with <c>IsManaged == true</c>
///     has one of its FIELD out-pins wired directly into a persisting sink (<see cref="SetVariableNode"/>
///     or <see cref="SetSharedNode"/>). Rule G1 (Q#15): a managed component-read value is
///     read-and-pass-to-managed-consumer only -- never persisted. See this rule's own doc comment
///     below for what BP1503/BP1501 already cover vs. the gap this closes.</item>
///   <item>BP2064 (CA-06, Slice W2, Q#16-C) -- a <see cref="SetComponentNode"/> with
///     <c>IsManaged == true</c> ALSO carries per-field <c>Fields</c>. Managed writes are
///     whole-replace-only (a single "Value" pin) -- per-field managed write is forbidden (snapshot
///     aliasing).</item>
///   <item>BP2065 (CA-06, Slice W2) -- a managed <see cref="SetComponentNode"/> in an
///     AiPrimitive-dispatch asset. AiPrimitive's generated <c>TickCore</c> has no
///     <c>IEntityCommandBuffer</c> parameter in scope (see <c>AiPrimitiveEmitter.EmitTickCore</c>),
///     so there is nowhere to queue <c>ecb.SetManagedComponent</c>.</item>
///   <item>BP2066 (CA-07b) -- a component-collection consumer (<see cref="ComponentForEachNode"/>/
///     <see cref="ComponentItemGetNode"/>/<see cref="ComponentItemCountNode"/>) whose "Collection"
///     data-in pin IS wired but whose baked accessor FQNs are empty. The wiring is meaningless
///     without the bake (CA-07c wires it at edit time); Stage5 would otherwise silently degrade to
///     its "safe default" (no read, out-pin resolves to <c>default</c>) with no diagnostic at all.</item>
/// </list>
/// <para>
/// Deliberately absent: a <c>[BlueprintWritable]</c> check. The compiler runs as a netstandard2.0
/// Roslyn analyzer and cannot reflect over the real component type to see the attribute (the same
/// reflection-free constraint documented throughout this file's other validators/Nodes.cs) --
/// <c>[BlueprintWritable]</c> is an EDITOR-primary gate (CA-04's write picker); see
/// <see cref="Fdp.Core.BlueprintWritableAttribute"/>'s doc comment.
/// </para>
/// </summary>
internal sealed class V_ComponentAccessRules : IValidator
{
    // Same syntactic FQN check as V_SharedStateRules -- one-or-more dot/plus-separated C#
    // identifier segments, optional "global::" prefix.
    private static readonly System.Text.RegularExpressions.Regex FqnPattern = new(
        @"^(global::)?[A-Za-z_][A-Za-z0-9_]*([.+][A-Za-z_][A-Za-z0-9_]*)*$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var graph in asset.Graphs)
        {
            foreach (var node in graph.Nodes)
            {
                if (node is SetComponentNode scn)
                {
                    if (string.IsNullOrEmpty(scn.ComponentTypeFqn))
                    {
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2060,
                            $"{nameof(SetComponentNode)}: ComponentTypeFqn must not be empty.",
                            asset.AssetId, graph.Id, node.Id));
                    }
                    else if (!FqnPattern.IsMatch(scn.ComponentTypeFqn))
                    {
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2061,
                            $"{nameof(SetComponentNode)}: ComponentTypeFqn '{scn.ComponentTypeFqn}' is not " +
                            "a well-formed type name.",
                            asset.AssetId, graph.Id, node.Id));
                    }

                    if (node.Pins.Any(p =>
                            !p.IsExec && string.Equals(p.Name, "Target", StringComparison.OrdinalIgnoreCase)))
                    {
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2062,
                            $"{nameof(SetComponentNode)} is self-only -- a \"Target\" pin/link is not permitted.",
                            asset.AssetId, graph.Id, node.Id));
                    }

                    if (scn.IsManaged)
                    {
                        // BP2064 (CA-06, Slice W2, Q#16-C) -- managed write is WHOLE-REPLACE ONLY.
                        // A managed node that ALSO carries per-field Fields (hand-authored/legacy
                        // asset, or an editor bug) is structurally contradictory -- reject it rather
                        // than silently ignoring the Fields list or, worse, letting some future
                        // Stage5 change accidentally read it for a per-field managed write (the
                        // architect-forbidden shape: per-field managed write risks snapshot aliasing).
                        if (scn.Fields is { Count: > 0 })
                        {
                            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2064,
                                $"{nameof(SetComponentNode)}: a managed (IsManaged=true) node must not " +
                                "carry per-field Fields -- managed writes are whole-replace-only " +
                                "(single \"Value\" pin). Per-field managed write is forbidden.",
                                asset.AssetId, graph.Id, node.Id));
                        }

                        // BP2065 (CA-06, Slice W2) -- AiPrimitive's generated TickCore(ref Params p,
                        // ref WorkingState ws, Entity self, EntityRepository world, float time) (see
                        // AiPrimitiveEmitter.EmitTickCore) carries NO IEntityCommandBuffer parameter
                        // at all -- there is no ECB in scope to queue ecb.SetManagedComponent on.
                        // Instance dispatch's Tick/Event/Func_* methods (InstanceEmitter) all declare
                        // one; Library dispatch has no `self` either (a separate, pre-existing gap
                        // this rule does not attempt to close). Reject HERE, at Stage2, so the
                        // pipeline never reaches Stage5/emit for this asset (BlueprintCompiler.Compile
                        // stops at the first Stage2 error) -- EmissionContext.EcbVar's AiPrimitive
                        // branch throws as defense-in-depth for exactly this case.
                        if (asset.Dispatch == BlueprintDispatchKind.AiPrimitive)
                        {
                            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2065,
                                $"{nameof(SetComponentNode)}: a managed (IsManaged=true) write is not " +
                                "permitted in an AiPrimitive-dispatch asset -- TickCore has no " +
                                "IEntityCommandBuffer in scope to queue the write on.",
                                asset.AssetId, graph.Id, node.Id));
                        }
                    }
                }
                else if (node is GetComponentNode { IsManaged: true } gcn && gcn.Fields is { Count: > 0 })
                {
                    CheckManagedReadNotPersisted(gcn, graph, asset, ctx);
                }
                else if (node is ComponentForEachNode or ComponentItemGetNode or ComponentItemCountNode
                                 or ComponentContainsNode or ComponentFindNode)
                {
                    CheckComponentCollectionConsumer(node, graph, asset, ctx);
                }
                else if (node is CollectionWriteNode cwn)
                {
                    CheckCollectionWrite(cwn, graph, asset, ctx);
                }
            }

            // G3 (Q#20/BP2071): warn on a collection write inside a ForEach body iterating the
            // SAME collection -- checked per graph, after the per-node loop (needs both endpoints).
            CheckWriteInsideIteration(graph, asset, ctx);
        }
    }

    /// <summary>
    /// FC-1 (Q#20) -- BP2067/BP2068/BP2069/BP2070. Mirrors <see cref="CheckComponentCollectionConsumer"/>'s
    /// wired-gating for the bake checks (unwired "Collection" is a legitimate not-used-yet state --
    /// Stage5 degrades it to Ok=false silently), but the self-only checks (BP2069 "Target" pin,
    /// BP2068 managed kind) fire regardless of wiring -- they are structural contradictions, not
    /// incomplete authoring.
    /// </summary>
    private static void CheckCollectionWrite(
        CollectionWriteNode cwn, Graph graph, BlueprintAsset asset, ValidationContext ctx)
    {
        // BP2069 -- self-only: no "Target" pin, ever (the CollectionWrite analog of BP2062, which
        // is pinned to SetComponentNode and deliberately not widened -- see Q#20 review R2a).
        if (cwn.Pins.Any(p =>
                !p.IsExec && string.Equals(p.Name, "Target", StringComparison.OrdinalIgnoreCase)))
        {
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2069,
                $"{nameof(CollectionWriteNode)} is self-only -- a \"Target\" pin/link is not permitted.",
                asset.AssetId, graph.Id, cwn.Id));
        }

        // BP2068 -- managed collections are not element-writable (Q#20-C): a ManagedMember bake on
        // a WRITE node is structurally forbidden whether or not it is wired (Stage5 additionally
        // degrades it to Ok=false as a backstop).
        if (cwn.CollectionKind == CollectionKind.ManagedMember)
        {
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2068,
                $"{nameof(CollectionWriteNode)}: a ManagedMember collection is not element-writable " +
                "(Q#20-C -- per-field managed mutation corrupts snapshots via reference aliasing); " +
                "managed collections may only be replaced whole via SetComponent (ECB).",
                asset.AssetId, graph.Id, cwn.Id));
        }

        var collectionPin = cwn.Pins.FirstOrDefault(p =>
            !p.IsExec && p.Direction == "In"
            && string.Equals(p.Name, "Collection", StringComparison.OrdinalIgnoreCase));
        if (collectionPin is null) return;

        var collLink = graph.Links.FirstOrDefault(
            l => l.ToNodeId == cwn.Id && l.ToPinId == collectionPin.Id);
        if (collLink is null) return;

        // BP2067 -- wired but not baked (the write analog of BP2066), including a malformed FQN
        // (the write node folds BP2060/BP2061's syntactic checks in here rather than splitting).
        if (string.IsNullOrEmpty(cwn.ComponentTypeFqn)
            || !FqnPattern.IsMatch(cwn.ComponentTypeFqn)
            || string.IsNullOrEmpty(cwn.WriteAccessorFqn)
            || !FqnPattern.IsMatch(cwn.WriteAccessorFqn))
        {
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2067,
                $"{nameof(CollectionWriteNode)}: \"Collection\" is wired but the baked " +
                "ComponentTypeFqn/WriteAccessorFqn are empty or malformed -- the collection was " +
                "not baked at wire time.",
                asset.AssetId, graph.Id, cwn.Id));
        }

        // BP2070 (G4) -- producer must be a SELF read: a GetComponent with "Target" wired resolves
        // the collection off another entity, and a write consumer inheriting that binding would be
        // a cross-entity write (forbidden). The emitted write binds `self` regardless (defense in
        // depth) -- this rule makes the mismatch visible instead of silently writing elsewhere
        // than the designer wired.
        var producer = graph.Nodes.FirstOrDefault(n => n.Id == collLink.FromNodeId);
        if (producer is GetComponentNode producerGcn)
        {
            var targetPin = producerGcn.Pins.FirstOrDefault(p =>
                !p.IsExec && p.Direction == "In"
                && string.Equals(p.Name, "Target", StringComparison.OrdinalIgnoreCase));
            bool targetWired = targetPin is not null && graph.Links.Any(
                l => l.ToNodeId == producerGcn.Id && l.ToPinId == targetPin.Id);
            if (targetWired)
            {
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2070,
                    $"{nameof(CollectionWriteNode)}: the producing GetComponent has \"Target\" " +
                    "wired -- collection writes are self-only (Q#16/Q#20); the write would bind " +
                    "self, not the wired entity. Read cross-entity, write self.",
                    asset.AssetId, graph.Id, cwn.Id));
            }
        }
    }

    /// <summary>
    /// FC-1 (Q#20 G3) -- BP2071 WARNING: a <see cref="CollectionWriteNode"/> reachable from a
    /// <see cref="ComponentForEachNode"/>'s "Body" exec chain, mutating the SAME collection that
    /// ForEach is iterating, has wire-dependent semantics (the loop bound is hoisted once iff the
    /// "Count" out-pin is wired, else re-evaluated per pass -- see StatementEmitter's IrOp_ForEach
    /// case), so RemoveAt/Add inside the body silently skips or re-reads elements depending on an
    /// unrelated wire. Designer rule: a collection is read-only while being iterated. "Same
    /// collection" = same ComponentTypeFqn + same accessor OWNER CLASS (one curated ops class per
    /// (component, collection) -- comparing the class avoids needing the collection name, which
    /// consumers do not bake). The body walk follows exec successors transitively (bounded by a
    /// visited set) -- a rare rejoining wire can over-approximate, acceptable for a warning.
    /// </summary>
    private static void CheckWriteInsideIteration(Graph graph, BlueprintAsset asset, ValidationContext ctx)
    {
        static string AccessorOwner(string fqn)
        {
            int i = fqn.LastIndexOf('.');
            return i <= 0 ? "" : fqn.Substring(0, i);
        }

        foreach (var node in graph.Nodes)
        {
            if (node is not ComponentForEachNode cfe) continue;
            if (string.IsNullOrEmpty(cfe.ComponentTypeFqn)) continue;
            string iterOwner = AccessorOwner(
                !string.IsNullOrEmpty(cfe.CountAccessorFqn) ? cfe.CountAccessorFqn : cfe.ItemAccessorFqn);
            if (string.IsNullOrEmpty(iterOwner)) continue;

            var bodyPin = cfe.Pins.FirstOrDefault(p =>
                p.IsExec && p.Direction == "Out"
                && string.Equals(p.Name, "Body", StringComparison.OrdinalIgnoreCase));
            if (bodyPin is null) continue;

            // BFS over exec successors starting from the Body wire.
            var visited = new HashSet<Guid>();
            var queue = new Queue<Guid>();
            foreach (var l in graph.Links.Where(l => l.FromNodeId == cfe.Id && l.FromPinId == bodyPin.Id))
                queue.Enqueue(l.ToNodeId);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!visited.Add(id)) continue;
                var n = graph.Nodes.FirstOrDefault(x => x.Id == id);
                if (n is null) continue;

                if (n is CollectionWriteNode w
                    && string.Equals(w.ComponentTypeFqn, cfe.ComponentTypeFqn, StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(w.WriteAccessorFqn)
                    && string.Equals(AccessorOwner(w.WriteAccessorFqn), iterOwner, StringComparison.Ordinal))
                {
                    ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP2071,
                        $"{nameof(CollectionWriteNode)} mutates the collection a surrounding " +
                        "ComponentForEach is iterating -- semantics depend on whether the loop's " +
                        "\"Count\" pin is wired (hoisted vs live bound). A collection is read-only " +
                        "while being iterated; restructure to collect-then-apply after \"Completed\".",
                        asset.AssetId, graph.Id, w.Id));
                }

                foreach (var outPin in n.Pins.Where(p => p.IsExec && p.Direction == "Out"))
                    foreach (var l in graph.Links.Where(l => l.FromNodeId == n.Id && l.FromPinId == outPin.Id))
                        queue.Enqueue(l.ToNodeId);
            }
        }
    }

    /// <summary>
    /// BP2066 (CA-07b) -- see this class's doc comment. Only fires when "Collection" is WIRED
    /// (an unwired Collection is a legitimate "not used yet" state -- Stage5 degrades it silently,
    /// same as any other unconnected optional pin elsewhere in this file); the missing-accessor
    /// check is per-kind (ComponentForEach needs BOTH Count+Item, ComponentItemGet needs only Item,
    /// ComponentItemCount needs only Count -- mirrors each kind's own Stage5 lowering requirement).
    /// </summary>
    private static void CheckComponentCollectionConsumer(
        Node node, Graph graph, BlueprintAsset asset, ValidationContext ctx)
    {
        var collectionPin = node.Pins.FirstOrDefault(p =>
            !p.IsExec && p.Direction == "In"
            && string.Equals(p.Name, "Collection", StringComparison.OrdinalIgnoreCase));
        if (collectionPin is null) return;

        bool wired = graph.Links.Any(l => l.ToNodeId == node.Id && l.ToPinId == collectionPin.Id);
        if (!wired) return;

        var (componentTypeFqn, countFqn, itemFqn, kind, fieldName) = node switch
        {
            ComponentForEachNode cfe   => (cfe.ComponentTypeFqn, cfe.CountAccessorFqn, cfe.ItemAccessorFqn, cfe.CollectionKind, cfe.CollectionFieldName),
            ComponentItemGetNode cig   => (cig.ComponentTypeFqn, "", cig.ItemAccessorFqn, cig.CollectionKind, cig.CollectionFieldName),
            ComponentItemCountNode cic => (cic.ComponentTypeFqn, cic.CountAccessorFqn, "", cic.CollectionKind, cic.CollectionFieldName),
            ComponentContainsNode ccn  => (ccn.ComponentTypeFqn, ccn.CountAccessorFqn, ccn.ItemAccessorFqn, ccn.CollectionKind, ccn.CollectionFieldName),
            ComponentFindNode cfn      => (cfn.ComponentTypeFqn, cfn.CountAccessorFqn, cfn.ItemAccessorFqn, cfn.CollectionKind, cfn.CollectionFieldName),
            _                          => ("", "", "", CollectionKind.CuratedStatic, (string?)null),
        };

        // Contains/Find both loop (Count) and compare each element (Item) -> need BOTH, like ForEach.
        bool needsCount = node is ComponentForEachNode or ComponentItemCountNode
                                  or ComponentContainsNode or ComponentFindNode;
        bool needsItem  = node is ComponentForEachNode or ComponentItemGetNode
                                  or ComponentContainsNode or ComponentFindNode;

        // CA-07d-2: a MANAGED collection (Q#18-C/D) bakes CollectionFieldName for native member access,
        // NOT the curated accessor FQNs (which are legitimately empty) -- so the required-non-empty set
        // is per-KIND: managed needs the field name; curated needs its accessor FQN(s).
        // FC-2/LV-2: a BlackboardFixedList consumer (Q#19-A) bakes only the VARIABLE name in
        // CollectionFieldName -- ComponentTypeFqn and the accessor FQNs are legitimately empty
        // (there is no entity/component; Stage5 binds a ref onto the state field).
        bool missing = kind == CollectionKind.BlackboardFixedList
            ? string.IsNullOrEmpty(fieldName)
            : string.IsNullOrEmpty(componentTypeFqn)
              || (kind == CollectionKind.ManagedMember
                    ? string.IsNullOrEmpty(fieldName)
                    : ((needsCount && string.IsNullOrEmpty(countFqn))
                       || (needsItem  && string.IsNullOrEmpty(itemFqn))));

        if (missing)
        {
            string what = kind == CollectionKind.BlackboardFixedList
                ? "the node's baked list-variable name (CollectionFieldName) is empty"
                : kind == CollectionKind.ManagedMember
                ? "the node's baked managed collection field name (CollectionFieldName) is empty"
                : "the node's baked accessor FQNs are empty";
            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2066,
                $"{node.GetType().Name}: \"Collection\" is wired but {what} -- " +
                "the collection was not baked at wire time (CA-07c/d-2).",
                asset.AssetId, graph.Id, node.Id));
        }
    }

    /// <summary>
    /// BP2063 (CA-05, Slice 1b) -- Rule G1's "never persist a managed component-read value" half
    /// (the "never mutate" half is structural: <see cref="GetComponentNode"/> has no write path at
    /// all).
    /// <para>
    /// <b>What was ALREADY enforced before this rule (investigated, not duplicated):</b>
    /// <list type="bullet">
    ///   <item><b>BP1503</b> (<c>Stage4_TypeResolve.CheckUnmanagedConstraint</c>) rejects DECLARING an
    ///     <c>asset.Variables</c>/<c>asset.WorkingState</c> entry whose OWN declared type resolves to
    ///     managed -- independent of wiring. So "managed value -&gt; a Variable declared with that same
    ///     managed type" is already impossible: the Variable itself cannot exist. This does NOT cover
    ///     <c>SetSharedNode</c> at all (<see cref="V_SharedStateRules"/> only checks <c>SharedTypeId</c>
    ///     syntactically, never its managed-ness), and does not stop wiring in general -- only the
    ///     specific case of a type-matched, explicitly-declared managed Variable/WorkingState field.
    ///   </item>
    ///   <item><b>BP1501</b> (<c>Stage4_TypeResolve.VerifyLinkTypes</c>) rejects a link only when the
    ///     source/destination pin's resolved <c>IrTypeRef.FullName</c> DIFFERS (with no registered
    ///     coercion). It is a pure type-NAME-equality check -- it has no concept of managed-vs-unmanaged
    ///     at all, so a link where both ends happen to share the same type name is accepted by BP1501
    ///     regardless of whether that shared type is managed.
    ///   </item>
    /// </list>
    /// <b>The gap this closes:</b> <see cref="SetSharedNode"/> has NO managed-ness check anywhere
    /// (BP1503 never looks at it), so wiring a managed <see cref="GetComponentNode"/> field straight
    /// into a <see cref="SetSharedNode"/> field pin of the SAME type name was previously accepted by
    /// both BP1503 (out of scope) and BP1501 (name matches). This rule closes that gap directly at the
    /// LINK level, and -- for defense in depth / a clearer diagnostic message pointing at the actual
    /// managed-read node -- also flags the <see cref="SetVariableNode"/> case even though BP1503
    /// typically already blocks it earlier (via the Variable's own declared type).
    /// </para>
    /// <para>
    /// Deliberately narrow: only flags a link whose SOURCE is one of <paramref name="gcn"/>'s named
    /// FIELD out-pins (excludes "Found", a plain <c>System.Boolean</c> that is never itself a managed
    /// value) landing on <see cref="SetVariableNode"/>/<see cref="SetSharedNode"/> specifically -- a
    /// link into a <see cref="FunctionCallNode"/> data-in (library/function call parameter) is NOT
    /// touched, so a legitimate managed-&gt;managed pass-through (e.g. a library call taking the managed
    /// type) is never rejected. <see cref="SetComponentNode"/> is also NOT a checked destination here:
    /// "reject per-field managed write" is CA-06's own rule -- BP2064, above in this class's
    /// <c>Validate</c> method (a managed <c>SetComponentNode</c> write is whole-replace-only) -- not
    /// this one.
    /// </para>
    /// </summary>
    private static void CheckManagedReadNotPersisted(
        GetComponentNode gcn, Graph graph, BlueprintAsset asset, ValidationContext ctx)
    {
        var fieldPinIds = new HashSet<Guid>(
            gcn.Pins.Where(p =>
                    !p.IsExec && p.Direction == "Out"
                    && !string.Equals(p.Name, "Found", StringComparison.OrdinalIgnoreCase)
                    && gcn.Fields!.Any(f => string.Equals(f.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(p => p.Id));
        if (fieldPinIds.Count == 0) return;

        foreach (var link in graph.Links)
        {
            if (link.FromNodeId != gcn.Id || !fieldPinIds.Contains(link.FromPinId)) continue;

            var sink = graph.Nodes.FirstOrDefault(n => n.Id == link.ToNodeId);
            if (sink is not (SetVariableNode or SetSharedNode)) continue;

            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2063,
                $"{nameof(GetComponentNode)}: a managed component-read field value may only feed a " +
                $"managed consumer (e.g. a library/function call) -- wiring it into {sink!.GetType().Name} " +
                "would persist it, which Rule G1 (Q#15) forbids.",
                asset.AssetId, graph.Id, gcn.Id, link.FromPinId));
        }
    }
}

// ---------------------------------------------------------------------------
// V_ListVariableRules  (FC-2/LV-3 -- BP1505/BP1506: fixed-list variable rules)
// ---------------------------------------------------------------------------

/// <summary>
/// FC-2/LV-3 -- rules for FIXED-LIST variables (BlueprintTypeRef.Capacity &gt; 0).
///
/// BP1505 -- a <see cref="ListWriteNode"/> whose VariableId does not resolve to a declared
/// fixed-list variable (empty binding is flagged only when the node is exec-wired into a
/// chain -- an unbound palette drop is legitimate not-used-yet authoring, mirroring the
/// BP2067 wired-gating philosophy).
///
/// BP1506 -- a fixed-list variable's <see cref="GetVariableNode"/> "Value" output wired to a
/// pin that cannot accept a whole list. The blittable wrapper struct is NOT a general value:
/// only the collection consumers' "Collection" in-pin reads it (by producer resolution), and
/// the ONE whole-value exception is <see cref="SetVariableNode"/> targeting an IDENTICAL-shape
/// fixed-list variable (same element TypeId, same Capacity) -- the whole-list clone, which
/// lowers to flat struct copies. Everything else (generic math/compare pins, function args,
/// a component CollectionWriteNode, a shape-mismatched SetVariable) is rejected here rather
/// than failing obscurely at Stage4/emit.
/// </summary>
internal sealed class V_ListVariableRules : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        // FC-3 (umbrella R5, Q#21) -- BP1507: a PARAMETER may not carry a fixed-list type.
        // Parameters are the exposed-on-spawn surface; the supported list homes are instance
        // Variables, AiPrimitive WorkingState, and action DTOs. (The Shared home is fenced at
        // the WIRE level: a list value feeding SetShared/GetShared trips BP1506 -- see
        // CheckListValueWires' allowlist.)
        foreach (var p in asset.Parameters)
        {
            if (p.Type is { Capacity: > 0 })
            {
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1507,
                    $"Parameter '{p.Name}' declares a fixed-list type (Capacity={p.Type.Capacity}) -- " +
                    "lists are not supported on Parameters (or Shared slots) in v1; declare the list " +
                    "as an instance Variable, an AiPrimitive WorkingState field, or an action-DTO field.",
                    asset.AssetId));
            }
        }

        foreach (var graph in asset.Graphs)
        {
            foreach (var node in graph.Nodes)
            {
                switch (node)
                {
                    case ListWriteNode lwn:
                        CheckListWriteTarget(lwn, graph, asset, ctx);
                        break;
                    case GetVariableNode gv when ResolveListDecl(asset, gv.VariableId) is { } listDecl:
                        CheckListValueWires(gv, listDecl, graph, asset, ctx);
                        break;
                }
            }
        }
    }

    /// <summary>Resolves a variableId ("var:"-prefix tolerated) to a FIXED-LIST decl; else null.</summary>
    private static VariableDecl? ResolveListDecl(BlueprintAsset asset, string? variableId)
    {
        var decl = ResolveAnyDecl(asset, variableId);
        return decl is { Type.Capacity: > 0 } ? decl : null;
    }

    private static VariableDecl? ResolveAnyDecl(BlueprintAsset asset, string? variableId)
    {
        var vid = variableId ?? "";
        if (vid.StartsWith("var:", StringComparison.Ordinal)) vid = vid.Substring(4);
        if (!Guid.TryParse(vid, out var id)) return null;
        return asset.Variables.FirstOrDefault(v => v.Id == id)
            ?? asset.WorkingState.FirstOrDefault(v => v.Id == id);
    }

    private static void CheckListWriteTarget(
        ListWriteNode lwn, Graph graph, BlueprintAsset asset, ValidationContext ctx)
    {
        if (ResolveListDecl(asset, lwn.VariableId) is not null) return;

        bool bound = !string.IsNullOrEmpty(lwn.VariableId);
        if (!bound)
        {
            // Unbound AND out of any exec chain -- a fresh palette drop; stay silent
            // (Stage5 degrades it to Ok=false).
            var execIn = lwn.Pins.FirstOrDefault(p => p.IsExec && p.Direction == "In");
            bool inFlow = execIn is not null && graph.Links.Any(
                l => l.ToNodeId == lwn.Id && l.ToPinId == execIn.Id);
            if (!inFlow) return;
        }

        var scalar = ResolveAnyDecl(asset, lwn.VariableId);
        string detail = scalar is not null
            ? $"variable '{scalar.Name}' is not a fixed-list (Capacity == 0)"
            : bound ? $"VariableId '{lwn.VariableId}' does not resolve to a declared variable"
                    : "VariableId is empty but the node is wired into an exec chain";
        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1505,
            $"{nameof(ListWriteNode)}: write target must be a declared fixed-list variable -- {detail}.",
            asset.AssetId, graph.Id, lwn.Id));
    }

    private static void CheckListValueWires(
        GetVariableNode gv, VariableDecl listDecl, Graph graph, BlueprintAsset asset, ValidationContext ctx)
    {
        var outPins = new HashSet<Guid>(
            gv.Pins.Where(p => !p.IsExec && p.Direction == "Out").Select(p => p.Id));

        foreach (var link in graph.Links)
        {
            if (link.FromNodeId != gv.Id || !outPins.Contains(link.FromPinId)) continue;

            var sink = graph.Nodes.FirstOrDefault(n => n.Id == link.ToNodeId);
            if (sink is null) continue;
            var toPin = sink.Pins.FirstOrDefault(p => p.Id == link.ToPinId);
            if (toPin is null || toPin.IsExec) continue;

            // The blessed consumers: the 5 collection readers' "Collection" in-pin.
            bool isConsumerCollectionPin =
                sink is ComponentForEachNode or ComponentItemGetNode or ComponentItemCountNode
                        or ComponentContainsNode or ComponentFindNode
                && string.Equals(toPin.Name, "Collection", StringComparison.OrdinalIgnoreCase);
            if (isConsumerCollectionPin) continue;

            // The one whole-value exception: SetVariable onto an IDENTICAL-shape list (clone).
            if (sink is SetVariableNode svn)
            {
                var target = ResolveAnyDecl(asset, svn.VariableId);
                if (target is not null
                    && target.Type.Capacity == listDecl.Type.Capacity
                    && string.Equals(target.Type.TypeId, listDecl.Type.TypeId, StringComparison.Ordinal))
                {
                    continue;   // whole-list clone -- lowers to flat struct copies
                }
                string shape = target is null
                    ? "an unresolved variable"
                    : target.Type.Capacity <= 0
                        ? $"non-list variable '{target.Name}'"
                        : $"list '{target.Name}' of different shape " +
                          $"({target.Type.TypeId}[{target.Type.Capacity}] vs {listDecl.Type.TypeId}[{listDecl.Type.Capacity}])";
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1506,
                    $"fixed-list variable '{listDecl.Name}' may only be SetVariable-cloned onto an " +
                    $"identical-shape fixed-list (same element type, same capacity) -- target is {shape}.",
                    asset.AssetId, graph.Id, gv.Id, link.FromPinId));
                continue;
            }

            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1506,
                $"fixed-list variable '{listDecl.Name}' wired to {sink.GetType().Name}.\"{toPin.Name}\" -- " +
                "a fixed-list may only feed a collection consumer's \"Collection\" pin or an " +
                "identical-shape SetVariable whole-list clone; use the collection nodes " +
                "(ItemGet/Count/Contains/Find/ForEach/ListWrite) to work with elements.",
                asset.AssetId, graph.Id, gv.Id, link.FromPinId));
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
// V_FunctionGraphReturnValue  (BP-71 / Q24-C3 + Q24-D)
// ---------------------------------------------------------------------------

/// <summary>
/// BP1655 — a Function graph declares an output, but a <see cref="ReturnNode"/> in it has nothing
/// wired into its value pin.
/// BP1656 — a Function graph declares MORE THAN ONE output, which is not supported yet (BP-73).
///
/// <para>
/// <b>Why BP1655 exists (BP-71).</b> Before this, an unwired return produced a BP4001 *warning*
/// plus a dummy <c>IrValue</c> that was never declared, so the emitter wrote <c>return __t7;</c>
/// with no <c>var __t7</c> — <b>CS0103 from Roslyn with no BP diagnostic to explain it</b> (the same
/// unattributable shape as BP-69). Stage 5 now also falls back to a typed <c>default(T)</c> so the
/// generated C# always compiles; this validator is what tells the *designer* rather than letting a
/// silently-defaulted return through (the BP-16 lesson: never a silent wrong value).
/// </para>
/// <para>
/// <b>Pin-ful graphs only</b>, like every other structural check here. A JSON-loaded asset carries
/// <c>"Pins": []</c> and is rehydrated in Stage 0, so its Return node has no value pin to inspect at
/// Stage 2 — and a graph with no links at all is an unauthored stub, not a designer error
/// (<c>V_GraphStructure</c> makes the same exemption). Stage 5's <c>default(T)</c> covers
/// correctness on that path.
/// </para>
/// <para>
/// Accepts a value pin in <b>either</b> direction: the projections now emit <c>"In"</c> (Q24-A1),
/// but hand-authored JSON may carry the legacy <c>"Out"</c> form, which Stage 5 still honours
/// (Q24-B1). Flagging the legacy form as "no pin" would turn a working asset into an error.
/// </para>
/// </summary>
internal sealed class V_FunctionGraphReturnValue : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var graph in asset.Graphs)
        {
            // BP-80 / F3: Macro is admitted DELIBERATELY, not by omission. A macro reuses ReturnNode
            // as its output boundary, so "declares an output but nothing is wired into it" is the
            // identical defect with the identical consequence -- and Macro_Implementation_Design §3's
            // splice rule 4 names this rule as the one that already covers an unwired Out′.dataIn[q].
            // Every Entry/Return rule must decide about Macro explicitly (F3's stated cost); this is
            // that decision for BP1655.
            if (graph.Kind != GraphKind.Function && graph.Kind != GraphKind.Macro) continue;
            if (graph.Outputs.Count == 0) continue;

            // ----- BP1656 RETIRED by BP-73 -----
            // This used to be an error for Outputs.Count > 1 whose wording said "not supported yet
            // -- see BP-73". BP-73 shipped: N outputs now compile to a ValueTuple carrier that the
            // call site fans back out. The code is kept in DiagnosticCodes as a retired entry so the
            // number is never reused, and there is deliberately no replacement diagnostic -- a
            // multi-output function graph is now ordinary, valid authoring.

            // ----- BP1655: an authored Return node must have its value wired -----
            // Unauthored stub (no links at all, pins not yet rehydrated) — nothing to judge.
            if (graph.Links.Count == 0) continue;

            foreach (var rn in graph.Nodes.OfType<ReturnNode>())
            {
                if (rn.Pins.Count == 0) continue; // pin-less: Stage 0 has not projected yet

                var valuePin = rn.Pins.FirstOrDefault(
                    p => !p.IsExec && (p.Direction == "In" || p.Direction == "Out"));
                if (valuePin is null) continue; // no value slot projected — not this rule's business

                bool wired = graph.Links.Any(
                    l => l.ToNodeId == rn.Id && l.ToPinId == valuePin.Id);
                if (wired) continue;

                bool isMacro = graph.Kind == GraphKind.Macro;
                var kindWord   = isMacro ? "Macro" : "Function";
                var yieldsWord = isMacro ? "every call site of this macro yields" : "the function returns";
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1655,
                    $"{kindWord} graph '{graph.Name}' declares output '{graph.Outputs[0].Name}' " +
                    $"({graph.Outputs[0].Type?.TypeId}), but the Return node (id={rn.Id}) has " +
                    $"nothing wired into its '{valuePin.Name}' pin. Wire a value into it, or set " +
                    $"the pin's inline default. Without this {yieldsWord} " +
                    $"default({graph.Outputs[0].Type?.TypeId}).",
                    asset.AssetId, graph.Id, rn.Id));
            }
        }
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
/// P1 (GAP-1): a <see cref="FlowForEachNode"/>'s -- and (CA-07b) a <see cref="ComponentForEachNode"/>'s
/// -- "Body" exec-subgraph must be a synchronous, latent-free sub-DAG. Both lower to an inline C#
/// <c>for</c> whose statements are scheduled inline (not BFS blocks), so a latent node -- which needs
/// a suspend/resume block split -- cannot appear inside it. P1b lifted the P1a branch-free
/// restriction: a <see cref="BranchNode"/> in the body now lowers to a nested inline <c>if</c>/
/// <c>else</c> (IrOp_If), so branches ARE allowed; only latent nodes remain forbidden.
/// </summary>
internal sealed class V_FlowForEachRules : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var graph in asset.Graphs)
        {
            var nodeById = graph.Nodes.ToDictionary(n => n.Id);
            // CA-07b: ComponentForEachNode shares FlowForEachNode's inline for-body scheduling
            // (ScheduleComponentForEachNode -> ScheduleInlineBodyChain), so its "Body" carries the
            // SAME latent-free requirement -- a latent node there would need a suspend/resume block
            // split the inline for-body cannot span. Both loop kinds expose a "Body" exec-out.
            foreach (var loop in graph.Nodes.Where(n => n is FlowForEachNode or ComponentForEachNode))
            {
                var bodyPin = loop.Pins.FirstOrDefault(p => p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Body", StringComparison.OrdinalIgnoreCase));
                if (bodyPin is null) continue;

                var loopKind = loop is ComponentForEachNode ? "ComponentForEach" : "FlowForEach";

                var visited = new HashSet<Guid>();
                var queue = new Queue<Node>();
                foreach (var start in ExecTargets(graph, loop.Id, bodyPin.Id, nodeById))
                    queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    var n = queue.Dequeue();
                    if (!visited.Add(n.Id)) continue;

                    // P1b: BranchNode is now allowed in the body (lowers to a nested inline if/else).
                    // Latent nodes remain forbidden -- they need a suspend/resume block split the
                    // inline for-body cannot span.
                    if (n is LatentDelayNode or WaitForChannelNode or WaitForEventNode or WhenNode)
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2050,
                            $"{loopKind} body must be latent-free: a latent '{n.GetType().Name}' is reachable from the loop 'Body'.",
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

// ---------------------------------------------------------------------------
// V_FormatStringRules (BP-108 -- BP2072)
// ---------------------------------------------------------------------------

/// <summary>
/// BP-108 -- BP2072: a <see cref="PrintStringNode"/> or <see cref="FormatStringNode"/> whose
/// <c>Format</c> fails <see cref="Hrot.Blueprints.Core.Compiler.Format.BlueprintFormatString.Parse"/>
/// (unclosed <c>'{'</c>, empty <c>'{}'</c>, invalid placeholder name, or a stray <c>'}'</c>).
/// <para>
/// ⚠ <b>Error, not Warning.</b> A malformed format yields NO derived arg pins --
/// <see cref="Hrot.Blueprints.Core.Compiler.Catalogs.BuiltInNodeRegistry"/>'s <c>AppendArgPins</c>
/// bails out on <c>!parsed.IsValid</c> -- so the node still "compiles" (exec pins only, or a lone
/// "Result" pin for Format String) and silently prints/formats the wrong thing at runtime. That is
/// exactly trap #5's shape: a wrong value is worse than a build failure, so this is an Error.
/// </para>
/// <para>
/// One parser, three consumers (this validator, the registry's pin derivation, and the emitter's
/// interpolated-body rewrite) -- see <c>BlueprintFormatString</c>'s own doc comment. This validator
/// never re-implements the grammar; it only reports what <c>Parse</c> already decided.
/// </para>
/// </summary>
internal sealed class V_FormatStringRules : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        foreach (var graph in asset.Graphs)
        {
            foreach (var node in graph.Nodes)
            {
                string? format = node switch
                {
                    PrintStringNode ps  => ps.Format,
                    FormatStringNode fs => fs.Format,
                    _                   => null,
                };
                if (format is null) continue;

                var parsed = Hrot.Blueprints.Core.Compiler.Format.BlueprintFormatString.Parse(format);
                if (!parsed.IsValid)
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP2072,
                        $"{node.GetType().Name}: malformed Format -- {parsed.Error}",
                        asset.AssetId, graph.Id, node.Id));
                }
            }
        }
    }
}
