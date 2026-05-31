using System;
using System.Collections.Generic;
using System.Numerics;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Layout;
using Hrot.Hsm.Editor.Layout;

namespace Hrot.Hsm.Editor.Model;

// Projects a compiled HsmDefinitionBlob + MachineMetadata + optional HsmEditorLayout
// into a mutable HsmAsset suitable for use in the editor.
internal static class HsmAssetProjector
{
    internal const ushort NoParent = 0xFFFF;

    // Projects the given blob into an HsmAsset.
    // assetId: stable identity derived from machine name.
    // machineName: display name.
    // sourceFilePath: path to the .cs source file (may be empty).
    // isEditorOwned: true if the editor generated this file.
    // assemblyNamespace: namespace hint for code generation (may be empty).
    public static HsmAsset Project(
        HsmDefinitionBlob blob,
        MachineMetadata? metadata,
        HsmEditorLayout? layout,
        Guid assetId,
        string machineName,
        string sourceFilePath,
        bool isEditorOwned,
        string assemblyNamespace)
    {
        metadata ??= new MachineMetadata();
        var states = blob.States;
        var transitions = blob.Transitions;
        var regions = blob.Regions;
        var globalTransitions = blob.GlobalTransitions;

        // === Build StateNode list ===
        // stateNodes[i] corresponds to blob.States[i]
        var stateNodes = new StateNode[states.Length];
        for (int i = 0; i < states.Length; i++)
        {
            var name = metadata.GetStateName((ushort)i);
            var node = new StateNode(name);
            node.FlatIndex = (ushort)i;

            var def = states[i];
            node.IsInitial     = (def.Flags & StateFlags.IsInitial)     != 0;
            node.IsHistory     = (def.Flags & StateFlags.IsHistory)     != 0;
            node.IsDeepHistory = (def.Flags & StateFlags.IsDeepHistory) != 0;
            node.IsParallel    = (def.Flags & StateFlags.IsParallel)    != 0;
            node.IsFinal       = (def.Flags & StateFlags.IsFinal)       != 0;
            node.OutputLaneMask = def.OutputLaneMask;

            // Resolve action names from metadata (0xFFFF = no action)
            if (def.OnEntryActionId  != 0xFFFF) node.OnEntryAction  = metadata.GetActionName(def.OnEntryActionId);
            if (def.OnExitActionId   != 0xFFFF) node.OnExitAction   = metadata.GetActionName(def.OnExitActionId);
            if (def.ActivityActionId != 0xFFFF) node.ActivityAction = metadata.GetActionName(def.ActivityActionId);
            if (def.TimerActionId    != 0xFFFF) node.TimerAction    = metadata.GetActionName(def.TimerActionId);

            // BPF-011: populate deferred event IDs from metadata (keyed by flat index).
            if (metadata.DeferredEventsByState.TryGetValue((ushort)i, out var deferredIds))
            {
                foreach (var id in deferredIds)
                    node.DeferredEventIds.Add(id);
            }

            stateNodes[i] = node;
        }

        // Build parent/child links
        var rootState = new StateNode("__root__");
        rootState.FlatIndex = 0xFFFF;

        for (int i = 0; i < states.Length; i++)
        {
            var def = states[i];
            StateNode parentNode;
            if (def.ParentIndex == NoParent)
            {
                parentNode = rootState;
            }
            else if (def.ParentIndex < stateNodes.Length)
            {
                parentNode = stateNodes[def.ParentIndex];
            }
            else
            {
                parentNode = rootState;
            }
            stateNodes[i].Parent = parentNode;
            parentNode.Children.Add(stateNodes[i]);
        }

        // BPF-025: assign StableIds from metadata (content-based, keyed by FlatIndex),
        // then apply layout data using each state's resolved StableId as the lookup key.
        for (int i = 0; i < stateNodes.Length; i++)
        {
            if (!metadata.StateStableIds.TryGetValue((ushort)i, out var sid) || sid == Guid.Empty)
                sid = Guid.NewGuid(); // fallback for states not in metadata
            stateNodes[i].StableId = sid;
        }

        if (layout != null)
        {
            // Apply layout positions and visual properties to each state by its StableId.
            foreach (var sn in stateNodes)
            {
                if (!layout.States.TryGetValue(sn.StableId, out var entry)) continue;
                sn.Position   = entry.Position;
                if (entry.SizeOverride.HasValue) sn.SizeOverride = entry.SizeOverride.Value;
                sn.Comment     = entry.Comment;
                sn.IsCollapsed = entry.Collapsed;
                sn.ColorOverride = entry.Color;
            }
        }

        // === Build TransitionNode list ===
        var transitionNodes = new List<TransitionNode>(transitions.Length);
        for (int i = 0; i < transitions.Length; i++)
        {
            var def = transitions[i];
            var tn = new TransitionNode
            {
                FlatIndex = (ushort)i,
                VisualId  = Guid.NewGuid(),  // replaced from layout below
                EventId   = def.EventId,
                SyncGroupId = def.SyncGroupId,
                Priority  = ExtractPriority(def.Flags),
                Kind      = ExtractKind(def.Flags),
            };

            if (def.SourceStateIndex < stateNodes.Length)
                tn.Source = stateNodes[def.SourceStateIndex];
            if (def.TargetStateIndex < stateNodes.Length)
                tn.Target = stateNodes[def.TargetStateIndex];

            if (def.EventId != 0)
                tn.EventName = metadata.GetEventName(def.EventId);
            if (def.GuardId != 0xFFFF)
                tn.GuardFunction = metadata.GetActionName(def.GuardId);
            if (def.ActionId != 0xFFFF)
                tn.ActionFunction = metadata.GetActionName(def.ActionId);

            // Register with source state's outgoing list
            if (def.SourceStateIndex < stateNodes.Length)
                stateNodes[def.SourceStateIndex].OutgoingTransitions.Add(tn);

            transitionNodes.Add(tn);
        }

        // Apply transition layout (VisualIds + waypoints + comments).
        // BPF-012: use metadata.TransitionVisualIds to match index to stable VisualId
        // so that deleting a transition does not shift IDs for surviving transitions.
        for (int i = 0; i < transitionNodes.Count; i++)
        {
            if (metadata.TransitionVisualIds.TryGetValue((ushort)i, out var vid))
                transitionNodes[i].VisualId = vid;
        }
        if (layout != null)
        {
            for (int i = 0; i < transitionNodes.Count; i++)
            {
                if (layout.Transitions.TryGetValue(transitionNodes[i].VisualId, out var entry))
                {
                    transitionNodes[i].Waypoints.AddRange(entry.Waypoints);
                    transitionNodes[i].Comment = entry.Comment;
                }
            }
        }

        // === Build RegionNode list ===
        var regionNodes = new List<RegionNode>(regions.Length);
        for (int i = 0; i < regions.Length; i++)
        {
            var def = regions[i];
            var rn = new RegionNode($"Region{i}")
            {
                RegionIndex = (byte)i,
                Priority    = def.Priority,
            };

            if (def.ParentStateIndex < stateNodes.Length)
                stateNodes[def.ParentStateIndex].RegionNodes.Add(rn);
            if (def.InitialStateIndex < stateNodes.Length)
                rn.InitialChild = stateNodes[def.InitialStateIndex];

            regionNodes.Add(rn);
        }

        // Apply region layout.
        // BPF-012: use RegionIndex stored in each layout entry to match by structural
        // position rather than sorted Guid order, so IDs survive region deletion.
        if (layout != null)
        {
            var regionIndexToStableId = new Dictionary<int, Guid>(layout.Regions.Count);
            foreach (var (key, entry) in layout.Regions)
                regionIndexToStableId[entry.RegionIndex] = key;

            for (int i = 0; i < regionNodes.Count; i++)
            {
                if (!regionIndexToStableId.TryGetValue(i, out var sid)) continue;
                regionNodes[i].StableId = sid;
                if (layout.Regions.TryGetValue(sid, out var entry))
                {
                    regionNodes[i].Comment = entry.Comment;
                    regionNodes[i].ColorOverride = entry.Color;
                }
            }
        }

        // === Build GlobalTransitionNode list ===
        var globalTransNodes = new List<GlobalTransitionNode>(globalTransitions.Length);
        for (int i = 0; i < globalTransitions.Length; i++)
        {
            var def = globalTransitions[i];
            var gtn = new GlobalTransitionNode
            {
                FlatIndex    = (ushort)i,
                VisualId     = Guid.NewGuid(),
                EventId      = def.EventId,
                Priority     = def.Priority,
            };

            if (def.TargetStateIndex < stateNodes.Length)
                gtn.Target = stateNodes[def.TargetStateIndex];

            if (def.EventId != 0)
                gtn.EventName = metadata.GetEventName(def.EventId);
            if (def.GuardId != 0xFFFF)
                gtn.GuardFunction = metadata.GetActionName(def.GuardId);
            if (def.ActionId != 0xFFFF)
                gtn.ActionFunction = metadata.GetActionName(def.ActionId);

            globalTransNodes.Add(gtn);
        }

        // === Build EventDefinition list from metadata ===
        var eventDefs = new List<EventDefinition>();
        foreach (var (eventId, eventName) in metadata.EventNames)
        {
            var ed = new EventDefinition(eventName, eventId);
            // Mark HasGlobalTransition
            foreach (var gt in globalTransNodes)
            {
                if (gt.EventId == eventId) { ed.HasGlobalTransition = true; break; }
            }
            eventDefs.Add(ed);
        }
        eventDefs.Sort((a, b) => a.EventId.CompareTo(b.EventId));

        // === Construct the asset ===
        var allStatesList = new List<StateNode>(stateNodes);
        var asset = new HsmAsset(
            assetId, machineName, sourceFilePath, isEditorOwned, assemblyNamespace,
            blob, metadata,
            rootState, allStatesList, transitionNodes,
            globalTransNodes, regionNodes, eventDefs);

        if (layout != null)
        {
            if (layout.BlackboardConflictSuppressions != null)
            {
                foreach (var sup in layout.BlackboardConflictSuppressions)
                {
                    asset.SetConflictSuppressed(sup.VariableName, sup.WriterPairKey, true);
                }
            }
            if (layout.UnusedWarningSuppressions != null)
            {
                foreach (var sup in layout.UnusedWarningSuppressions)
                {
                    asset.SetUnusedWarningSuppressed(sup, true);
                }
            }
        }

        // Run auto-layout if no layout was provided
        if (layout == null || layout.States.Count == 0)
            HsmAutoLayout.Layout(asset);

        return asset;
    }

    private static byte ExtractPriority(TransitionFlags flags)
    {
        // Priority is stored in bits 8-11 of TransitionFlags
        // (per HsmFlattener.BuildTransitionFlags which uses (priority & 0x0F) << 8)
        return (byte)(((ushort)flags >> 8) & 0x0F);
    }

    private static TransitionKind ExtractKind(TransitionFlags flags)
    {
        if ((flags & TransitionFlags.IsInternal) != 0) return TransitionKind.Internal;
        // Local is not explicitly stored in current TransitionFlags; default External
        return TransitionKind.External;
    }
}
