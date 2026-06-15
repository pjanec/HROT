using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fbt;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.BTree.Editor.Persistence;

/// <summary>
/// Maps between the editor model (BehaviorTreeAsset) and the persisted DTO (BehaviorTreeAssetDto).
/// Design §3 D3: editor⇄DTO mapping both ways.
/// Design §5.2: persist topology+layout+sync+suppressions+blackboard; exclude runtime-only fields.
/// </summary>
public static class BehaviorTreeAssetMapper
{
    // ── Model → DTO ───────────────────────────────────────────────────────────

    /// <summary>Maps a BehaviorTreeAsset (editor model) to a BehaviorTreeAssetDto (persisted form).</summary>
    public static BehaviorTreeAssetDto ToDto(BehaviorTreeAsset asset)
    {
        var dto = new BehaviorTreeAssetDto
        {
            AssetId            = asset.AssetId,
            Name               = asset.Name,
            TargetNamespace    = asset.TargetNamespace,
            BlackboardTypeName = asset.BlackboardTypeName,
            ContextTypeName    = asset.ContextTypeName,
            Canvas = new CanvasDto
            {
                PanX = asset.CanvasPanOffset.X,
                PanY = asset.CanvasPanOffset.Y,
                Zoom = asset.CanvasZoomLevel,
            },
        };

        // Topology — nodes
        foreach (var node in asset.Nodes)
            dto.Nodes.Add(NodeToDto(node));

        // Topology — pills
        foreach (var pill in asset.Pills)
            dto.Pills.Add(PillToDto(pill));

        // Subtree sync bindings (§5.2)
        foreach (var kv in asset.GetAllSyncBindings())
        {
            var key = kv.Key.ToString();
            var bindings = new List<SubtreeSyncBindingDto>();
            foreach (var b in kv.Value)
            {
                bindings.Add(new SubtreeSyncBindingDto
                {
                    FieldName          = b.FieldName,
                    MasterVariableName = b.MasterVariableName,
                    SyncIn             = b.SyncIn,
                    SyncOut            = b.SyncOut,
                });
            }
            dto.SubtreeSyncBindings[key] = bindings;
        }

        // Suppressions (§5.2)
        foreach (var (varName, writerKey) in asset.GetConflictSuppressions())
        {
            dto.Suppressions.Conflict.Add(new ConflictSuppressionDto
            {
                VariableName = varName,
                WriterPairKey = writerKey,
            });
        }
        foreach (var varName in asset.GetUnusedSuppressions())
            dto.Suppressions.Unused.Add(varName);

        // Blackboard block (§5.4)
        dto.Blackboard = BlackboardToDto(asset);

        return dto;
    }

    // ── DTO → Model ───────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a BehaviorTreeAssetDto (persisted form) to a BehaviorTreeAsset (editor model).
    /// The returned asset has IsDirty=false, IsEditorOwned=true, and an empty Blob
    /// (blob is filled in when the assembly is reflected or a generator runs).
    /// </summary>
    public static BehaviorTreeAsset FromDto(BehaviorTreeAssetDto dto)
        => ToModel(dto, string.Empty, true);

