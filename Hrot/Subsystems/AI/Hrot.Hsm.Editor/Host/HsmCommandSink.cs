using System;
using System.Collections.Generic;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;

namespace Hrot.Hsm.Editor.Host;

/// <summary>
/// Command sink for the HSM canvas.
/// Dispatches editor-initiated mutations to per-command stub handlers.
/// Each stub marks the asset dirty so callers receive change notifications.
/// </summary>
internal sealed class HsmCommandSink : IGraphCommandSink
{
    private readonly HsmAsset _asset;

    internal HsmCommandSink(HsmAsset asset)
    {
        _asset = asset;
    }

    public GraphCommandResult Apply(GraphCommand command)
    {
        switch (command)
        {
            case GraphCommand.MoveNodes cmd:
                ApplyMoveNodes(cmd);
                break;
            case GraphCommand.AddNode cmd:
                ApplyAddNode(cmd);
                break;
            case GraphCommand.RemoveNodes cmd:
                ApplyRemoveNodes(cmd);
                break;
            case GraphCommand.AddLink cmd:
                ApplyAddLink(cmd);
                break;
            case GraphCommand.RemoveLinks cmd:
                ApplyRemoveLinks(cmd);
                break;
            case GraphCommand.SetNodeProperty cmd:
                ApplySetNodeProperty(cmd);
                break;
            case GraphCommand.ChangeParent cmd:
                ApplyChangeParent(cmd);
                break;
            case GraphCommand.SetContainerCollapsed cmd:
                ApplySetContainerCollapsed(cmd);
                break;
            case GraphCommand.AddRegion cmd:
                ApplyAddRegion(cmd);
                break;
            case GraphCommand.RemoveRegion cmd:
                ApplyRemoveRegion(cmd);
                break;
            case GraphCommand.ReorderRegions cmd:
                ApplyReorderRegions(cmd);
                break;
            case GraphCommand.AddAttachment cmd:
                ApplyAddAttachment(cmd);
                break;
            case GraphCommand.RemoveAttachments cmd:
                ApplyRemoveAttachments(cmd);
                break;
            case GraphCommand.Batch cmd:
                foreach (var sub in cmd.Commands)
                {
                    var result = Apply(sub);
                    if (!result.Success)
                        return result;
                }
                break;
            default:
                return new GraphCommandResult(false, $"Unsupported: {command.GetType().Name}");
        }

        _asset.MarkDirty();
        return new GraphCommandResult(true, null);
    }

    // ---- Per-command stubs (populated in later tasks) ----

    private void ApplyMoveNodes(GraphCommand.MoveNodes cmd)           { /* TODO */ }
    private void ApplyAddNode(GraphCommand.AddNode cmd)               { /* TODO */ }
    private void ApplyRemoveNodes(GraphCommand.RemoveNodes cmd)       { /* TODO */ }
    private void ApplyAddLink(GraphCommand.AddLink cmd)               { /* TODO */ }
    private void ApplyRemoveLinks(GraphCommand.RemoveLinks cmd)       { /* TODO */ }
    private void ApplySetNodeProperty(GraphCommand.SetNodeProperty cmd) { /* TODO */ }
    private void ApplyChangeParent(GraphCommand.ChangeParent cmd)     { /* TODO */ }
    private void ApplySetContainerCollapsed(GraphCommand.SetContainerCollapsed cmd) { /* TODO */ }
    private void ApplyAddRegion(GraphCommand.AddRegion cmd)
    {
        var state = _asset.FindStateByStableId(cmd.ContainerId.Value);
        if (state is null) return;

        var region = new RegionNode(cmd.RegionName) { Priority = (byte)cmd.Priority };
        int insertAt = Math.Clamp(cmd.InsertAtIndex, 0, state.RegionNodes.Count);
        state.RegionNodes.Insert(insertAt, region);

        // Reindex all regions so RegionIndex stays contiguous.
        for (int i = 0; i < state.RegionNodes.Count; i++)
            state.RegionNodes[i].RegionIndex = (byte)i;

        _asset.RegisterRegion(region);
    }

    private void ApplyRemoveRegion(GraphCommand.RemoveRegion cmd)
    {
        var state = _asset.FindStateByStableId(cmd.ContainerId.Value);
        if (state is null) return;
        if (cmd.RegionIndex < 0 || cmd.RegionIndex >= state.RegionNodes.Count) return;

        var region = state.RegionNodes[cmd.RegionIndex];

        // Redistribute children of the removed region.
        switch (cmd.Policy)
        {
            case ChildRedistributionPolicy.MoveToFirstRegion:
                // Move children to region 0 if it is different from the one being removed.
                int targetRegion = cmd.RegionIndex == 0 ? 1 : 0;
                if (targetRegion < state.RegionNodes.Count)
                {
                    foreach (var child in state.Children)
                        if (child.RegionIndex == cmd.RegionIndex)
                            child.RegionIndex = targetRegion;
                }
                else
                {
                    // No other region to move to; leave children with index 0.
                    foreach (var child in state.Children)
                        if (child.RegionIndex == cmd.RegionIndex)
                            child.RegionIndex = 0;
                }
                break;

            case ChildRedistributionPolicy.MoveToParent:
                // Promote children to no-region (index 0, parent owns them).
                foreach (var child in state.Children)
                    if (child.RegionIndex == cmd.RegionIndex)
                        child.RegionIndex = 0;
                break;

            case ChildRedistributionPolicy.DeleteChildren:
                // Remove children from the state's child list.
                state.Children.RemoveAll(c => c.RegionIndex == cmd.RegionIndex);
                break;
        }

        state.RegionNodes.RemoveAt(cmd.RegionIndex);
        _asset.UnregisterRegion(region);

        // Reindex remaining regions.
        for (int i = 0; i < state.RegionNodes.Count; i++)
            state.RegionNodes[i].RegionIndex = (byte)i;
    }

    private void ApplyReorderRegions(GraphCommand.ReorderRegions cmd)
    {
        var state = _asset.FindStateByStableId(cmd.ContainerId.Value);
        if (state is null) return;
        if (cmd.NewOrder.Count != state.RegionNodes.Count) return;

        var reordered = new List<RegionNode>(state.RegionNodes.Count);
        foreach (var oldIndex in cmd.NewOrder)
        {
            if (oldIndex < 0 || oldIndex >= state.RegionNodes.Count) return;
            reordered.Add(state.RegionNodes[oldIndex]);
        }

        state.RegionNodes.Clear();
        state.RegionNodes.AddRange(reordered);

        // Reindex so RegionIndex matches the new positions.
        for (int i = 0; i < state.RegionNodes.Count; i++)
            state.RegionNodes[i].RegionIndex = (byte)i;
    }

    private void ApplyAddAttachment(GraphCommand.AddAttachment cmd)
    {
        var att = new HsmAttachment(
            cmd.NewId,
            cmd.HostNodeId,
            cmd.Category,
            cmd.Glyph,
            cmd.Label,
            cmd.Tooltip,
            cmd.StackIndex,
            cmd.HostProperties);
        _asset.AddAttachment(att);
    }

    private void ApplyRemoveAttachments(GraphCommand.RemoveAttachments cmd)
    {
        _asset.RemoveAttachments(cmd.AttachmentIds);
    }
}
