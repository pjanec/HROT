namespace NodeEditor.Core.Action;

/// <summary>
/// Default implementation of <see cref="IEditorCommands"/>.
/// Holds a registry of <see cref="EditorCommandDescriptor"/> objects and
/// delegates invocations to their registered action.
/// </summary>
public sealed class EditorCommandsImpl : IEditorCommands
{
    private readonly Dictionary<string, RegisteredCommand> _commands = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public IReadOnlyList<EditorCommandDescriptor> All { get; private set; } =
        Array.Empty<EditorCommandDescriptor>();

    /// <inheritdoc/>
    public EditorCommandDescriptor? Get(string commandId) =>
        _commands.TryGetValue(commandId, out var c) ? c.Descriptor : null;

    /// <inheritdoc/>
    public EditorCommandResult Invoke(string commandId, EditorCommandContext? ctx = null)
    {
        if (!_commands.TryGetValue(commandId, out var cmd))
            return new EditorCommandResult(false, $"Unknown command: {commandId}");

        if (!cmd.Descriptor.IsEnabled())
            return new EditorCommandResult(false, "Command not enabled.");

        try
        {
            cmd.Action(ctx ?? default);
            return new EditorCommandResult(true, null);
        }
        catch (Exception ex)
        {
            return new EditorCommandResult(false, ex.Message);
        }
    }

    /// <inheritdoc/>
    public event System.Action<string>? AvailabilityChanged;

    /// <summary>Register a command. Called at editor startup by the host or by the editor itself.</summary>
    public void Register(EditorCommandDescriptor descriptor, System.Action<EditorCommandContext> action)
    {
        _commands[descriptor.Id] = new RegisteredCommand(descriptor, action);
        RebuildList();
    }

    /// <summary>Trigger an <see cref="AvailabilityChanged"/> event for a command id.</summary>
    public void NotifyAvailabilityChanged(string commandId) =>
        AvailabilityChanged?.Invoke(commandId);

    private void RebuildList()
    {
        All = _commands.Values.Select(c => c.Descriptor).ToList();
    }

    private readonly record struct RegisteredCommand(
        EditorCommandDescriptor Descriptor,
        System.Action<EditorCommandContext> Action);
}