    /// <summary>
    /// Maps a BehaviorTreeAssetDto to a BehaviorTreeAsset, setting SourceFilePath and
    /// IsEditorOwned explicitly.
    /// Design §3 D4 / PU-301: used by the JSON file-based contributor so the loaded
    /// model carries the correct SourceFilePath and ownership flag.
    /// </summary>
    public static BehaviorTreeAsset ToModel(
        BehaviorTreeAssetDto dto,
        string sourceFilePath,
        bool isEditorOwned)
    {
        // Blob is not persisted (runtime-only). Provide an empty placeholder.
        var emptyBlob = new BehaviorTreeBlob
        {
            TreeName        = dto.Name,
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

        var asset = new BehaviorTreeAsset(
            dto.AssetId,
            dto.Name,
            sourceFilePath:       sourceFilePath,
            isEditorOwned:        isEditorOwned,
            dto.BlackboardTypeName,
            dto.ContextTypeName,
            emptyBlob,
            dto.TargetNamespace);

        asset.CanvasPanOffset = new Vector2(dto.Canvas.PanX, dto.Canvas.PanY);
        asset.CanvasZoomLevel = dto.Canvas.Zoom;

        // Build nodes and pills
        var nodes = new List<BTreeEditorNode>();
        var pills = new List<BTreeEditorPill>();

        foreach (var nodeDto in dto.Nodes)
            nodes.Add(NodeFromDto(nodeDto));

        foreach (var pillDto in dto.Pills)
            pills.Add(PillFromDto(pillDto));

        // ReplaceAll wires up the lookup tables; use empty blob (no KernelBlobIndex valid)
        asset.ReplaceAll(nodes, pills, emptyBlob);

        // Subtree sync bindings
        var syncBindings = new Dictionary<Guid, IReadOnlyList<SubtreeSyncBinding>>();
        foreach (var kv in dto.SubtreeSyncBindings)
        {
            if (!Guid.TryParse(kv.Key, out var nodeId)) continue;
            var list = new List<SubtreeSyncBinding>();
            foreach (var b in kv.Value)
            {
                list.Add(new SubtreeSyncBinding(
                    b.FieldName,
                    b.MasterVariableName,
                    b.SyncIn,
                    b.SyncOut));
            }
            syncBindings[nodeId] = list.AsReadOnly();
        }
        asset.LoadSyncBindings(syncBindings);

        // Suppressions
        foreach (var s in dto.Suppressions.Conflict)
            asset.SetConflictSuppressed(s.VariableName, s.WriterPairKey, true);
        foreach (var varName in dto.Suppressions.Unused)
            asset.SetUnusedWarningSuppressed(varName, true);

        // Blackboard variables
        // Restore the editor-managed flag from the persisted block. Without this the
        // round-trip is lossy: BlackboardToDto writes Managed out, but loading never
        // set it back, so a Managed asset always opened as "not managed".
        asset.IsBlackboardEditorManaged = dto.Blackboard.Managed;
        var vars = BlackboardFromDto(dto.Blackboard);
        if (vars.Count > 0)
            asset.SetBlackboardVariables(vars);

        // Clear dirty flag set by the mutations above
        asset.ClearDirty();

        return asset;
    }

    // ── Node mapping ──────────────────────────────────────────────────────────

    private static BTreeNodeDto NodeToDto(BTreeEditorNode node)
    {
        BTreeNodeDto dto = node.KernelType switch
        {
            NodeType.Root             => new BTreeRootNodeDto(),
            NodeType.Sequence         => new BTreeSequenceNodeDto(),
            NodeType.Selector         => new BTreeSelectorNodeDto(),
            NodeType.Parallel         => new BTreeParallelNodeDto(),
            NodeType.Inverter         => new BTreeInverterNodeDto(),
            NodeType.ForceSuccess     => new BTreeForceSuccessNodeDto(),
            NodeType.ForceFailure     => new BTreeForceFailureNodeDto(),
            NodeType.UntilSuccess     => new BTreeUntilSuccessNodeDto(),
            NodeType.UntilFailure     => new BTreeUntilFailureNodeDto(),
            NodeType.ObserverSelector => new BTreeObserverSelectorNodeDto(),
            NodeType.Service          => new BTreeServiceNodeDto(),
            NodeType.Observer         => new BTreeObserverNodeDto(),
            NodeType.Repeater         => new BTreeRepeaterNodeDto(),
            NodeType.Cooldown         => new BTreeCooldownNodeDto(),
            NodeType.Action           => new BTreeActionNodeDto(),
            NodeType.Condition        => new BTreeConditionNodeDto(),
            NodeType.Wait             => new BTreeWaitNodeDto(),
            NodeType.Subtree          => new BTreeSubtreeNodeDto(),
            _                         => new BTreeSequenceNodeDto(), // safe fallback
        };

        dto.VisualId      = node.VisualId;
        dto.DisplayLabel  = node.DisplayLabel;
        dto.ChildVisualIds.AddRange(node.ChildVisualIds);
        dto.EditorMetadata = new NodeEditorMetadataDto
        {
            X         = node.Position.X,
            Y         = node.Position.Y,
            Comment   = node.Comment,
            Collapsed = false,   // BTreeEditorNode has no Collapsed field — always false
            Color     = null,    // BTreeEditorNode has no Color field
            Waypoints = node.Waypoints.Count > 0
                ? node.Waypoints.Select(wp => new BTreeWaypointDto { X = wp.X, Y = wp.Y }).ToList()
                : null,
        };

        // Payloads
        if (dto is BTreeRepeaterNodeDto rep && node.KernelType == NodeType.Repeater)
        {
            // IntParam on pills is used for Repeater count in pill model;
            // for a standalone Repeater node there's no IntParam in BTreeEditorNode itself.
            // Leave null (no pill param on the node level).
        }
        if (dto is BTreeCooldownNodeDto cool && node.KernelType == NodeType.Cooldown)
        {
            // Similarly no FloatParam at node level (pill carries it).
        }
        if (dto is BTreeActionNodeDto actDto && node.Action != null)
        {
            actDto.Action = new BTreeActionPayloadDto
            {
                MethodFqn           = node.Action.MethodFqn,
                ExpressionTargetField = node.Action.ExpressionTargetField,
                DelegateShape       = (BTreeDelegateShapeDto)node.Action.DelegateShape,
            };
        }
        if (dto is BTreeConditionNodeDto condDto && node.Condition != null)
        {
            condDto.Condition = new BTreeConditionPayloadDto
            {
                MethodFqn           = node.Condition.MethodFqn,
                ExpressionTargetField = node.Condition.ExpressionTargetField,
                DelegateShape       = (BTreeDelegateShapeDto)node.Condition.DelegateShape,
            };
        }
        if (dto is BTreeWaitNodeDto waitDto && node.Wait != null)
        {
            waitDto.Wait = new BTreeWaitPayloadDto { Duration = node.Wait.Duration };
        }
        if (dto is BTreeSubtreeNodeDto subtreeDto && node.Subtree != null)
        {
            subtreeDto.Subtree = new BTreeSubtreePayloadDto
            {
                SubtreeAssetId = node.Subtree.SubtreeAssetId,
                SubtreeName    = node.Subtree.SubtreeName,
                IsResolved     = node.Subtree.IsResolved,
            };
        }

        return dto;
    }

    private static BTreeEditorNode NodeFromDto(BTreeNodeDto dto)
    {
        var node = new BTreeEditorNode
        {
            VisualId     = dto.VisualId,
            KernelType   = DtoKindToNodeType(dto),
            DisplayLabel = dto.DisplayLabel,
            Position     = new Vector2(dto.EditorMetadata.X, dto.EditorMetadata.Y),
            Comment      = dto.EditorMetadata.Comment,
            // IsBreakpoint excluded (runtime/session-only)
            // KernelBlobIndex excluded (runtime-only, rehydrated after reload)
            KernelBlobIndex = -1,
        };
        node.ChildVisualIds.AddRange(dto.ChildVisualIds);
        if (dto.EditorMetadata.Waypoints != null)
        {
            foreach (var wp in dto.EditorMetadata.Waypoints)
                node.Waypoints.Add(new Vector2(wp.X, wp.Y));
        }

        if (dto is BTreeActionNodeDto actDto && actDto.Action != null)
        {
            node.Action = new BTreeActionPayload
            {
                MethodFqn           = actDto.Action.MethodFqn,
                ExpressionTargetField = actDto.Action.ExpressionTargetField,
                DelegateShape       = (BTreeActionDelegateShape)actDto.Action.DelegateShape,
            };
        }
        if (dto is BTreeConditionNodeDto condDto && condDto.Condition != null)
        {
            node.Condition = new BTreeConditionPayload
            {
                MethodFqn           = condDto.Condition.MethodFqn,
                ExpressionTargetField = condDto.Condition.ExpressionTargetField,
                DelegateShape       = (BTreeActionDelegateShape)condDto.Condition.DelegateShape,
            };
        }
        if (dto is BTreeWaitNodeDto waitDto && waitDto.Wait != null)
        {
            node.Wait = new BTreeWaitPayload { Duration = waitDto.Wait.Duration };
        }
        if (dto is BTreeSubtreeNodeDto subtreeDto && subtreeDto.Subtree != null)
        {
            node.Subtree = new BTreeSubtreePayload
            {
                SubtreeAssetId = subtreeDto.Subtree.SubtreeAssetId,
                SubtreeName    = subtreeDto.Subtree.SubtreeName,
                IsResolved     = subtreeDto.Subtree.IsResolved,
            };
        }

        return node;
    }

    private static NodeType DtoKindToNodeType(BTreeNodeDto dto) => dto switch
    {
        BTreeRootNodeDto             => NodeType.Root,
        BTreeSequenceNodeDto         => NodeType.Sequence,
        BTreeSelectorNodeDto         => NodeType.Selector,
        BTreeParallelNodeDto         => NodeType.Parallel,
        BTreeInverterNodeDto         => NodeType.Inverter,
        BTreeForceSuccessNodeDto     => NodeType.ForceSuccess,
        BTreeForceFailureNodeDto     => NodeType.ForceFailure,
        BTreeUntilSuccessNodeDto     => NodeType.UntilSuccess,
        BTreeUntilFailureNodeDto     => NodeType.UntilFailure,
        BTreeObserverSelectorNodeDto => NodeType.ObserverSelector,
        BTreeServiceNodeDto          => NodeType.Service,
        BTreeObserverNodeDto         => NodeType.Observer,
        BTreeRepeaterNodeDto         => NodeType.Repeater,
        BTreeCooldownNodeDto         => NodeType.Cooldown,
        BTreeActionNodeDto           => NodeType.Action,
        BTreeConditionNodeDto        => NodeType.Condition,
        BTreeWaitNodeDto             => NodeType.Wait,
        BTreeSubtreeNodeDto          => NodeType.Subtree,
        _                            => NodeType.Sequence,
    };

    // ── Pill mapping ──────────────────────────────────────────────────────────

    private static BTreePillDto PillToDto(BTreeEditorPill pill) => new()
    {
        VisualId         = pill.VisualId,
        HostNodeVisualId = pill.HostNodeVisualId,
        DecoratorType    = pill.DecoratorType.ToString(),
        IntParam         = pill.IntParam,
        FloatParam       = pill.FloatParam,
        Comment          = pill.Comment,
        StackIndex       = pill.StackIndex,
    };

    private static BTreeEditorPill PillFromDto(BTreePillDto dto)
    {
        Enum.TryParse<NodeType>(dto.DecoratorType, out var decoratorType);
        return new BTreeEditorPill
        {
            VisualId         = dto.VisualId,
            HostNodeVisualId = dto.HostNodeVisualId,
            DecoratorType    = decoratorType,
            IntParam         = dto.IntParam,
            FloatParam       = dto.FloatParam,
            Comment          = dto.Comment,
            StackIndex       = dto.StackIndex,
        };
    }

    // ── Blackboard mapping (§5.4) ─────────────────────────────────────────────

    private static BlackboardBlockDto BlackboardToDto(BehaviorTreeAsset asset)
    {
        var block = new BlackboardBlockDto
        {
            Managed     = asset.IsBlackboardEditorManaged,
            TypeName    = asset.BlackboardTypeName,
            HeavyDtoType = null,
        };

        foreach (var v in asset.BlackboardVariables)
        {
            var typeId = v.FieldType.FullName ?? v.FieldType.Name;
            block.Variables.Add(new BlackboardVariableDto
            {
                Name = v.Name,
                Type = new BlackboardTypeRefDto
                {
                    TypeId      = typeId,
                    IsArray     = false,
                    FixedLength = null,
                },
                DefaultValueJson = v.DefaultValueJson,
                Comment          = v.Comment,
                IsAutoManaged    = v.IsAutoManaged,
            });
        }

        return block;
    }

    private static List<BlackboardVariableEntry> BlackboardFromDto(BlackboardBlockDto block)
    {
        var result = new List<BlackboardVariableEntry>();
        foreach (var v in block.Variables)
        {
            // Resolve CLR type from TypeId string. Fall back to object if unknown.
            var clrType = ResolveClrType(v.Type.TypeId);
            result.Add(new BlackboardVariableEntry(
                v.Name, clrType, v.Comment,
                IsAutoManaged:    v.IsAutoManaged,
                DefaultValueJson: v.DefaultValueJson));
        }
        return result;
    }

    private static Type ResolveClrType(string typeId)
    {
        // Try the short-name map first (for primitives)
        var primitive = Hrot.Editor.AiShared.Blackboard.BlackboardTypeHelper.GetPrimitiveType(typeId);
        if (primitive != null) return primitive;

        // Try full name via Type.GetType (works for System types)
        var t = Type.GetType(typeId);
        if (t != null) return t;

        // DTO struct types (e.g. action param DTOs like DemoCounterNodes+DemoCounterParams)
        // live in behavior assemblies (Hrot.AI.Behaviors, etc.), NOT the editor assembly, so
        // Type.GetType — which only probes this assembly + corelib — misses them. Search all
        // loaded assemblies by full name. TypeId uses the `+` nested separator (from Type.FullName),
        // which Assembly.GetType understands directly.
        var resolved = ResolveFromLoadedAssemblies(typeId);
        if (resolved != null) return resolved;

        // Alias shorthand (e.g. "int" or "float") already handled by GetPrimitiveType;
        // try the display name path as well
        foreach (var name in Hrot.Editor.AiShared.Blackboard.BlackboardTypeHelper.DefaultKnownTypeNames)
        {
            var pt = Hrot.Editor.AiShared.Blackboard.BlackboardTypeHelper.GetPrimitiveType(name);
            if (pt != null && (pt.FullName == typeId || pt.Name == typeId)) return pt;
        }

        return typeof(object);
    }

    /// <summary>
    /// Searches all loaded assemblies for a value type whose full name equals <paramref name="typeId"/>.
    /// Used to resolve action/condition DTO struct types referenced by managed-blackboard variables,
    /// which live in behavior assemblies rather than the editor assembly. Returns null if not found.
    /// </summary>
    private static Type? ResolveFromLoadedAssemblies(string typeId)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? byName;
            try { byName = asm.GetType(typeId, throwOnError: false, ignoreCase: false); }
            catch { byName = null; } // dynamic/reflection-only assemblies may throw
            if (byName != null) return byName;
        }
        return null;
    }
}
