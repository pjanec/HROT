using NodeEditor.Core.Action;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// Thin holder for a single long-lived shell-level <see cref="IEditorCommands"/> set.
/// Owned by <see cref="WindowManager"/>, subsystems register their global editor commands
/// here once at startup (scenario lifecycle, AI-debug stepping, open-browser, etc.).
/// Per-document command sets are separate and unchanged.
/// </summary>
public sealed class ShellEditorCommands : IEditorCommands
{
    private readonly EditorCommandsImpl _impl = new();

    /// <summary>
    /// Register a shell-level command. Called at editor startup by subsystems.
    /// </summary>
    /// <param name="descriptor">Static command metadata.</param>
    /// <param name="action">The handler to invoke when the command is executed.</param>
    public void Register(EditorCommandDescriptor descriptor, System.Action<EditorCommandContext> action)
        => _impl.Register(descriptor, action);

    /// <inheritdoc/>
    public IReadOnlyList<EditorCommandDescriptor> All => _impl.All;

    /// <inheritdoc/>
    public EditorCommandDescriptor? Get(string commandId) => _impl.Get(commandId);

    /// <inheritdoc/>
    public EditorCommandResult Invoke(string commandId, EditorCommandContext? ctx = null)
        => _impl.Invoke(commandId, ctx);

    /// <inheritdoc/>
    public event System.Action<string>? AvailabilityChanged
    {
        add => _impl.AvailabilityChanged += value;
        remove => _impl.AvailabilityChanged -= value;
    }

    /// <summary>
    /// Trigger an <see cref="AvailabilityChanged"/> event for a command id.
    /// </summary>
    public void NotifyAvailabilityChanged(string commandId)
        => _impl.NotifyAvailabilityChanged(commandId);
}
