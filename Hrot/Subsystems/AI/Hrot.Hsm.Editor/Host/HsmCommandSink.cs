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
    private void ApplyAddRegion(GraphCommand.AddRegion cmd)           { /* TODO */ }
    private void ApplyRemoveRegion(GraphCommand.RemoveRegion cmd)     { /* TODO */ }
    private void ApplyReorderRegions(GraphCommand.ReorderRegions cmd) { /* TODO */ }
    private void ApplyAddAttachment(GraphCommand.AddAttachment cmd)   { /* TODO */ }
    private void ApplyRemoveAttachments(GraphCommand.RemoveAttachments cmd) { /* TODO */ }
}
