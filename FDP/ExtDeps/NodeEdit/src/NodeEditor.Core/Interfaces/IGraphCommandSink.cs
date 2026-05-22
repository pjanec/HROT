using NodeEditor.Core.Commands;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Sink for all editor-initiated mutations. The host implements this to
/// apply commands to its data store. Multiple commands per user action
/// are batched via <see cref="GraphCommand.Batch"/>.
/// </summary>
public interface IGraphCommandSink
{
    /// <summary>Apply a command. The host should treat the command atomically.</summary>
    GraphCommandResult Apply(GraphCommand command);
}

/// <summary>Result of applying a command.</summary>
public readonly record struct GraphCommandResult(bool Success, string? Message);
