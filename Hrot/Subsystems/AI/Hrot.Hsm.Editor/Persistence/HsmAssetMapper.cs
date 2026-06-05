using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fhsm.Kernel.Data;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Persistence;

/// <summary>
/// Maps between the editor model (HsmAsset) and the persisted DTO (HsmAssetDto).
/// Design §3 D3 / §5.2/§5.4: editor⇄DTO mapping both ways.
/// Runtime-only fields excluded: Blob/Metadata, FlatIndex, *PinId,
/// LoadDiagnosticMessage, IsDirty, Changed, IsBreakpoint.
/// </summary>
public static class HsmAssetMapper
{
    // ── Model → DTO ───────────────────────────────────────────────────────────

    /// <summary>Maps an HsmAsset (editor model) to an HsmAssetDto (persisted form).</summary>
    public static HsmAssetDto ToDto(HsmAsset asset)
    {
        var dto = new HsmAssetDto
        {
            AssetId            = asset.AssetId,
            Name               = asset.Name,
            TargetNamespace    = asset.TargetNamespace,
            BlackboardTypeName = asset.BlackboardTypeName,
            Canvas = new HsmCanvasDto
            {
                PanX = asset.CanvasPanOffset.X,
                PanY = asset.CanvasPanOffset.Y,
                Zoom = asset.CanvasZoomLevel,
            },
        };

        // Build event-id-to-name lookup for DeferredEventNames mapping
        var eventIdToName = new Dictionary<ushort, string>();
        foreach (var ev in asset.AllEvents)
            eventIdToName[ev.EventId] = ev.Name;

        // States (excluding synthetic RootState)
        foreach (var s in asset.AllStates)
        {
            var stateDto = new StateNodeDto
            {
                StableId       = s.StableId,
                Name           = s.Name,
                IsInitial      = s.IsInitial,
                IsHistory      = s.IsHistory,
                IsDeepHistory  = s.IsDeepHistory,
                IsParallel     = s.IsParallel,
                IsFinal        = s.IsFinal,
                OnEntryAction  = s.OnEntryAction,
                OnExitAction   = s.OnExitAction,
                ActivityAction = s.ActivityAction,
                TimerAction    = s.TimerAction,
                RegionIndex    = s.RegionIndex,
                X              = s.Position.X,
                Y              = s.Position.Y,
                Comment        = s.Comment,
                IsCollapsed    = s.IsCollapsed,
                ColorOverride  = s.ColorOverride,
                ParentStableId = s.Parent?.Parent != null ? s.Parent?.StableId : null,
            };

            if (s.SizeOverride.HasValue)
            {
                stateDto.SizeOverrideX = s.SizeOverride.Value.X;
                stateDto.SizeOverrideY = s.SizeOverride.Value.Y;
            }

            // Deferred event names (preserve ordering from DeferredEventIds for round-trip stability)
            foreach (var deferredId in s.DeferredEventIds)
            {
                if (eventIdToName.TryGetValue(deferredId, out var evName))
                    stateDto.DeferredEventNames.Add(evName);
            }

            foreach (var child in s.Children)
                stateDto.ChildStableIds.Add(child.StableId);

            dto.States.Add(stateDto);
        }

        // Regions
        foreach (var r in asset.AllRegions)
        {
            dto.Regions.Add(new RegionNodeDto
            {
                StableId            = r.StableId,
                RegionIndex         = r.RegionIndex,
                Name                = r.Name,
                Priority            = r.Priority,
                InitialChildStableId = r.InitialChild?.StableId,
                Comment             = r.Comment,
                ColorOverride       = r.ColorOverride,
            });
        }

        // Transitions
        foreach (var t in asset.AllTransitions)
        {
            var tDto = new TransitionNodeDto
            {
                VisualId        = t.VisualId,
                SourceStableId  = t.Source.StableId,
                TargetStableId  = t.Target.StableId,
                EventName       = t.EventName,
                GuardFunction   = t.GuardFunction,
                ActionFunction  = t.ActionFunction,
                Priority        = t.Priority,
                Kind            = (TransitionKindDto)t.Kind,
                SyncGroupId     = t.SyncGroupId,
                Comment         = t.Comment,
            };
            foreach (var wp in t.Waypoints)
                tDto.Waypoints.Add(new WaypointDto { X = wp.X, Y = wp.Y });
            dto.Transitions.Add(tDto);
        }

        // Global transitions
        foreach (var g in asset.AllGlobalTransitions)
        {
            dto.GlobalTransitions.Add(new GlobalTransitionNodeDto
            {
                VisualId       = g.VisualId,
                TargetStableId = g.Target.StableId,
                EventName      = g.EventName,
                GuardFunction  = g.GuardFunction,
                ActionFunction = g.ActionFunction,
                Priority       = g.Priority,
                Comment        = g.Comment,
            });
        }

        // Events (EventId included for emit-core byte-identity; re-assigned sequentially
        // by the Phase-2 generator once JSON becomes the source of truth).
        foreach (var ev in asset.AllEvents)
        {
            dto.Events.Add(new EventDefinitionDto
            {
                Name         = ev.Name,
                PayloadSize  = ev.PayloadSize,
                IsIndirect   = ev.IsIndirect,
                IsDeferrable = ev.IsDeferrable,
                EventId      = ev.EventId,
            });
        }

        // Suppressions (§5.2)
        foreach (var (varName, writerKey) in asset.GetConflictSuppressions())
            dto.Suppressions.Conflict.Add(new HsmConflictSuppressionDto { VariableName = varName, WriterPairKey = writerKey });
        foreach (var varName in asset.GetUnusedSuppressions())
            dto.Suppressions.Unused.Add(varName);

        // Blackboard (§5.4)
        dto.Blackboard = BlackboardToDto(asset);

        return dto;
    }

