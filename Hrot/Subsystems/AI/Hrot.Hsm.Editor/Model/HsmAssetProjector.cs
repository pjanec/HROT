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

        // Apply StableIds from layout or mint fresh ones
        if (layout != null)
        {
            // The layout stores StableIds as dict keys.
            // We need to match by flat index via name since layout keys are Guids.
            // For now: try to find a state in the layout by matching every stored Guid
            // to a state by flat index using the metadata name mapping.
            // Simple approach: assign StableIds from layout if the count matches (positional).
            // The layout maps Guid -> StateLayoutEntry; we need to map FlatIndex -> Guid.
            // Since we don't have that reverse mapping here, we store all layout Guids
            // and assign to states sequentially if counts match. This is a stub;
            // the full HsmEditorLayoutBuilder records state stable IDs by Guid key.
            // When HsmEditorLayoutBuilder.State(stableIdString, ...) is called,
            // it stores [Guid -> StateLayoutEntry]. We'll look up by matching the GUID
            // stored in the layout to a state's mint GUID.
            // Real approach: use a name->stableId mapping passed from the layout method.
            // For now: for each state, if the layout has an entry, apply position/size/comment.
            foreach (var (stableId, entry) in layout.States)
            {
                // This state's StableId is the key; find or apply to a matching state.
                // We do an O(n) scan to find if any existing stateNode's current StableId matches.
                // Since StableIds are freshly minted (Guid.NewGuid()), no match is expected yet.
                // The correct implementation assigns StableId from layout before minting.
                // For this batch, we apply positions to states by array order if the
                // layout entry count matches the state count (simple heuristic).
                // TODO: full round-trip via HsmFluentEmitter (HS-S1-05)
            }

            // Apply layout positions in order of layout.States (sorted by Guid key)
            // to states in flat-index order. Not ideal but serviceable for initial support.
            // Full round-trip via HsmFluentEmitter (HS-S1-05) will fix ordering.
            var layoutStateKeys = new List<Guid>(layout.States.Keys);
            layoutStateKeys.Sort();
            for (int i = 0; i < Math.Min(layoutStateKeys.Count, stateNodes.Length); i++)
            {
                var key = layoutStateKeys[i];
                var entry = layout.States[key];
                stateNodes[i].StableId = key;
                stateNodes[i].Position = entry.Position;
                if (entry.SizeOverride.HasValue) stateNodes[i].Size = entry.SizeOverride.Value;
                stateNodes[i].Comment = entry.Comment;
                stateNodes[i].IsCollapsed = entry.Collapsed;
                stateNodes[i].ColorOverride = entry.Color;
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

        // Apply transition layout (VisualIds + waypoints + comments)
        if (layout != null)
        {
            var layoutTransKeys = new List<Guid>(layout.Transitions.Keys);
            layoutTransKeys.Sort();
            for (int i = 0; i < Math.Min(layoutTransKeys.Count, transitionNodes.Count); i++)
            {
                var key = layoutTransKeys[i];
                var entry = layout.Transitions[key];
                transitionNodes[i].VisualId = key;
                transitionNodes[i].Waypoints.AddRange(entry.Waypoints);
                transitionNodes[i].Comment = entry.Comment;
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
                stateNodes[def.ParentStateIndex].Regions.Add(rn);
            if (def.InitialStateIndex < stateNodes.Length)
                rn.InitialChild = stateNodes[def.InitialStateIndex];

            regionNodes.Add(rn);
        }

        // Apply region layout
        if (layout != null)
        {
            var layoutRegionKeys = new List<Guid>(layout.Regions.Keys);
            layoutRegionKeys.Sort();
            for (int i = 0; i < Math.Min(layoutRegionKeys.Count, regionNodes.Count); i++)
            {
                var key = layoutRegionKeys[i];
                var entry = layout.Regions[key];
                regionNodes[i].StableId = key;
                regionNodes[i].Comment = entry.Comment;
                regionNodes[i].ColorOverride = entry.Color;
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