    // ── DTO → Model ───────────────────────────────────────────────────────────

    /// <summary>
    /// Maps an HsmAssetDto (persisted form) to an HsmAsset (editor model).
    /// The returned asset has empty Blob/Metadata (runtime-only; filled in after assembly load).
    /// </summary>
    public static HsmAsset FromDto(HsmAssetDto dto)
        => ToModel(dto, string.Empty, true);

    /// <summary>
    /// Maps an HsmAssetDto to an HsmAsset, setting SourceFilePath and IsEditorOwned explicitly.
    /// Design §3 D4 / PU-301: used by the JSON file-based contributor so the loaded model
    /// carries the correct SourceFilePath and ownership flag.
    /// </summary>
    public static HsmAsset ToModel(
        HsmAssetDto dto,
        string sourceFilePath,
        bool isEditorOwned)
    {
        // Build empty blob / metadata placeholders (runtime-only fields)
        var emptyBlob     = new HsmDefinitionBlob();
        var emptyMetadata = new MachineMetadata();

        // Build state nodes
        var stableIdToState = new Dictionary<Guid, StateNode>();
        var stateNodes = new List<StateNode>();
        var rootState = new StateNode("__root__") { StableId = Guid.NewGuid() };

        foreach (var sDto in dto.States)
        {
            var state = new StateNode(sDto.Name)
            {
                StableId      = sDto.StableId,
                IsInitial     = sDto.IsInitial,
                IsHistory     = sDto.IsHistory,
                IsDeepHistory = sDto.IsDeepHistory,
                IsParallel    = sDto.IsParallel,
                IsFinal       = sDto.IsFinal,
                OnEntryAction = sDto.OnEntryAction,
                OnExitAction  = sDto.OnExitAction,
                ActivityAction = sDto.ActivityAction,
                TimerAction   = sDto.TimerAction,
                RegionIndex   = sDto.RegionIndex,
                Position      = new Vector2(sDto.X, sDto.Y),
                Comment       = sDto.Comment,
                IsCollapsed   = sDto.IsCollapsed,
                ColorOverride = sDto.ColorOverride,
                FlatIndex     = 0,   // runtime-only; will be filled after assembly reload
            };
            if (sDto.SizeOverrideX.HasValue && sDto.SizeOverrideY.HasValue)
                state.SizeOverride = new Vector2(sDto.SizeOverrideX.Value, sDto.SizeOverrideY.Value);

            stableIdToState[sDto.StableId] = state;
            stateNodes.Add(state);
        }

        // Wire parent→child relationships
        foreach (var sDto in dto.States)
        {
            if (!stableIdToState.TryGetValue(sDto.StableId, out var state)) continue;

            if (sDto.ParentStableId.HasValue &&
                stableIdToState.TryGetValue(sDto.ParentStableId.Value, out var parent))
            {
                state.Parent = parent;
                if (!parent.Children.Contains(state))
                    parent.Children.Add(state);
            }
            else
            {
                // Top-level state: parent is the synthetic root
                state.Parent = rootState;
                if (!rootState.Children.Contains(state))
                    rootState.Children.Add(state);
            }
        }

        // Build regions
        var regionNodes = new List<RegionNode>();
        var stableIdToRegion = new Dictionary<Guid, RegionNode>();
        foreach (var rDto in dto.Regions)
        {
            var region = new RegionNode(rDto.Name)
            {
                StableId      = rDto.StableId,
                RegionIndex   = rDto.RegionIndex,
                Priority      = rDto.Priority,
                Comment       = rDto.Comment,
                ColorOverride = rDto.ColorOverride,
            };
            if (rDto.InitialChildStableId.HasValue &&
                stableIdToState.TryGetValue(rDto.InitialChildStableId.Value, out var initChild))
                region.InitialChild = initChild;

            regionNodes.Add(region);
            stableIdToRegion[rDto.StableId] = region;
        }

        // Build transitions
        var transitions = new List<TransitionNode>();
        foreach (var tDto in dto.Transitions)
        {
            if (!stableIdToState.TryGetValue(tDto.SourceStableId, out var src)) continue;
            if (!stableIdToState.TryGetValue(tDto.TargetStableId, out var tgt)) continue;

            var t = new TransitionNode
            {
                VisualId       = tDto.VisualId,
                Source         = src,
                Target         = tgt,
                EventName      = tDto.EventName,
                GuardFunction  = tDto.GuardFunction,
                ActionFunction = tDto.ActionFunction,
                Priority       = tDto.Priority,
                Kind           = (TransitionKind)tDto.Kind,
                SyncGroupId    = tDto.SyncGroupId,
                Comment        = tDto.Comment,
                FlatIndex      = 0,   // runtime-only
                EventId        = 0,   // runtime-only
            };
            foreach (var wp in tDto.Waypoints)
                t.Waypoints.Add(new Vector2(wp.X, wp.Y));
            transitions.Add(t);
            src.OutgoingTransitions.Add(t);
        }

        // Build global transitions
        var globalTransitions = new List<GlobalTransitionNode>();
        foreach (var gDto in dto.GlobalTransitions)
        {
            if (!stableIdToState.TryGetValue(gDto.TargetStableId, out var tgt)) continue;
            globalTransitions.Add(new GlobalTransitionNode
            {
                VisualId       = gDto.VisualId,
                Target         = tgt,
                EventName      = gDto.EventName,
                GuardFunction  = gDto.GuardFunction,
                ActionFunction = gDto.ActionFunction,
                Priority       = gDto.Priority,
                Comment        = gDto.Comment,
                FlatIndex      = 0,   // runtime-only
                EventId        = 0,   // runtime-only
            });
        }

        // Build events; EventId is stored in DTO for emit-core byte-identity.
        // For new assets created from JSON (PU-03+), IDs will be reassigned sequentially.
        var events = new List<EventDefinition>();
        var eventNameToId = new Dictionary<string, ushort>(StringComparer.Ordinal);
        ushort fallbackId = 1;
        foreach (var eDto in dto.Events)
        {
            // Use stored EventId when present (non-zero); fall back to sequential for new JSON assets.
            ushort id = eDto.EventId != 0 ? eDto.EventId : fallbackId;
            var ev = new EventDefinition(eDto.Name, id)
            {
                PayloadSize  = eDto.PayloadSize,
                IsIndirect   = eDto.IsIndirect,
                IsDeferrable = eDto.IsDeferrable,
            };
            events.Add(ev);
            eventNameToId[eDto.Name] = id;
            if (eDto.EventId == 0) fallbackId++;
        }

        // Restore DeferredEventIds from DeferredEventNames
        foreach (var sDto in dto.States)
        {
            if (sDto.DeferredEventNames.Count > 0 &&
                stableIdToState.TryGetValue(sDto.StableId, out var state))
            {
                foreach (var evName in sDto.DeferredEventNames)
                {
                    if (eventNameToId.TryGetValue(evName, out var id))
                        state.DeferredEventIds.Add(id);
                }
            }
        }

        var asset = new HsmAsset(
            dto.AssetId,
            dto.Name,
            sourceFilePath:    sourceFilePath,
            isEditorOwned:     isEditorOwned,
            dto.TargetNamespace,
            emptyBlob,
            emptyMetadata,
            rootState,
            stateNodes,
            transitions,
            globalTransitions,
            regionNodes,
            events);

        // Blackboard type name is set in constructor via SanitizeIdentifier(name);
        // override with persisted value if it differs.
        // (BlackboardTypeName has a setter)
        asset.BlackboardTypeName = dto.BlackboardTypeName;

        asset.CanvasPanOffset = new Vector2(dto.Canvas.PanX, dto.Canvas.PanY);
        asset.CanvasZoomLevel = dto.Canvas.Zoom;

        // Suppressions
        foreach (var s in dto.Suppressions.Conflict)
            asset.SetConflictSuppressed(s.VariableName, s.WriterPairKey, true);
        foreach (var varName in dto.Suppressions.Unused)
            asset.SetUnusedWarningSuppressed(varName, true);

        // Blackboard variables
        var vars = BlackboardFromDto(dto.Blackboard);
        if (vars.Count > 0)
            asset.SetBlackboardVariables(vars);

        asset.ClearDirty();
        return asset;
    }

    // ── Blackboard mapping (§5.4) ─────────────────────────────────────────────

    private static HsmBlackboardBlockDto BlackboardToDto(HsmAsset asset)
    {
        var block = new HsmBlackboardBlockDto
        {
            Managed      = asset.IsBlackboardEditorManaged,
            TypeName     = asset.BlackboardTypeName,
            HeavyDtoType = null,
        };

        foreach (var v in asset.BlackboardVariables)
        {
            var typeId = v.FieldType.FullName ?? v.FieldType.Name;
            block.Variables.Add(new HsmBlackboardVariableDto
            {
                Name = v.Name,
                Type = new HsmBlackboardTypeRefDto
                {
                    TypeId      = typeId,
                    IsArray     = false,
                    FixedLength = null,
                },
                DefaultValueJson = null,
                Comment          = v.Comment,
            });
        }

        return block;
    }

    private static List<BlackboardVariableEntry> BlackboardFromDto(HsmBlackboardBlockDto block)
    {
        var result = new List<BlackboardVariableEntry>();
        foreach (var v in block.Variables)
        {
            var clrType = ResolveClrType(v.Type.TypeId);
            result.Add(new BlackboardVariableEntry(v.Name, clrType, v.Comment));
        }
        return result;
    }

    private static Type ResolveClrType(string typeId)
    {
        var primitive = BlackboardTypeHelper.GetPrimitiveType(typeId);
        if (primitive != null) return primitive;

        var t = Type.GetType(typeId);
        if (t != null) return t;

        foreach (var name in BlackboardTypeHelper.DefaultKnownTypeNames)
        {
            var pt = BlackboardTypeHelper.GetPrimitiveType(name);
            if (pt != null && (pt.FullName == typeId || pt.Name == typeId)) return pt;
        }

        return typeof(object);
    }
}
